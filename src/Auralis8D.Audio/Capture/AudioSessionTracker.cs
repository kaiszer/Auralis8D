using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Auralis8D.Audio.Interop;
using Auralis8D.Audio.Interop.WASAPI;
using Auralis8D.Core.Models;

namespace Auralis8D.Audio.Capture
{
    /// <summary>
    /// Discovers and tracks Windows audio-producing processes and desktop applications.
    /// Correlates live Core Audio WASAPI sessions with user desktop applications and resolves
    /// multi-process application clusters (e.g., Spotify, Google Chrome, Microsoft Edge)
    /// to their root process ID, ensuring loopback capture captures the entire application audio tree.
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

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        /// <summary>
        /// Internal tracking record for clustering processes of the same application.
        /// </summary>
        private class AppCluster
        {
            public int RootPid { get; set; }
            public string ProcessName { get; set; } = string.Empty;
            public string ExecutablePath { get; set; } = string.Empty;
            public string FriendlyName { get; set; } = string.Empty;
            public string WindowTitle { get; set; } = string.Empty;
            public bool HasActiveSession { get; set; }
            public bool IsActiveAudio { get; set; }
            public float PeakVolume { get; set; }
            public string RenderDeviceId { get; set; } = string.Empty;
            public string RenderDeviceName { get; set; } = string.Empty;
            public bool IsIsolatedFromOutput { get; set; }
        }

