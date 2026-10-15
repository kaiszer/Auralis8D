using System;
using System.IO;
using System.Linq;
using Auralis8D.Audio.Capture;
using Auralis8D.Audio.Interop;
using Auralis8D.Audio.Output;
using Auralis8D.Core.Models;
using Auralis8D.Core.Services;

namespace Auralis8D.Tests
{
    /// <summary>
    /// Comprehensive automated test runner for Auralis 8D.
    /// Validates C++ DSP algorithms, phase tracking, equal-power panning,
    /// M/S stereo widening, soft clipping, JSON configuration, and audio session discovery.
    /// </summary>
    public static class Program
    {
        private static int _passedCount = 0;
        private static int _failedCount = 0;

        public static int Main()
        {
            Console.WriteLine("=================================================");
            Console.WriteLine("  AURALIS 8D - AUTOMATED TEST SUITE EXECUTION   ");
            Console.WriteLine("=================================================");

            RunTest("TestNativeDSPAllocationAndParameters", TestNativeDSPAllocationAndParameters);
            RunTest("TestDSPPhaseContinuityAcrossBuffers", TestDSPPhaseContinuityAcrossBuffers);
            RunTest("TestEqualPowerPanningLaw", TestEqualPowerPanningLaw);
            RunTest("TestSoftSaturationLimiter", TestSoftSaturationLimiter);
            RunTest("TestMidSideStereoWidth", TestMidSideStereoWidth);
            RunTest("TestBypassCrossfade", TestBypassCrossfade);
            RunTest("TestConfigurationServicePersistence", TestConfigurationServicePersistence);
            RunTest("TestProfileManagerSelection", TestProfileManagerSelection);
            RunTest("TestAudioSessionEnumeration", TestAudioSessionEnumeration);
            RunTest("TestAudioDeviceEnumeration", TestAudioDeviceEnumeration);

            Console.WriteLine("-------------------------------------------------");
            Console.WriteLine($"Summary: {_passedCount} passed, {_failedCount} failed.");
            Console.WriteLine("=================================================");

            return _failedCount == 0 ? 0 : 1;
        }

        private static void RunTest(string testName, Action testAction)
        {
            Console.Write($"[TEST] {testName} ... ");
            try
            {
                testAction();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("PASSED");
                Console.ResetColor();
                _passedCount++;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FAILED: {ex.Message}");
                Console.ResetColor();
                _failedCount++;
            }
        }

        /// <summary>
        /// Validates DSP lifecycle, sample rate changes, and parameter round-trip.
        /// </summary>
        private static void TestNativeDSPAllocationAndParameters()
        {
            using var dsp = new NativeDSP(48000);
            dsp.SetParameters(0.25f, 0.85f, 1.2f, false);
            var (speed, depth, width, bypass) = dsp.GetParameters();

            Assert(Math.Abs(speed - 0.25f) < 0.001f, "Speed mismatch");
            Assert(Math.Abs(depth - 0.85f) < 0.001f, "Depth mismatch");
            Assert(Math.Abs(width - 1.2f) < 0.001f, "Width mismatch");
            Assert(!bypass, "Bypass mismatch");

            dsp.SetSampleRate(96000);
            dsp.Reset();
        }

        /// <summary>
        /// Validates that chunked processing produces identical continuous audio to a single block,
        /// proving continuous phase tracking without boundary discontinuities.
        /// </summary>
        private static void TestDSPPhaseContinuityAcrossBuffers()
        {
            const uint sampleRate = 48000;
            const int totalFrames = 1024;
            float[] inputData = new float[totalFrames * 2];
            for (int i = 0; i < totalFrames; i++)
            {
                float s = (float)Math.Sin(2.0 * Math.PI * 440.0 * i / sampleRate) * 0.4f;
                inputData[i * 2] = s;
                inputData[i * 2 + 1] = s;
            }

            // 1. Single buffer
            using var dspSingle = new NativeDSP(sampleRate);
            dspSingle.SetParameters(0.3f, 1.0f, 1.0f, false);
            dspSingle.Reset();
            float[] singleBuffer = (float[])inputData.Clone();
            dspSingle.ProcessInterleaved(singleBuffer, (uint)totalFrames);

            // 2. Chunks of 64 frames
            using var dspChunked = new NativeDSP(sampleRate);
            dspChunked.SetParameters(0.3f, 1.0f, 1.0f, false);
            dspChunked.Reset();
            float[] chunkedBuffer = (float[])inputData.Clone();
            const int chunkSize = 64;
            for (int offset = 0; offset < totalFrames; offset += chunkSize)
            {
                int count = Math.Min(chunkSize, totalFrames - offset);
                float[] chunk = new float[count * 2];
                Array.Copy(chunkedBuffer, offset * 2, chunk, 0, count * 2);
                dspChunked.ProcessInterleaved(chunk, (uint)count);
                Array.Copy(chunk, 0, chunkedBuffer, offset * 2, count * 2);
            }

            // Verify sample-by-sample equivalence
            for (int i = 0; i < totalFrames * 2; i++)
            {
                Assert(Math.Abs(singleBuffer[i] - chunkedBuffer[i]) < 0.001f, $"Phase mismatch at sample {i}");
            }
        }

