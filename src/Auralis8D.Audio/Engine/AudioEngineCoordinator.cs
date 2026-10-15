using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Auralis8D.Audio.Capture;
using Auralis8D.Audio.Interop;
using Auralis8D.Audio.Output;
using Auralis8D.Core.Models;

namespace Auralis8D.Audio.Engine
{
    /// <summary>
    /// Master coordinator for the Auralis 8D audio engine.
    /// Manages application capture pipelines, real-time C++ DSP processors, and output rendering.
    /// Thread-safe and resilient to process termination and audio device changes.
    /// </summary>
    public sealed class AudioEngineCoordinator : IDisposable
    {
        private class ProcessPipeline : IDisposable
        {
            public int ProcessId { get; set; }
            public string ProcessName { get; set; } = string.Empty;
            public ProcessLoopbackCapture? Capture { get; set; }
            public NativeDSP? DSP { get; set; }
            public bool IsActive { get; set; }
            public float LastPanPosition { get; set; }

            public void Dispose()
            {
                Capture?.Dispose();
                DSP?.Dispose();
            }
        }

        private readonly ConcurrentDictionary<int, ProcessPipeline> _pipelines = new();
        private WasapiAudioRenderer? _renderer;
        private readonly AudioSessionTracker _sessionTracker = new();
        private readonly object _stateLock = new();

        private bool _masterEnabled = true;
        private float _rotationSpeedHz = 0.15f;
        private float _effectDepth = 0.90f;
        private float _stereoWidth = 1.15f;
        private string _outputDeviceId = string.Empty;
        private bool _disposed;

        // Peak volume and pan position for UI animation
        private float _currentPanPosition = 0.0f;
        private float _currentPeak = 0.0f;
        private DateTime _lastAudioTime = DateTime.MinValue;
        private string? _lastErrorMessage;
        private DateTime _lastErrorTime = DateTime.MinValue;

        /// <summary>
        /// Event notifying UI of engine status updates or errors.
        /// </summary>
        public Action<string>? EngineStatusChanged;

        public bool MasterEnabled => _masterEnabled;
        public float CurrentPanPosition => _currentPanPosition;
        public float CurrentPeak => _currentPeak;
        public bool IsAudioFlowing => (DateTime.UtcNow - _lastAudioTime).TotalMilliseconds < 1200;
        public int ActiveProcessingCount => _pipelines.Values.Count(p => p.IsActive);

        public AudioEngineCoordinator()
        {
        }

        /// <summary>
        /// Starts or reconfigures the audio output renderer.
        /// </summary>
        public bool InitializeRenderer(string outputDeviceId = "")
        {
            lock (_stateLock)
            {
                _outputDeviceId = outputDeviceId;

                _renderer?.Dispose();
                _renderer = new WasapiAudioRenderer(_outputDeviceId);
                _renderer.PlaybackErrorOccurred += (err) =>
                {
                    EngineStatusChanged?.Invoke($"Output Error: {err}");
                };

                bool started = _renderer.Start();
                if (started)
                {
                    EngineStatusChanged?.Invoke("Audio engine ready.");
                }
                else
                {
                    EngineStatusChanged?.Invoke("Output device unavailable.");
                }
                return started;
            }
        }

        /// <summary>
        /// Updates the real-time DSP parameters across all active pipelines.
        /// Changes take effect instantly and smoothly without clicks or audio interruption.
        /// </summary>
        public void UpdateParameters(bool masterEnabled, float speedHz, float depth, float width)
        {
            _masterEnabled = masterEnabled;
            _rotationSpeedHz = speedHz;
            _effectDepth = depth;
            _stereoWidth = width;

            foreach (var kvp in _pipelines)
            {
                var pipeline = kvp.Value;
                try
                {
                    pipeline.DSP?.SetParameters(_rotationSpeedHz, _effectDepth, _stereoWidth, !_masterEnabled);
                }
                catch (Exception) { }
            }
        }

        /// <summary>
        /// Synchronizes selected applications with active capture pipelines.
        /// Starts capture for newly selected applications, stops capture for unselected ones.
        /// </summary>
        public void SyncSelectedProcesses(IEnumerable<AppProcessItem> selectedApps)
        {
            lock (_stateLock)
            {
                EnsureRendererStarted();

                var selectedList = selectedApps.Where(a => a.IsSelected).ToList();
                var selectedPids = new HashSet<int>(selectedList.Select(a => a.ProcessId));

                // Remove unselected pipelines
                var currentPids = _pipelines.Keys.ToList();
                foreach (int pid in currentPids)
                {
                    if (!selectedPids.Contains(pid))
                    {
                        if (_pipelines.TryRemove(pid, out var removedPipeline))
                        {
                            removedPipeline.Dispose();
                        }
                    }
                }

                // Add or start new pipelines, and automatically recover any stopped capture pipelines
                foreach (var app in selectedList)
                {
                    int rootPid = ProcessTreeHelper.GetRootProcessId(app.ProcessId);
                    if (_pipelines.TryGetValue(rootPid, out var existingPipeline))
                    {
                        // If the process loopback capture thread exited or faulted (e.g. track change), restart it seamlessly
                        if (existingPipeline.Capture == null || !existingPipeline.Capture.IsRunning)
                        {
                            _pipelines.TryRemove(rootPid, out _);
                            existingPipeline.Dispose();
                            StartCaptureForProcess(rootPid, app.ProcessName);
                        }
                    }
                    else
                    {
                        StartCaptureForProcess(rootPid, app.ProcessName);
                    }
                }

                UpdateStatusMessage();
            }
        }

