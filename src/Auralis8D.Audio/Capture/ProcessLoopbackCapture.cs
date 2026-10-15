using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Auralis8D.Audio.Interop.WASAPI;

namespace Auralis8D.Audio.Capture
{
    /// <summary>
    /// Captures live real-time audio exclusively from a specific Windows application process tree
    /// using Windows Application Loopback Capture (ActivateAudioInterfaceAsync on VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK).
    /// Manages the process's original session volume (muting while active, unmuting when stopped or on error).
    /// </summary>
    public sealed class ProcessLoopbackCapture : IDisposable
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private readonly int _targetPid;
        private readonly string _processName;
        private IAudioClient? _audioClient;
        private IAudioCaptureClient? _captureClient;
        private IntPtr _hCaptureEvent = IntPtr.Zero;
        private Thread? _captureThread;
        private volatile bool _isRunning;
        private bool _disposed;

        private uint _sampleRate = 48000;
        private ushort _channels = 2;
        private ushort _bitsPerSample = 32;
        private bool _isFloat = true;

        // Reference to original session volume for mute/unmute management
        private ISimpleAudioVolume? _sessionVolume;
        private bool _originalMuteState;
        private bool _didMuteOriginal;

        /// <summary>
        /// Callback fired when new audio samples are captured from the process.
        /// Buffer contains interleaved float stereo audio [-1.0, 1.0].
        /// </summary>
        public Action<float[], uint, uint>? AudioSamplesCaptured;

        /// <summary>
        /// Callback fired when the target process exits or capture errors out.
        /// </summary>
        public Action<string>? CaptureErrorOccurred;

        public int TargetProcessId => _targetPid;
        public string ProcessName => _processName;
        public uint SampleRate => _sampleRate;
        public ushort Channels => _channels;
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Initializes a new ProcessLoopbackCapture instance for the target process.
        /// </summary>
        /// <param name="targetPid">Target process ID.</param>
        /// <param name="processName">Process image name.</param>
        public ProcessLoopbackCapture(int targetPid, string processName)
        {
            _targetPid = targetPid;
            _processName = processName;
        }

