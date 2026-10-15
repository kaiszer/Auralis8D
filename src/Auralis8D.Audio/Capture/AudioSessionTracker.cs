using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Auralis8D.Audio.Interop.WASAPI;
using Auralis8D.Core.Models;

namespace Auralis8D.Audio.Capture
{
    /// <summary>
    /// Tracks active Windows audio sessions, monitors audio peak activity, and discovers audio-producing processes.
    /// </summary>
    public class AudioSessionTracker
    {
        /// <summary>
        /// Enumerates all processes with active audio sessions on the default render device.
        /// </summary>
        public List<AppProcessItem> GetActiveAudioProcesses()
        {
            var list = new List<AppProcessItem>();
            var seenPids = new HashSet<int>();

            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IMMDevice defaultDevice);
                if (hr != 0 || defaultDevice == null) return list;

                Guid sessionMgrGuid = typeof(IAudioSessionManager2).GUID;
                defaultDevice.Activate(ref sessionMgrGuid, 23, IntPtr.Zero, out object sessionMgrObj);
                var sessionMgr = (IAudioSessionManager2)sessionMgrObj;

                if (sessionMgr.GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum) == 0 && sessionEnum != null)
                {
                    sessionEnum.GetCount(out int count);
                    for (int i = 0; i < count; i++)
                    {
                        if (sessionEnum.GetSession(i, out IAudioSessionControl control) == 0 && control != null)
                        {
                            if (control is IAudioSessionControl2 control2)
                            {
                                if (control2.IsSystemSoundsSession() == 0)
                                {
                                    // Skip system sounds
                                }

                                control2.GetProcessId(out uint pid);
                                int intPid = (int)pid;

                                // Ignore PID 0 (system idle) and duplicate PIDs
                                if (intPid <= 4 || seenPids.Contains(intPid)) continue;
                                seenPids.Add(intPid);

                                string procName = "Unknown";
                                string displayName = "Unknown Application";
                                string exePath = string.Empty;

                                try
                                {
                                    using var proc = Process.GetProcessById(intPid);
                                    procName = proc.ProcessName + ".exe";
                                    displayName = !string.IsNullOrWhiteSpace(proc.MainWindowTitle)
                                        ? proc.MainWindowTitle
                                        : proc.ProcessName;

                                    try
                                    {
                                        exePath = proc.MainModule?.FileName ?? string.Empty;
                                    }
                                    catch (Exception) { }
                                }
                                catch (Exception)
                                {
                                    continue; // Process already terminated
                                }

                                // Query peak level
                                float peak = 0.0f;
                                if (control is IAudioMeterInformation meter)
                                {
                                    meter.GetPeakValue(out peak);
                                }

                                control2.GetState(out uint state); // 1 = AudioSessionStateActive
                                bool isActive = (state == 1) || (peak > 0.001f);

                                list.Add(new AppProcessItem
                                {
                                    ProcessId = intPid,
                                    ProcessName = procName,
                                    DisplayName = displayName,
                                    ExecutablePath = exePath,
                                    IsActiveAudio = isActive,
                                    AudioPeak = peak,
                                    StatusMessage = isActive ? "Playing audio" : "Session active (silent)"
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Core audio enumeration issue
            }

            return list;
        }
    }
}