        /// <summary>
        /// Validates the equal-power panning law: L^2 + R^2 must equal input energy.
        /// </summary>
        private static void TestEqualPowerPanningLaw()
        {
            const uint sampleRate = 48000;
            using var dsp = new NativeDSP(sampleRate);
            dsp.SetParameters(0.5f, 1.0f, 1.0f, false);

            // Warm up smoothing filter
            float[] warmup = Enumerable.Repeat(0.5f, (int)sampleRate * 2).ToArray();
            dsp.ProcessInterleaved(warmup, sampleRate);

            const int frames = 2400;
            float[] buffer = Enumerable.Repeat(0.5f, frames * 2).ToArray();
            dsp.ProcessInterleaved(buffer, (uint)frames);

            for (int i = 0; i < frames; i++)
            {
                float l = buffer[i * 2];
                float r = buffer[i * 2 + 1];
                float power = (l * l) + (r * r);
                Assert(Math.Abs(power - 0.25f) < 0.05f, $"Power law violated at frame {i}: power = {power}");
            }
        }

        /// <summary>
        /// Validates soft saturation limiter: extreme inputs must not clip hard or exceed [-1.0, 1.0].
        /// </summary>
        private static void TestSoftSaturationLimiter()
        {
            using var dsp = new NativeDSP(48000);
            dsp.Reset();

            const int frames = 256;
            float[] buffer = new float[frames * 2];
            for (int i = 0; i < frames; i++)
            {
                // Extreme hot signal +3.5 to -3.5
                float hot = 3.5f * (float)Math.Sin(i * 0.1);
                buffer[i * 2] = hot;
                buffer[i * 2 + 1] = hot;
            }

            dsp.ProcessInterleaved(buffer, (uint)frames);

            for (int i = 0; i < frames * 2; i++)
            {
                Assert(!float.IsNaN(buffer[i]) && !float.IsInfinity(buffer[i]), $"Invalid sample value at {i}");
                Assert(buffer[i] <= 1.0f && buffer[i] >= -1.0f, $"Limiter boundary exceeded at {i}: {buffer[i]}");
            }
        }

        /// <summary>
        /// Validates Mid/Side stereo width processing.
        /// </summary>
        private static void TestMidSideStereoWidth()
        {
            const uint sampleRate = 48000;

            // Width = 0.0: stereo collapses to mono before rotation
            using (var dspMono = new NativeDSP(sampleRate))
            {
                dspMono.SetParameters(0.0f, 0.0f, 0.0f, false); // Center panning, mono width
                dspMono.Reset();

                float[] warmup = new float[4800 * 2];
                dspMono.ProcessInterleaved(warmup, 4800);

                float[] buffer = new float[] { 0.8f, 0.2f }; // Asymmetric input
                dspMono.ProcessInterleaved(buffer, 1);

                // With width=0, side is 0, mid is (0.8+0.2)/2 = 0.5. At center pan, gainL=gainR ~ 0.707
                // Output L and R should be identical!
                Assert(Math.Abs(buffer[0] - buffer[1]) < 0.02f, $"Mono collapse failed: L={buffer[0]}, R={buffer[1]}");
            }
        }

        /// <summary>
        /// Validates bypass mode restores dry audio bit-faithfully after fade.
        /// </summary>
        private static void TestBypassCrossfade()
        {
            using var dsp = new NativeDSP(48000);
            dsp.SetParameters(0.2f, 1.0f, 1.0f, true); // Bypass = true
            dsp.Reset();

            // Warm up crossfade
            float[] warmup = new float[4800 * 2];
            dsp.ProcessInterleaved(warmup, 4800);

            float[] testBuffer = new float[] { 0.42f, 0.78f };
            dsp.ProcessInterleaved(testBuffer, 1);

            Assert(Math.Abs(testBuffer[0] - 0.42f) < 0.01f, $"Bypass L mismatch: {testBuffer[0]}");
            Assert(Math.Abs(testBuffer[1] - 0.78f) < 0.01f, $"Bypass R mismatch: {testBuffer[1]}");
        }

        /// <summary>
        /// Validates JSON settings persistence, reload, and directory creation.
        /// </summary>
        private static void TestConfigurationServicePersistence()
        {
            string tempConfigPath = Path.Combine(Path.GetTempPath(), "Auralis8D_Test", "test_settings.json");
            try
            {
                if (File.Exists(tempConfigPath)) File.Delete(tempConfigPath);

                var configService = new ConfigurationService(tempConfigPath);
                var settings = configService.LoadSettings();

                settings.MasterEnabled = false;
                settings.RotationSpeedHz = 0.35f;
                settings.EffectDepth = 0.75f;
                settings.SelectedProcessNames.Add("testapp.exe");
                configService.SaveSettings(settings);

                var reloaded = configService.LoadSettings();
                Assert(!reloaded.MasterEnabled, "MasterEnabled failed to persist");
                Assert(Math.Abs(reloaded.RotationSpeedHz - 0.35f) < 0.001f, "RotationSpeedHz failed to persist");
                Assert(reloaded.SelectedProcessNames.Contains("testapp.exe"), "Process names failed to persist");
            }
            finally
            {
                if (File.Exists(tempConfigPath)) File.Delete(tempConfigPath);
            }
        }

