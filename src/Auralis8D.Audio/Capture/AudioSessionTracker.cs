using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Auralis8D.Audio.Interop.WASAPI;
using Auralis8D.Core.Models;

namespace Auralis8D.Audio.Capture
{
    /// <summary>
    /// Discovers and tracks Windows audio-producing processes and desktop applications.
    /// Combines live Core Audio WASAPI sessions with user desktop processes so users can select
    /// applications even before they start playing sound.
    /// </summary>
    public class AudioSessionTracker
    {
        // System and shell processes that should never be targeted for 8D audio enhancement
        private static readonly HashSet<string> SystemProcessBlacklist = new(StringComparer.OrdinalIgnoreCase)
        {
            "system", "system idle process", "registry", "smss", "csrss", "wininit",
            "services", "lsass", "svchost", "fontdrvhost", "dwm", "sihost",
            "taskhostw", "ctfmon", "audiodg", "explorer", "shellexperiencehost",
            "startmenuexperiencehost", "searchhost", "textinputhost", "securityhealthsystray",
            "conhost", "openconsole", "cmd", "powershell", "pwsh", "dotnet",
            "auralis8d"
        };

        /// <summary>
        /// Enumerates all available applications: processes actively playing audio via WASAPI,
        /// plus user desktop applications ready to produce sound.
        /// </summary>
        public List<AppProcessItem> GetActiveAudioProcesses()
        {
            var results = new List<AppProcessItem>();
            var seenPids = new HashSet<int>();
            int currentPid = Process.GetCurrentProcess().Id;
            int currentSessionId = -1;

            try
            {
                currentSessionId = Process.GetCurrentProcess().SessionId;
            }
            catch { }

            // 1. Discover processes with active WASAPI audio sessions on default render device
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IMMDevice defaultDevice);
                if (hr == 0 && defaultDevice != null)
                {
                    Guid sessionMgrGuid = typeof(IAudioSessionManager2).GUID;
                    if (defaultDevice.Activate(ref sessionMgrGuid, 23, IntPtr.Zero, out object sessionMgrObj) == 0 && sessionMgrObj is IAudioSessionManager2 sessionMgr)
                    {
                        if (sessionMgr.GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum) == 0 && sessionEnum != null)
                        {
                            sessionEnum.GetCount(out int count);
                            for (int i = 0; i < count; i++)
                            {
                                if (sessionEnum.GetSession(i, out IAudioSessionControl control) == 0 && control != null)
                                {
                                    if (control is IAudioSessionControl2 control2)
                                    {
                                        control2.GetProcessId(out uint pid);
                                        int intPid = (int)pid;

                                        if (intPid <= 4 || intPid == currentPid || seenPids.Contains(intPid)) continue;

                                        string procName = "Unknown";
                                        string displayName = string.Empty;
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
                                            catch { }
                                        }
                                        catch
                                        {
                                            continue; // Process already terminated
                                        }

                                        // Query peak volume
                                        float peak = 0.0f;
                                        if (control is IAudioMeterInformation meter)
                                        {
                                            meter.GetPeakValue(out peak);
                                        }

                                        control2.GetState(out uint state);
                                        bool isActive = (state == 1) || (peak > 0.001f);

                                        seenPids.Add(intPid);
                                        results.Add(new AppProcessItem
                                        {
                                            ProcessId = intPid,
                                            ProcessName = procName,
                                            DisplayName = string.IsNullOrWhiteSpace(displayName) ? procName : displayName,
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
                }
            }
            catch (Exception)
            {
                // Fallback to desktop process scanning if WASAPI session manager encounters issues
            }

            // 2. Discover running user applications in current interactive desktop session
            // Allows users to select applications before they start playing sound (e.g. Spotify, Chrome, Edge, Games)
            try
            {
                var processes = Process.GetProcesses();
                foreach (var proc in processes)
                {
                    try
                    {
                        int pid = proc.Id;
                        if (pid <= 4 || pid == currentPid || seenPids.Contains(pid)) continue;

                        // Ensure process belongs to the user's interactive session
                        if (currentSessionId >= 0 && proc.SessionId != currentSessionId) continue;

                        string baseName = proc.ProcessName;
                        if (SystemProcessBlacklist.Contains(baseName)) continue;

                        // Filter for applications that either have a main window, or match well-known media/audio apps
                        bool hasWindow = proc.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(proc.MainWindowTitle);
                        bool isKnownAudioApp = IsKnownAudioApplication(baseName);

                        if (hasWindow || isKnownAudioApp)
                        {
                            string exeName = baseName + ".exe";
                            string title = !string.IsNullOrWhiteSpace(proc.MainWindowTitle)
                                ? proc.MainWindowTitle
                                : GetFriendlyNameFromProcess(baseName);

                            // Avoid duplicate background worker instances of the same application without distinct titles
                            if (!hasWindow && results.Any(r => r.ProcessName.Equals(exeName, StringComparison.OrdinalIgnoreCase)))
                            {
                                continue;
                            }

                            string exePath = string.Empty;
                            try
                            {
                                exePath = proc.MainModule?.FileName ?? string.Empty;
                            }
                            catch { }

                            seenPids.Add(pid);
                            results.Add(new AppProcessItem
                            {
                                ProcessId = pid,
                                ProcessName = exeName,
                                DisplayName = title,
                                ExecutablePath = exePath,
                                IsActiveAudio = false,
                                AudioPeak = 0.0f,
                                StatusMessage = "Ready to enhance"
                            });
                        }
                    }
                    catch
                    {
                        // Ignore inaccessible or terminated processes
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
            catch (Exception)
            {
                // Process enumeration fallback
            }

            // Sort: active audio processes first, then alphabetically by display name
            return results
                .OrderByDescending(a => a.IsActiveAudio)
                .ThenBy(a => a.DisplayName)
                .ToList();
        }

        private static bool IsKnownAudioApplication(string processName)
        {
            string lower = processName.ToLowerInvariant();
            return lower.Contains("spotify") ||
                   lower.Contains("chrome") ||
                   lower.Contains("msedge") ||
                   lower.Contains("brave") ||
                   lower.Contains("firefox") ||
                   lower.Contains("opera") ||
                   lower.Contains("vlc") ||
                   lower.Contains("discord") ||
                   lower.Contains("telegram") ||
                   lower.Contains("itunes") ||
                   lower.Contains("musicbee") ||
                   lower.Contains("foobar") ||
                   lower.Contains("wmplayer") ||
                   lower.Contains("steam");
        }

        private static string GetFriendlyNameFromProcess(string processName)
        {
            string lower = processName.ToLowerInvariant();
            if (lower == "msedge") return "Microsoft Edge";
            if (lower == "chrome") return "Google Chrome";
            if (lower == "brave") return "Brave Browser";
            if (lower == "spotify") return "Spotify Music";
            if (lower == "vlc") return "VLC Media Player";
            if (lower == "firefox") return "Mozilla Firefox";
            if (lower == "discord") return "Discord";
            return processName;
        }
    }
}
