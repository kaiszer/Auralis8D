using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Auralis8D.Audio.Interop
{
    /// <summary>
    /// P/Invoke bindings and safe wrapper for the native C++ Auralis8D.DSP audio processing library.
    /// Handles DLL resolution, instance lifecycle, and sample-accurate buffer processing.
    /// </summary>
    public sealed class NativeDSP : IDisposable
    {
        private const string DllName = "Auralis8D.DSP.dll";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpLibFileName);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr Auralis8D_CreateDSP(uint sampleRate);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Auralis8D_DestroyDSP(IntPtr handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Auralis8D_SetSampleRate(IntPtr handle, uint sampleRate);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Auralis8D_SetParameters(IntPtr handle, float panSpeedHz, float panDepth, float stereoWidth, int bypass);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Auralis8D_GetParameters(IntPtr handle, out float outPanSpeedHz, out float outPanDepth, out float outStereoWidth, out int outBypass);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Auralis8D_ProcessInterleaved(IntPtr handle, [In, Out] float[] buffer, uint frameCount);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Auralis8D_Reset(IntPtr handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern float Auralis8D_GetCurrentPanPosition(IntPtr handle);

        private IntPtr _handle;
        private bool _disposed;
        private static bool _dllLoaded;

        static NativeDSP()
        {
            EnsureDllLoaded();
        }

        /// <summary>
        /// Pre-loads Auralis8D.DSP.dll from expected installation or build directories.
        /// </summary>
        private static void EnsureDllLoaded()
        {
            if (_dllLoaded) return;

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] probePaths = new[]
            {
                Path.Combine(baseDir, DllName),
                Path.Combine(baseDir, "..", "..", "..", "..", "src", "Auralis8D.DSP", DllName),
                Path.Combine(baseDir, "runtimes", "win-x64", "native", DllName),
                Path.Combine(Directory.GetCurrentDirectory(), "src", "Auralis8D.DSP", DllName)
            };

            foreach (var path in probePaths)
            {
                string fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                {
                    IntPtr hMod = LoadLibrary(fullPath);
                    if (hMod != IntPtr.Zero)
                    {
                        _dllLoaded = true;
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Creates a new instance of the native 8D DSP audio processor.
        /// </summary>
        /// <param name="sampleRate">Initial sample rate in Hz (e.g. 48000).</param>
        public NativeDSP(uint sampleRate = 48000)
        {
            _handle = Auralis8D_CreateDSP(sampleRate);
            if (_handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to allocate native Auralis8D DSP instance.");
            }
        }

        /// <summary>
        /// Updates the sample rate on the processor.
        /// </summary>
        public void SetSampleRate(uint sampleRate)
        {
            ThrowIfDisposed();
            Auralis8D_SetSampleRate(_handle, sampleRate);
        }

        /// <summary>
        /// Configures real-time DSP parameters.
        /// </summary>
        /// <param name="panSpeedHz">Rotation speed in Hz.</param>
        /// <param name="panDepth">Panning depth [0.0, 1.0].</param>
        /// <param name="stereoWidth">Stereo width multiplier [0.0, 2.0].</param>
        /// <param name="bypass">Whether to bypass the effect.</param>
        public void SetParameters(float panSpeedHz, float panDepth, float stereoWidth, bool bypass)
        {
            ThrowIfDisposed();
            Auralis8D_SetParameters(_handle, panSpeedHz, panDepth, stereoWidth, bypass ? 1 : 0);
        }

        /// <summary>
        /// Retrieves the current DSP configuration parameters.
        /// </summary>
        public (float SpeedHz, float Depth, float StereoWidth, bool Bypass) GetParameters()
        {
            ThrowIfDisposed();
            Auralis8D_GetParameters(_handle, out float speed, out float depth, out float width, out int bypass);
            return (speed, depth, width, bypass != 0);
        }

        /// <summary>
        /// Processes an interleaved stereo 32-bit float audio buffer in-place.
        /// </summary>
        /// <param name="buffer">Interleaved stereo float buffer (length >= frameCount * 2).</param>
        /// <param name="frameCount">Number of frames to process.</param>
        public void ProcessInterleaved(float[] buffer, uint frameCount)
        {
            ThrowIfDisposed();
            if (buffer == null || frameCount == 0) return;
            Auralis8D_ProcessInterleaved(_handle, buffer, frameCount);
        }

        /// <summary>
        /// Resets the internal phase and interpolation state.
        /// </summary>
        public void Reset()
        {
            ThrowIfDisposed();
            Auralis8D_Reset(_handle);
        }

        /// <summary>
        /// Retrieves the instantaneous normalized pan position [-1.0, 1.0] for UI visualizer.
        /// </summary>
        public float GetCurrentPanPosition()
        {
            ThrowIfDisposed();
            return Auralis8D_GetCurrentPanPosition(_handle);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed || _handle == IntPtr.Zero)
            {
                throw new ObjectDisposedException(nameof(NativeDSP));
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_handle != IntPtr.Zero)
                {
                    Auralis8D_DestroyDSP(_handle);
                    _handle = IntPtr.Zero;
                }
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }

        ~NativeDSP()
        {
            Dispose();
        }
    }
}
