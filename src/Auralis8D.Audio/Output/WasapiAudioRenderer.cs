using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Auralis8D.Audio.Interop.WASAPI;

namespace Auralis8D.Audio.Output
{
    /// <summary>
    /// Real-time WASAPI audio renderer. Streams processed 8D audio frames to the chosen audio output endpoint.
    /// Employs a lock-free circular ring buffer to decouple audio capture/DSP from playback timing.
    /// </summary>
    public sealed class WasapiAudioRenderer : IDisposable
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private readonly string _deviceId;
        private IAudioClient? _audioClient;
        private IAudioRenderClient? _renderClient;
        private IntPtr _hRenderEvent = IntPtr.Zero;
        private Thread? _renderThread;
        private volatile bool _isRunning;
        private bool _disposed;

        private uint _bufferSize;
        private uint _sampleRate = 48000;
        private ushort _channels = 2;

        // Circular ring buffer for lock-free audio streaming (1 second capacity at 48kHz stereo = ~96000 floats)
        private readonly float[] _ringBuffer = new float[96000 * 2];
        private int _writePos;
        private int _readPos;
        private readonly object _bufferLock = new object();

        public Action<string>? PlaybackErrorOccurred;
        public uint SampleRate => _sampleRate;
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Creates a new WASAPI renderer.
        /// </summary>
        /// <param name="deviceId">Optional MMDevice endpoint ID. Empty string uses default render endpoint.</param>
        public WasapiAudioRenderer(string deviceId = "")
        {
            _deviceId = deviceId;
        }