        /// <summary>
        /// Initiates application loopback capture and DSP pipeline for a target process.
        /// Resolves child worker PIDs to the root application process so capture includes all sub-processes.
        /// </summary>
        private void StartCaptureForProcess(int pid, string processName)
        {
            try
            {
                int rootPid = ProcessTreeHelper.GetRootProcessId(pid);
                if (_pipelines.ContainsKey(rootPid)) return;

                var dsp = new NativeDSP(48000);
                dsp.SetParameters(_rotationSpeedHz, _effectDepth, _stereoWidth, !_masterEnabled);

                var capture = new ProcessLoopbackCapture(rootPid, processName);
                var pipeline = new ProcessPipeline
                {
                    ProcessId = rootPid,
                    ProcessName = processName,
                    Capture = capture,
                    DSP = dsp,
                    IsActive = false
                };

                capture.AudioSamplesCaptured = (buffer, frames, sampleRate) =>
                {
                    OnSamplesCaptured(pipeline, buffer, frames, sampleRate);
                };

                capture.CaptureErrorOccurred = (errMsg) =>
                {
                    _lastErrorMessage = $"Capture error on {processName}: {errMsg}";
                    _lastErrorTime = DateTime.UtcNow;
                    EngineStatusChanged?.Invoke(_lastErrorMessage);
                    _pipelines.TryRemove(rootPid, out var failedPipeline);
                    failedPipeline?.Dispose();
                    UpdateStatusMessage();
                };

                if (capture.Start())
                {
                    pipeline.IsActive = true;
                    _pipelines[rootPid] = pipeline;
                    _lastErrorMessage = null;
                }
                else
                {
                    pipeline.Dispose();
                }
            }
            catch (Exception ex)
            {
                _lastErrorMessage = $"Failed to attach to {processName}: {ex.Message}";
                _lastErrorTime = DateTime.UtcNow;
                EngineStatusChanged?.Invoke(_lastErrorMessage);
            }
        }

        /// <summary>
        /// Audio processing callback executed upon receiving captured audio frames.
        /// Tracks instantaneous audio amplitude and 8D stereo rotation angle.
        /// </summary>
        private void OnSamplesCaptured(ProcessPipeline pipeline, float[] buffer, uint frameCount, uint sampleRate)
        {
            if (buffer == null || frameCount == 0 || pipeline.DSP == null) return;

            // 1. Process audio through C++ 8D DSP engine
            pipeline.DSP.ProcessInterleaved(buffer, frameCount);

            // 2. Measure instantaneous peak for visualizer and activity check
            float peak = 0.0f;
            uint totalSamples = frameCount * 2;
            for (uint i = 0; i < totalSamples; i += 4) // sub-sample for efficiency
            {
                float absVal = Math.Abs(buffer[i]);
                if (absVal > peak) peak = absVal;
            }

            // Always update pan position from DSP phase accumulator for smooth continuous animation
            _currentPanPosition = pipeline.DSP.GetCurrentPanPosition();

            if (peak > 0.0005f)
            {
                _lastAudioTime = DateTime.UtcNow;
                _currentPeak = peak;
            }
            else
            {
                // Smooth exponential release decay for UI visualizer
                _currentPeak *= 0.95f;
            }

            // 3. Write processed frames to WASAPI audio renderer
            _renderer?.WriteSamples(buffer, frameCount);
        }

        private void EnsureRendererStarted()
        {
            if (_renderer == null || !_renderer.IsRunning)
            {
                InitializeRenderer(_outputDeviceId);
            }
        }

        private void UpdateStatusMessage()
        {
            if ((DateTime.UtcNow - _lastErrorTime).TotalSeconds < 5.0 && !string.IsNullOrEmpty(_lastErrorMessage))
            {
                EngineStatusChanged?.Invoke(_lastErrorMessage);
                return;
            }

            if (_pipelines.IsEmpty)
            {
                EngineStatusChanged?.Invoke("Audio engine ready.");
            }
            else if (IsAudioFlowing)
            {
                EngineStatusChanged?.Invoke(_masterEnabled ? "8D processing active." : "8D effect bypassed.");
            }
            else
            {
                EngineStatusChanged?.Invoke("Connected (waiting for audio).");
            }
        }

        /// <summary>
        /// Scans active audio processes on the system.
        /// </summary>
        public List<AppProcessItem> ScanProcesses()
        {
            var apps = _sessionTracker.GetActiveAudioProcesses();
            foreach (var app in apps)
            {
                if (_pipelines.ContainsKey(app.ProcessId))
                {
                    app.IsSelected = true;
                    app.IsProcessing = true;
                    app.StatusMessage = "8D Enhanced";
                }
            }
            return apps;
        }

        /// <summary>
        /// Completely stops all capture and playback, releasing sessions and unmuting applications.
        /// </summary>
        public void StopAll()
        {
            lock (_stateLock)
            {
                foreach (var pipeline in _pipelines.Values)
                {
                    pipeline.Dispose();
                }
                _pipelines.Clear();

                _renderer?.Stop();
                _renderer?.Dispose();
                _renderer = null;

                EngineStatusChanged?.Invoke("Audio engine stopped.");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                StopAll();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }

        ~AudioEngineCoordinator()
        {
            Dispose();
        }
    }
}