        /// <summary>
        /// Starts capturing audio from the target application process.
        /// Mutes the original session to prevent duplicate audio, and launches the capture loop.
        /// </summary>
        public bool Start()
        {
            if (_isRunning) return true;

            try
            {
                // Verify process is alive
                try
                {
                    var proc = Process.GetProcessById(_targetPid);
                    if (proc.HasExited)
                    {
                        CaptureErrorOccurred?.Invoke($"Process {_processName} (PID {_targetPid}) is no longer running.");
                        return false;
                    }
                }
                catch (Exception)
                {
                    CaptureErrorOccurred?.Invoke($"Process {_processName} (PID {_targetPid}) not found.");
                    return false;
                }

                // 1. Locate and mute target process audio session on default playback endpoint
                TryMuteOriginalSession();

                // 2. Activate Application Loopback IAudioClient
                if (!ActivateProcessLoopbackClient())
                {
                    RestoreOriginalSessionMute();
                    return false;
                }

                // 3. Start capture thread
                _isRunning = true;
                _captureThread = new Thread(CaptureLoop)
                {
                    Name = $"Auralis8D_Capture_{_processName}_{_targetPid}",
                    Priority = ThreadPriority.AboveNormal,
                    IsBackground = true
                };
                _captureThread.Start();

                return true;
            }
            catch (Exception ex)
            {
                RestoreOriginalSessionMute();
                CaptureErrorOccurred?.Invoke($"Failed to start capture for {_processName}: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Activates the loopback IAudioClient for the target PID using ActivateAudioInterfaceAsync.
        /// </summary>
        private bool ActivateProcessLoopbackClient()
        {
            AUDIOCLIENT_ACTIVATION_PARAMS actParams = new AUDIOCLIENT_ACTIVATION_PARAMS
            {
                ActivationType = AUDIOCLIENT_ACTIVATION_TYPE.AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK,
                ProcessLoopbackParams = new AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS
                {
                    TargetProcessId = (uint)_targetPid,
                    ProcessLoopbackMode = PROCESS_LOOPBACK_MODE.PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE
                }
            };

            int structSize = Marshal.SizeOf<AUDIOCLIENT_ACTIVATION_PARAMS>();
            IntPtr pParams = Marshal.AllocHGlobal(structSize);
            Marshal.StructureToPtr(actParams, pParams, false);

            PROPVARIANT propVar = new PROPVARIANT
            {
                vt = 0x0041, // VT_BLOB
                pointerVal = (IntPtr)structSize,
                pointerVal2 = pParams
            };

            IntPtr pPropVar = Marshal.AllocHGlobal(Marshal.SizeOf<PROPVARIANT>());
            Marshal.StructureToPtr(propVar, pPropVar, false);

            ActivateAudioCompletionHandler handler = new ActivateAudioCompletionHandler();
            Guid iidAudioClient = typeof(IAudioClient).GUID;

            try
            {
                int hr = NativeAudioMethods.ActivateAudioInterfaceAsync(
                    AudioGuids.VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,
                    ref iidAudioClient,
                    pPropVar,
                    handler,
                    out _
                );

                if (hr != 0)
                {
                    CaptureErrorOccurred?.Invoke($"ActivateAudioInterfaceAsync returned error: 0x{hr:X8}");
                    return false;
                }

                if (!handler.WaitForCompletion(3000))
                {
                    CaptureErrorOccurred?.Invoke("Timed out waiting for process loopback audio interface activation.");
                    return false;
                }

                if (handler.ActivationResult != 0 || handler.ActivatedInterface == null)
                {
                    CaptureErrorOccurred?.Invoke($"Loopback activation completed with HRESULT: 0x{handler.ActivationResult:X8}");
                    return false;
                }

                _audioClient = (IAudioClient)handler.ActivatedInterface;

                // Configure capture stream using default render device format or standard 48kHz float
                IntPtr pFormat = GetDeviceMixFormat();
                if (pFormat == IntPtr.Zero)
                {
                    CaptureErrorOccurred?.Invoke("Failed to query audio mix format.");
                    return false;
                }

                try
                {
                    ParseAudioFormat(pFormat);

                    _hCaptureEvent = CreateEvent(IntPtr.Zero, false, false, null);
                    if (_hCaptureEvent == IntPtr.Zero)
                    {
                        return false;
                    }

                    long hnsBufferDuration = 200000; // 20 ms buffer for low latency
                    Guid sessionGuid = Guid.Empty;
                    int initHr = _audioClient.Initialize(
                        AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED,
                        AUDCLNT_STREAMFLAGS.AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS.AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
                        hnsBufferDuration,
                        0,
                        pFormat,
                        ref sessionGuid
                    );

                    if (initHr != 0)
                    {
                        CaptureErrorOccurred?.Invoke($"AudioClient Initialize failed: 0x{initHr:X8}");
                        return false;
                    }

                    _audioClient.SetEventHandle(_hCaptureEvent);

                    Guid captureClientGuid = typeof(IAudioCaptureClient).GUID;
                    _audioClient.GetService(ref captureClientGuid, out object captureObj);
                    _captureClient = (IAudioCaptureClient)captureObj;

                    _audioClient.Start();
                    return true;
                }
                finally
                {
                    if (pFormat != IntPtr.Zero)
                    {
                        Marshal.FreeCoTaskMem(pFormat);
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pParams);
                Marshal.FreeHGlobal(pPropVar);
            }
        }

        /// <summary>
        /// Retrieves the mix format for process loopback capture.
        /// Windows WASAPI process loopback virtual devices return E_NOTIMPL when calling GetMixFormat directly.
        /// To resolve this, we obtain the active mix format from the default render endpoint, or construct
        /// a standard 48kHz 32-bit IEEE float stereo format structure as fallback.
        /// </summary>
        /// <returns>Unmanaged pointer to WAVEFORMATEX or WAVEFORMATEXTENSIBLE allocated with CoTaskMemAlloc, which the caller must free.</returns>
        private IntPtr GetDeviceMixFormat()
        {
            IntPtr pFormat = IntPtr.Zero;
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IMMDevice defaultDevice);
                if (hr == 0 && defaultDevice != null)
                {
                    Guid iidAudioClient = typeof(IAudioClient).GUID;
                    int actHr = defaultDevice.Activate(ref iidAudioClient, 23, IntPtr.Zero, out object clientObj);
                    if (actHr == 0 && clientObj is IAudioClient renderClient)
                    {
                        renderClient.GetMixFormat(out pFormat);
                    }
                }
            }
            catch
            {
                // Fallback to manual format initialization if default device mix format is inaccessible
            }

            if (pFormat == IntPtr.Zero)
            {
                // Construct standard 48kHz stereo 32-bit IEEE float format
                WAVEFORMATEXTENSIBLE wfex = new WAVEFORMATEXTENSIBLE
                {
                    Format = new WAVEFORMATEX
                    {
                        wFormatTag = 0xFFFE, // WAVE_FORMAT_EXTENSIBLE
                        nChannels = 2,
                        nSamplesPerSec = 48000,
                        wBitsPerSample = 32,
                        nBlockAlign = (ushort)((2 * 32) / 8),
                        nAvgBytesPerSec = 48000 * ((2 * 32) / 8),
                        cbSize = 22 // Size of extensible structure beyond WAVEFORMATEX
                    },
                    wValidBitsPerSample = 32,
                    dwChannelMask = 0x3, // SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT
                    SubFormat = AudioGuids.KSDATAFORMAT_SUBTYPE_IEEE_FLOAT
                };
                int size = Marshal.SizeOf<WAVEFORMATEXTENSIBLE>();
                pFormat = Marshal.AllocCoTaskMem(size);
                Marshal.StructureToPtr(wfex, pFormat, false);
            }

            return pFormat;
        }

        /// <summary>
        /// Reads format information from WAVEFORMATEX or WAVEFORMATEXTENSIBLE.
        /// </summary>
        private void ParseAudioFormat(IntPtr pFormat)
        {
            var waveFormat = Marshal.PtrToStructure<WAVEFORMATEX>(pFormat);
            _sampleRate = waveFormat.nSamplesPerSec;
            _channels = waveFormat.nChannels;
            _bitsPerSample = waveFormat.wBitsPerSample;

            if (waveFormat.wFormatTag == 3) // WAVE_FORMAT_IEEE_FLOAT
            {
                _isFloat = true;
            }
            else if (waveFormat.wFormatTag == 0xFFFE) // WAVE_FORMAT_EXTENSIBLE
            {
                var extensible = Marshal.PtrToStructure<WAVEFORMATEXTENSIBLE>(pFormat);
                _isFloat = (extensible.SubFormat == AudioGuids.KSDATAFORMAT_SUBTYPE_IEEE_FLOAT);
            }
            else
            {
                _isFloat = false;
            }
        }

        /// <summary>
        /// Locates the target process's audio session on the default output device and mutes it.
        /// This ensures the user does not hear both the un-panned source and the 8D processed audio.
        /// </summary>
        private void TryMuteOriginalSession()
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IMMDevice defaultDevice);
                if (hr != 0 || defaultDevice == null) return;

                Guid sessionMgrGuid = typeof(IAudioSessionManager2).GUID;
                defaultDevice.Activate(ref sessionMgrGuid, 23, IntPtr.Zero, out object sessionMgrObj);
                var sessionMgr = (IAudioSessionManager2)sessionMgrObj;

                if (sessionMgr.GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum) == 0)
                {
                    sessionEnum.GetCount(out int count);
                    for (int i = 0; i < count; i++)
                    {
                        if (sessionEnum.GetSession(i, out IAudioSessionControl control) == 0)
                        {
                            if (control is IAudioSessionControl2 control2)
                            {
                                control2.GetProcessId(out uint pid);
                                if (pid == (uint)_targetPid)
                                {
                                    if (control is ISimpleAudioVolume volume)
                                    {
                                        _sessionVolume = volume;
                                        _sessionVolume.GetMute(out _originalMuteState);
                                        Guid ctx = Guid.Empty;
                                        _sessionVolume.SetMute(true, ref ctx);
                                        _didMuteOriginal = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Non-fatal if session muting fails, capture will still function
            }
        }

        /// <summary>
        /// Restores original mute status when stopping capture.
        /// </summary>
        private void RestoreOriginalSessionMute()
        {
            try
            {
                if (_sessionVolume != null && _didMuteOriginal)
                {
                    Guid ctx = Guid.Empty;
                    _sessionVolume.SetMute(_originalMuteState, ref ctx);
                    _didMuteOriginal = false;
                    _sessionVolume = null;
                }
            }
            catch (Exception)
            {
                // Ignore COM cleanup exceptions
            }
        }

        /// <summary>
        /// High-priority loop reading audio frames from WASAPI loopback capture buffer.
        /// </summary>
        private void CaptureLoop()
        {
            float[] sampleBuffer = new float[4096 * 2];
            using var waitHandle = new AutoResetEvent(false) { SafeWaitHandle = new Microsoft.Win32.SafeHandles.SafeWaitHandle(_hCaptureEvent, false) };

            while (_isRunning && _captureClient != null)
            {
                try
                {
                    // Wait for WASAPI audio buffer ready event (100ms timeout)
                    if (!waitHandle.WaitOne(100))
                    {
                        continue;
                    }

                    if (!_isRunning || _captureClient == null) break;

                    while (_captureClient.GetNextPacketSize(out uint packetFrames) == 0 && packetFrames > 0)
                    {
                        int hr = _captureClient.GetBuffer(
                            out IntPtr pData,
                            out uint framesRead,
                            out AUDCLNT_BUFFERFLAGS flags,
                            out _,
                            out _
                        );

                        if (hr != 0 || pData == IntPtr.Zero) break;

                        if (framesRead > 0)
                        {
                            uint totalSamples = framesRead * _channels;
                            if (sampleBuffer.Length < totalSamples)
                            {
                                sampleBuffer = new float[totalSamples * 2];
                            }

                            if ((flags & AUDCLNT_BUFFERFLAGS.AUDCLNT_BUFFERFLAGS_SILENT) != 0)
                            {
                                Array.Clear(sampleBuffer, 0, (int)totalSamples);
                            }
                            else
                            {
                                ConvertToFloatStereo(pData, framesRead, sampleBuffer);
                            }

                            AudioSamplesCaptured?.Invoke(sampleBuffer, framesRead, _sampleRate);
                        }

                        _captureClient.ReleaseBuffer(framesRead);
                    }
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                    {
                        CaptureErrorOccurred?.Invoke($"Capture loop exception: {ex.Message}");
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// Converts raw byte buffer into normalized stereo 32-bit float [-1.0, 1.0].
        /// </summary>
        private unsafe void ConvertToFloatStereo(IntPtr pData, uint frames, float[] destination)
        {
            if (_isFloat && _bitsPerSample == 32)
            {
                float* pFloat = (float*)pData.ToPointer();
                if (_channels == 2)
                {
                    Marshal.Copy(pData, destination, 0, (int)(frames * 2));
                }
                else if (_channels == 1)
                {
                    for (int i = 0; i < frames; i++)
                    {
                        float mono = pFloat[i];
                        destination[i * 2] = mono;
                        destination[i * 2 + 1] = mono;
                    }
                }
                else
                {
                    // Multichannel (e.g. 5.1/7.1): downmix front left and front right
                    for (int i = 0; i < frames; i++)
                    {
                        int baseIdx = i * _channels;
                        destination[i * 2] = pFloat[baseIdx];
                        destination[i * 2 + 1] = pFloat[baseIdx + 1];
                    }
                }
            }
            else if (_bitsPerSample == 16)
            {
                short* pShort = (short*)pData.ToPointer();
                const float scale = 1.0f / 32768.0f;
                for (int i = 0; i < frames; i++)
                {
                    if (_channels >= 2)
                    {
                        int baseIdx = i * _channels;
                        destination[i * 2] = pShort[baseIdx] * scale;
                        destination[i * 2 + 1] = pShort[baseIdx + 1] * scale;
                    }
                    else
                    {
                        float val = pShort[i] * scale;
                        destination[i * 2] = val;
                        destination[i * 2 + 1] = val;
                    }
                }
            }
        }

        /// <summary>
        /// Stops capturing and safely un-mutes the target application.
        /// </summary>
        public void Stop()
        {
            _isRunning = false;

            try
            {
                _audioClient?.Stop();
            }
            catch (Exception) { }

            if (_captureThread != null && _captureThread.IsAlive)
            {
                _captureThread.Join(500);
            }

            RestoreOriginalSessionMute();

            _captureClient = null;
            _audioClient = null;

            if (_hCaptureEvent != IntPtr.Zero)
            {
                CloseHandle(_hCaptureEvent);
                _hCaptureEvent = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Stop();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }

        ~ProcessLoopbackCapture()
        {
            Dispose();
        }
    }
}