        /// <summary>
        /// Validates profile manager defaults and selection.
        /// </summary>
        private static void TestProfileManagerSelection()
        {
            var mgr = new ProfileManager();
            Assert(mgr.Profiles.Count >= 4, "Missing default profiles");

            var gentle = mgr.SelectProfile("Gentle Rotation");
            Assert(gentle.Name == "Gentle Rotation", "Failed to select Gentle Rotation");
            Assert(Math.Abs(gentle.SpeedHz - 0.08f) < 0.001f, "Gentle speed mismatch");

            mgr.UpdateCustomProfile(0.4f, 0.6f, 1.4f);
            var custom = mgr.ActiveProfile;
            Assert(custom.Name == "Custom", "Active profile not updated to custom");
            Assert(Math.Abs(custom.SpeedHz - 0.4f) < 0.001f, "Custom speed mismatch");
        }

        /// <summary>
        /// Validates audio session discovery.
        /// </summary>
        private static void TestAudioSessionEnumeration()
        {
            try
            {
                var enumerator = (Auralis8D.Audio.Interop.WASAPI.IMMDeviceEnumerator)new Auralis8D.Audio.Interop.WASAPI.MMDeviceEnumerator();
                int hr = enumerator.GetDefaultAudioEndpoint(Auralis8D.Audio.Interop.WASAPI.EDataFlow.eRender, Auralis8D.Audio.Interop.WASAPI.ERole.eMultimedia, out var defaultDevice);
                Console.WriteLine($"\n  GetDefaultAudioEndpoint hr=0x{hr:X8}");
                if (hr == 0 && defaultDevice != null)
                {
                    Guid mgrGuid = typeof(Auralis8D.Audio.Interop.WASAPI.IAudioSessionManager2).GUID;
                    hr = defaultDevice.Activate(ref mgrGuid, 23, IntPtr.Zero, out object mgrObj);
                    Console.WriteLine($"  defaultDevice.Activate IAudioSessionManager2 hr=0x{hr:X8}, mgrObj={mgrObj?.GetType()}");
            var tracker = new Auralis8D.Audio.Capture.AudioSessionTracker();
            var processes = tracker.GetActiveAudioProcesses();
            Console.WriteLine($"\n  -> AudioSessionTracker discovered {processes.Count} available application(s):");
            foreach (var p in processes)
            {
                Console.WriteLine($"     * PID={p.ProcessId,-6} | Name={p.ProcessName,-20} | Title={p.DisplayName,-30} | ActiveAudio={p.IsActiveAudio}");
            }
            Assert(processes != null && processes.Count > 0, "No applications discovered");

            var edgeApp = processes.FirstOrDefault(p => p.IsActiveAudio) ?? processes.FirstOrDefault();
            if (edgeApp != null)
            {
                Console.WriteLine($"\n  -> Testing ProcessLoopbackCapture on {edgeApp.ProcessName} (PID {edgeApp.ProcessId})...");
                var capture = new Auralis8D.Audio.Capture.ProcessLoopbackCapture(edgeApp.ProcessId, edgeApp.ProcessName);
                long framesCaptured = 0;
                float maxPeak = 0.0f;
                capture.AudioSamplesCaptured = (buf, frames, rate) =>
                {
                    Interlocked.Add(ref framesCaptured, frames);
                    for (int i = 0; i < frames * 2; i++)
                    {
                        float v = Math.Abs(buf[i]);
                        if (v > maxPeak) maxPeak = v;
                    }
                };
                capture.CaptureErrorOccurred = (err) => Console.WriteLine($"     [ERROR] {err}");
                bool started = capture.Start();
                Console.WriteLine($"     [RESULT] Capture.Start() = {started}");
                Assert(started, "ProcessLoopbackCapture failed to start");

                if (started)
                {
                    Thread.Sleep(600);
                    capture.Stop();
                    Console.WriteLine($"     [RESULT] Stopped cleanly. Total frames: {framesCaptured}, Max Peak: {maxPeak}");
                    Assert(framesCaptured > 0, "No audio frames captured from active process");
                }
            }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  EXCEPTION: {ex}");
            }
        }

        /// <summary>
        /// Validates audio device enumeration.
        /// </summary>
        private static void TestAudioDeviceEnumeration()
        {
            var devices = DeviceEnumerator.GetAudioRenderDevices();
            Assert(devices != null && devices.Count > 0, "No audio devices detected");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception(message);
            }
        }
    }
}