        /// <summary>
        /// Pushes processed float stereo audio samples into the ring buffer for playback.
        /// </summary>
        /// <param name="samples">Interleaved stereo float samples.</param>
        /// <param name="frameCount">Number of frames.</param>
        public void WriteSamples(float[] samples, uint frameCount)
        {
            if (!_isRunning || samples == null || frameCount == 0) return;

            int sampleCount = (int)(frameCount * 2);
            lock (_bufferLock)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    _ringBuffer[_writePos] = samples[i];
                    _writePos = (_writePos + 1) % _ringBuffer.Length;
                }
            }
        }

        /// <summary>
        /// Flushes all pending audio samples from the circular ring buffer.
        /// Ensures instantaneous silence upon switching to bypass mode, preventing lingering audio duplicates.
        /// </summary>
        public void ClearBuffer()
        {
            lock (_bufferLock)
            {
                Array.Clear(_ringBuffer, 0, _ringBuffer.Length);
                _writePos = 0;
                _readPos = 0;
            }
        }

        /// <summary>
        /// Initializes WASAPI render client on the designated audio device and starts streaming.
        /// </summary>
        public bool Start()
        {
            if (_isRunning) return true;

            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                IMMDevice device;

                if (string.IsNullOrEmpty(_deviceId))
                {
                    int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device);
                    if (hr != 0 || device == null)
                    {
                        PlaybackErrorOccurred?.Invoke("No default audio output device found.");
                        return false;
                    }
                }
                else
                {
                    int hr = enumerator.GetDevice(_deviceId, out device);
                    if (hr != 0 || device == null)
                    {
                        // Fallback to default endpoint if specified device disconnected
                        hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device);
                        if (hr != 0 || device == null)
                        {
                            PlaybackErrorOccurred?.Invoke("Failed to activate fallback audio output device.");
                            return false;
                        }
                    }
                }

                Guid clientGuid = typeof(IAudioClient).GUID;
                int actHr = device.Activate(ref clientGuid, 23, IntPtr.Zero, out object clientObj);
                if (actHr != 0 || clientObj == null)
                {
                    PlaybackErrorOccurred?.Invoke($"Failed to activate IAudioClient for output: 0x{actHr:X8}");
                    return false;
                }

                _audioClient = (IAudioClient)clientObj;

                // Query format
                int fmtHr = _audioClient.GetMixFormat(out IntPtr pFormat);
                if (fmtHr != 0 || pFormat == IntPtr.Zero)
                {
                    PlaybackErrorOccurred?.Invoke("Failed to retrieve output device mix format.");
                    return false;
                }

                var waveFormat = Marshal.PtrToStructure<WAVEFORMATEX>(pFormat);
                _sampleRate = waveFormat.nSamplesPerSec;
                _channels = waveFormat.nChannels;

                _hRenderEvent = CreateEvent(IntPtr.Zero, false, false, null);
                long hnsBufferDuration = 200000; // 20ms buffer
                Guid sessionGuid = Guid.Empty;

                int initHr = _audioClient.Initialize(
                    AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED,
                    AUDCLNT_STREAMFLAGS.AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
                    hnsBufferDuration,
                    0,
                    pFormat,
                    ref sessionGuid
                );
                Marshal.FreeCoTaskMem(pFormat);

                if (initHr != 0)
                {
                    PlaybackErrorOccurred?.Invoke($"Failed to initialize output client: 0x{initHr:X8}");
                    return false;
                }

                _audioClient.GetBufferSize(out _bufferSize);
                _audioClient.SetEventHandle(_hRenderEvent);

                Guid renderGuid = typeof(IAudioRenderClient).GUID;
                _audioClient.GetService(ref renderGuid, out object renderObj);
                _renderClient = (IAudioRenderClient)renderObj;

                // Pre-fill initial buffer with silence
                if (_renderClient.GetBuffer(_bufferSize, out IntPtr pInitialBuffer) == 0)
                {
                    _renderClient.ReleaseBuffer(_bufferSize, AUDCLNT_BUFFERFLAGS.AUDCLNT_BUFFERFLAGS_SILENT);
                }

                _audioClient.Start();

                _isRunning = true;
                _renderThread = new Thread(RenderLoop)
                {
                    Name = "Auralis8D_AudioRenderThread",
                    Priority = ThreadPriority.Highest,
                    IsBackground = true
                };
                _renderThread.Start();

                return true;
            }
            catch (Exception ex)
            {
                PlaybackErrorOccurred?.Invoke($"Exception opening audio output: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// High-priority loop feeding frames from circular buffer to WASAPI output buffer.
        /// </summary>
        private unsafe void RenderLoop()
        {
            while (_isRunning && _renderClient != null && _audioClient != null)
            {
                try
                {
                    using (var waitHandle = new AutoResetEvent(false) { SafeWaitHandle = new Microsoft.Win32.SafeHandles.SafeWaitHandle(_hRenderEvent, false) })
                    {
                        if (!waitHandle.WaitOne(100))
                        {
                            continue;
                        }
                    }

                    if (!_isRunning || _audioClient == null || _renderClient == null) break;

                    if (_audioClient.GetCurrentPadding(out uint paddingFrames) != 0) continue;

                    uint framesNeeded = _bufferSize - paddingFrames;
                    if (framesNeeded == 0) continue;

                    int hr = _renderClient.GetBuffer(framesNeeded, out IntPtr pData);
                    if (hr != 0 || pData == IntPtr.Zero) continue;

                    float* pOutFloat = (float*)pData.ToPointer();
                    int samplesAvailable = GetAvailableSampleCount();

                    if (samplesAvailable >= framesNeeded * 2)
                    {
                        lock (_bufferLock)
                        {
                            for (int i = 0; i < framesNeeded; i++)
                            {
                                float l = _ringBuffer[_readPos];
                                _readPos = (_readPos + 1) % _ringBuffer.Length;

                                float r = _ringBuffer[_readPos];
                                _readPos = (_readPos + 1) % _ringBuffer.Length;

                                if (_channels == 2)
                                {
                                    pOutFloat[i * 2] = l;
                                    pOutFloat[i * 2 + 1] = r;
                                }
                                else
                                {
                                    for (int ch = 0; ch < _channels; ch++)
                                    {
                                        pOutFloat[i * _channels + ch] = (ch == 1) ? r : l;
                                    }
                                }
                            }
                        }
                        _renderClient.ReleaseBuffer(framesNeeded, AUDCLNT_BUFFERFLAGS.AUDCLNT_BUFFERFLAGS_NONE);
                    }
                    else
                    {
                        // Starvation: fill with silence to prevent buzz or DC clicks
                        _renderClient.ReleaseBuffer(framesNeeded, AUDCLNT_BUFFERFLAGS.AUDCLNT_BUFFERFLAGS_SILENT);
                    }
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                    {
                        PlaybackErrorOccurred?.Invoke($"Render loop exception: {ex.Message}");
                    }
                    break;
                }
            }
        }

        private int GetAvailableSampleCount()
        {
            lock (_bufferLock)
            {
                if (_writePos >= _readPos)
                {
                    return _writePos - _readPos;
                }
                return (_ringBuffer.Length - _readPos) + _writePos;
            }
        }

        /// <summary>
        /// Stops playback and releases resources.
        /// </summary>
        public void Stop()
        {
            _isRunning = false;

            try
            {
                _audioClient?.Stop();
            }
            catch (Exception) { }

            if (_renderThread != null && _renderThread.IsAlive)
            {
                _renderThread.Join(500);
            }

            _renderClient = null;
            _audioClient = null;

            if (_hRenderEvent != IntPtr.Zero)
            {
                CloseHandle(_hRenderEvent);
                _hRenderEvent = IntPtr.Zero;
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

        ~WasapiAudioRenderer()
        {
            Dispose();
        }
    }
}