        /// <summary>
        /// Enumerates all available applications: processes actively playing audio via WASAPI,
        /// plus user desktop applications ready to produce sound.
        /// Deduplicates multi-process applications into a single canonical entry per application,
        /// targeting the root process ID so loopback capture hooks all child worker and audio service processes.
        /// </summary>
        /// <returns>A deduplicated list of available applications sorted by audio activity and display name.</returns>
        public List<AppProcessItem> GetActiveAudioProcesses()
        {
            var clusters = new Dictionary<int, AppCluster>();
            int currentPid = Process.GetCurrentProcess().Id;
            int currentSessionId = -1;

            try
            {
                currentSessionId = Process.GetCurrentProcess().SessionId;
            }
            catch { }

            // Capture point-in-time process tree snapshot using Toolhelp API
            var treeSnapshot = ProcessTreeHelper.GetProcessTreeSnapshot();

            // 1. First Pass: Scan running desktop processes to collect window titles and executable paths
            // This allows us to correlate window titles (e.g. "Maribou State - Midas") with the root process
            var windowTitles = new Dictionary<int, string>();
            var exePaths = new Dictionary<int, string>();

            try
            {
                var processes = Process.GetProcesses();
                foreach (var proc in processes)
                {
                    try
                    {
                        int pid = proc.Id;
                        if (pid <= 4 || pid == currentPid) continue;

                        // Check session match
                        if (currentSessionId >= 0 && proc.SessionId != currentSessionId) continue;

                        string baseName = proc.ProcessName;
                        if (SystemProcessBlacklist.Contains(baseName)) continue;

                        int rootPid = ProcessTreeHelper.GetRootProcessId(pid, treeSnapshot);

                        // Capture executable path if available
                        if (!exePaths.ContainsKey(rootPid))
                        {
                            try
                            {
                                string? fn = proc.MainModule?.FileName;
                                if (!string.IsNullOrEmpty(fn))
                                {
                                    exePaths[rootPid] = fn;
                                }
                            }
                            catch { }
                        }

                        // Capture non-empty main window title for the application cluster
                        if (proc.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(proc.MainWindowTitle))
                        {
                            string title = proc.MainWindowTitle.Trim();
                            bool isDescriptive = !title.Equals(baseName, StringComparison.OrdinalIgnoreCase) &&
                                                 !title.Equals("Spotify Free", StringComparison.OrdinalIgnoreCase) &&
                                                 !title.Equals("Spotify Premium", StringComparison.OrdinalIgnoreCase);

                            // Always update with the latest descriptive track/window title
                            if (isDescriptive || !windowTitles.ContainsKey(rootPid))
                            {
                                windowTitles[rootPid] = title;
                            }
                        }
                    }
                    catch
                    {
                        // Ignore inaccessible or terminating processes
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
            catch (Exception)
            {
                // Fallback to direct process scanning
            }

            // Pass 1b: Use native Win32 EnumWindows to discover active titles on visible windows of secondary render processes
            // (e.g. Spotify, Edge, Chrome where the actual song/video title is hosted on a child window)
            try
            {
                var sb = new System.Text.StringBuilder(512);
                EnumWindows((hWnd, lParam) =>
                {
                    try
                    {
                        if (IsWindowVisible(hWnd))
                        {
                            GetWindowThreadProcessId(hWnd, out uint winPid);
                            int intWinPid = (int)winPid;
                            if (treeSnapshot.TryGetValue(intWinPid, out var procInfo))
                            {
                                int rootPid = ProcessTreeHelper.GetRootProcessId(intWinPid, treeSnapshot);
                                int len = GetWindowTextLength(hWnd);
                                if (len > 0)
                                {
                                    sb.Clear();
                                    GetWindowText(hWnd, sb, sb.Capacity);
                                    string title = sb.ToString().Trim();
                                    if (!string.IsNullOrEmpty(title))
                                    {
                                        string baseName = Path.GetFileNameWithoutExtension(procInfo.ExeFile);
                                        bool isDescriptive = !title.Equals(baseName, StringComparison.OrdinalIgnoreCase) &&
                                                             !title.Equals("Spotify Free", StringComparison.OrdinalIgnoreCase) &&
                                                             !title.Equals("Spotify Premium", StringComparison.OrdinalIgnoreCase);

                                        if (isDescriptive || !windowTitles.ContainsKey(rootPid))
                                        {
                                            windowTitles[rootPid] = title;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }

            // 2. Second Pass: Discover processes with active WASAPI audio sessions across all render devices
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                string defaultDeviceId = string.Empty;
                int defHr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IMMDevice defaultDevice);
                if (defHr == 0 && defaultDevice != null)
                {
                    defaultDevice.GetId(out defaultDeviceId);
                }

                // Collect active render devices to inspect
                var devicesToScan = new List<(IMMDevice Device, string Id, string Name, bool IsDefault)>();
                if (defaultDevice != null && !string.IsNullOrEmpty(defaultDeviceId))
                {
                    devicesToScan.Add((defaultDevice, defaultDeviceId, GetDeviceFriendlyName(defaultDevice), true));
                }

                int enumHr = enumerator.EnumAudioEndpoints(EDataFlow.eRender, 1 /* DEVICE_STATE_ACTIVE */, out IMMDeviceCollection collection);
                if (enumHr == 0 && collection != null)
                {
                    collection.GetCount(out uint count);
                    for (uint i = 0; i < count; i++)
                    {
                        if (collection.Item(i, out IMMDevice dev) == 0 && dev != null)
                        {
                            dev.GetId(out string devId);
                            if (!string.IsNullOrEmpty(devId) && !devicesToScan.Any(d => d.Id.Equals(devId, StringComparison.OrdinalIgnoreCase)))
                            {
                                devicesToScan.Add((dev, devId, GetDeviceFriendlyName(dev), false));
                            }
                        }
                    }
                }

                Guid sessionMgrGuid = typeof(IAudioSessionManager2).GUID;
                foreach (var deviceEntry in devicesToScan)
                {
                    var dev = deviceEntry.Device;
                    string devId = deviceEntry.Id;
                    string devName = deviceEntry.Name;
                    bool isDefaultEndpoint = deviceEntry.IsDefault;

                    if (dev.Activate(ref sessionMgrGuid, 23, IntPtr.Zero, out object sessionMgrObj) == 0 && sessionMgrObj is IAudioSessionManager2 sessionMgr)
                    {
                        if (sessionMgr.GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum) == 0 && sessionEnum != null)
                        {
                            sessionEnum.GetCount(out int sessionCount);
                            for (int i = 0; i < sessionCount; i++)
                            {
                                if (sessionEnum.GetSession(i, out IAudioSessionControl control) == 0 && control != null)
                                {
                                    if (control is IAudioSessionControl2 control2)
                                    {
                                        control2.GetProcessId(out uint pid);
                                        int intPid = (int)pid;

                                        if (intPid <= 4 || intPid == currentPid) continue;

                                        int rootPid = ProcessTreeHelper.GetRootProcessId(intPid, treeSnapshot);

                                        float peak = 0.0f;
                                        if (control is IAudioMeterInformation meter)
                                        {
                                            meter.GetPeakValue(out peak);
                                        }

                                        control2.GetState(out uint state);
                                        bool isSessionActive = (state == 1) || (peak > 0.001f);
                                        bool isIsolated = !string.IsNullOrEmpty(defaultDeviceId) &&
                                                          !string.Equals(devId, defaultDeviceId, StringComparison.OrdinalIgnoreCase);

                                        if (!clusters.TryGetValue(rootPid, out var cluster))
                                        {
                                            string procName = "Unknown.exe";
                                            string friendlyName = "Unknown";
                                            try
                                            {
                                                using var rootProc = Process.GetProcessById(rootPid);
                                                procName = rootProc.ProcessName + ".exe";
                                                friendlyName = GetFriendlyNameFromProcess(rootProc.ProcessName);
                                            }
                                            catch
                                            {
                                                if (treeSnapshot.TryGetValue(rootPid, out var info))
                                                {
                                                    procName = info.ExeFile;
                                                    friendlyName = GetFriendlyNameFromProcess(Path.GetFileNameWithoutExtension(info.ExeFile));
                                                }
                                            }

                                            cluster = new AppCluster
                                            {
                                                RootPid = rootPid,
                                                ProcessName = procName,
                                                FriendlyName = friendlyName,
                                                ExecutablePath = exePaths.TryGetValue(rootPid, out var ep) ? ep : string.Empty,
                                                WindowTitle = windowTitles.TryGetValue(rootPid, out var wt) ? wt : string.Empty,
                                                HasActiveSession = true,
                                                IsActiveAudio = isSessionActive,
                                                PeakVolume = peak,
                                                RenderDeviceId = devId,
                                                RenderDeviceName = devName,
                                                IsIsolatedFromOutput = isIsolated
                                            };
                                            clusters[rootPid] = cluster;
                                        }
                                        else
                                        {
                                            cluster.HasActiveSession = true;
                                            if (isSessionActive)
                                            {
                                                cluster.IsActiveAudio = true;
                                                cluster.RenderDeviceId = devId;
                                                cluster.RenderDeviceName = devName;
                                                cluster.IsIsolatedFromOutput = isIsolated;
                                            }
                                            if (peak > cluster.PeakVolume)
                                            {
                                                cluster.PeakVolume = peak;
                                                cluster.RenderDeviceId = devId;
                                                cluster.RenderDeviceName = devName;
                                                cluster.IsIsolatedFromOutput = isIsolated;
                                            }
                                            if (windowTitles.TryGetValue(rootPid, out var wt) && !string.IsNullOrEmpty(wt))
                                            {
                                                cluster.WindowTitle = wt;
                                            }
                                        }
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

            // 3. Third Pass: Add desktop applications ready to produce sound (not yet playing)
            try
            {
                var processes = Process.GetProcesses();
                foreach (var proc in processes)
                {
                    try
                    {
                        int pid = proc.Id;
                        if (pid <= 4 || pid == currentPid) continue;
                        if (currentSessionId >= 0 && proc.SessionId != currentSessionId) continue;

                        string baseName = proc.ProcessName;
                        if (SystemProcessBlacklist.Contains(baseName)) continue;

                        int rootPid = ProcessTreeHelper.GetRootProcessId(pid, treeSnapshot);

                        // If already tracked as an audio session cluster, update window title with latest title
                        if (clusters.TryGetValue(rootPid, out var existingCluster))
                        {
                            if (windowTitles.TryGetValue(rootPid, out var wt) && !string.IsNullOrEmpty(wt))
                            {
                                existingCluster.WindowTitle = wt;
                            }
                            continue;
                        }

                        bool hasWindow = proc.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(proc.MainWindowTitle);
                        bool isKnownAudioApp = IsKnownAudioApplication(baseName);

                        if (hasWindow || isKnownAudioApp)
                        {
                            string exeName = baseName + ".exe";
                            string friendly = GetFriendlyNameFromProcess(baseName);
                            string windowTitle = windowTitles.TryGetValue(rootPid, out var foundTitle) ? foundTitle : (hasWindow ? proc.MainWindowTitle.Trim() : string.Empty);
                            string exePath = exePaths.TryGetValue(rootPid, out var foundPath) ? foundPath : string.Empty;

                            clusters[rootPid] = new AppCluster
                            {
                                RootPid = rootPid,
                                ProcessName = exeName,
                                FriendlyName = friendly,
                                WindowTitle = windowTitle,
                                ExecutablePath = exePath,
                                HasActiveSession = false,
                                IsActiveAudio = false,
                                PeakVolume = 0.0f
                            };
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
                // Desktop process scan fallback
            }

            // 4. Construct clean, deduplicated AppProcessItem list
            var results = new List<AppProcessItem>();
            foreach (var cluster in clusters.Values)
            {
                // Determine the best display name: prefer window title if descriptive, otherwise friendly application name
                string displayName = cluster.FriendlyName;
                if (!string.IsNullOrWhiteSpace(cluster.WindowTitle))
                {
                    displayName = cluster.WindowTitle;
                }

                string status;
                if (cluster.IsActiveAudio)
                {
                    status = "Playing audio";
                }
                else if (cluster.HasActiveSession)
                {
                    status = "Session active (silent)";
                }
                else
                {
                    status = "Ready to enhance";
                }

                results.Add(new AppProcessItem
                {
                    ProcessId = cluster.RootPid,
                    ProcessName = cluster.ProcessName,
                    DisplayName = displayName,
                    ExecutablePath = cluster.ExecutablePath,
                    IsActiveAudio = cluster.IsActiveAudio,
                    AudioPeak = cluster.PeakVolume,
                    StatusMessage = status,
                    RenderDeviceId = cluster.RenderDeviceId,
                    RenderDeviceName = cluster.RenderDeviceName,
                    IsIsolatedFromOutput = cluster.IsIsolatedFromOutput
                });
            }

            // Sort: active audio processes first, then alphabetically by display name
            return results
                .OrderByDescending(a => a.IsActiveAudio)
                .ThenBy(a => a.DisplayName)
                .ToList();
        }

        /// <summary>
        /// Queries the friendly display name of an audio device from its Core Audio property store.
        /// </summary>
        /// <param name="device">IMMDevice instance.</param>
        /// <returns>Friendly device name or fallback string.</returns>
        private static string GetDeviceFriendlyName(IMMDevice device)
        {
            try
            {
                if (device.OpenPropertyStore(0 /* STGM_READ */, out IPropertyStore store) == 0 && store != null)
                {
                    PROPERTYKEY key = new PROPERTYKEY
                    {
                        fmtid = AudioGuids.PKEY_Device_FriendlyName,
                        pid = 14
                    };

                    if (store.GetValue(ref key, out PROPVARIANT pv) == 0)
                    {
                        if (pv.pointerVal != IntPtr.Zero)
                        {
                            string? name = Marshal.PtrToStringUni(pv.pointerVal);
                            NativeAudioMethods.PropVariantClear(ref pv);
                            return name ?? string.Empty;
                        }
                    }
                }
            }
            catch (Exception) { }

            return "Audio Output Device";
        }

        /// <summary>
        /// Checks if a process name corresponds to a well-known media or audio-producing application.
        /// </summary>
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

        /// <summary>
        /// Formats executable names into clean user-friendly labels.
        /// </summary>
        private static string GetFriendlyNameFromProcess(string processName)
        {
            string lower = processName.ToLowerInvariant();
            if (lower == "msedge") return "Microsoft Edge";
            if (lower == "chrome") return "Google Chrome";
            if (lower == "brave") return "Brave Browser";
            if (lower == "spotify") return "Spotify";
            if (lower == "vlc") return "VLC Media Player";
            if (lower == "firefox") return "Mozilla Firefox";
            if (lower == "discord") return "Discord";
            return processName;
        }
    }
}
