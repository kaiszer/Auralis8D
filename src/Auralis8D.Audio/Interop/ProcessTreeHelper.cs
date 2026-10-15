using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Auralis8D.Audio.Interop
{
    /// <summary>
    /// Helper utility for resolving Windows process hierarchies and application clusters.
    /// Multi-process desktop applications (e.g. Spotify, Chrome, Microsoft Edge, Discord)
    /// distribute functionality across a root process, renderer processes, GPU processes,
    /// and dedicated audio service processes.
    /// This utility maps child processes back to their root application process to ensure
    /// WASAPI Application Loopback Capture attaches to the correct process tree root.
    /// </summary>
    public static class ProcessTreeHelper
    {
        private const uint TH32CS_SNAPPROCESS = 0x00000002;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        /// <summary>
        /// Captures a point-in-time snapshot of all active processes and their parent PIDs.
        /// Uses standard Win32 Toolhelp API to avoid needing elevated process handle rights.
        /// </summary>
        /// <returns>Dictionary mapping ProcessId to a tuple of (ParentProcessId, ExecutableFileName).</returns>
        public static Dictionary<int, (int ParentPid, string ExeFile)> GetProcessTreeSnapshot()
        {
            var map = new Dictionary<int, (int, string)>();
            IntPtr hSnap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (hSnap == IntPtr.Zero || hSnap == (IntPtr)(-1))
            {
                return map;
            }

            try
            {
                var entry = new PROCESSENTRY32
                {
                    dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>()
                };

                if (Process32First(hSnap, ref entry))
                {
                    do
                    {
                        map[(int)entry.th32ProcessID] = ((int)entry.th32ParentProcessID, entry.szExeFile);
                    }
                    while (Process32Next(hSnap, ref entry));
                }
            }
            finally
            {
                CloseHandle(hSnap);
            }

            return map;
        }

        /// <summary>
        /// Traverses up the process parent hierarchy to find the top-most root process
        /// that shares the same executable name or image family.
        /// </summary>
        /// <param name="pid">Target process ID to resolve.</param>
        /// <returns>Root process ID if found; otherwise the original pid.</returns>
        public static int GetRootProcessId(int pid)
        {
            if (pid <= 4) return pid;

            var tree = GetProcessTreeSnapshot();
            return GetRootProcessId(pid, tree);
        }

        /// <summary>
        /// Traverses up the process parent hierarchy within a pre-captured process tree snapshot.
        /// Resolves child worker/renderer PIDs (such as Spotify's UI window or audio service)
        /// to the root application process ID.
        /// </summary>
        /// <param name="pid">Process ID to resolve.</param>
        /// <param name="tree">Pre-captured snapshot dictionary from GetProcessTreeSnapshot.</param>
        /// <returns>Root process ID for the application cluster.</returns>
        public static int GetRootProcessId(int pid, Dictionary<int, (int ParentPid, string ExeFile)> tree)
        {
            if (!tree.TryGetValue(pid, out var info))
            {
                return pid;
            }

            string exeName = info.ExeFile;
            int currentPid = pid;

            // Follow parent links as long as the parent has the same executable name
            // and is a valid user-mode process (PID > 4).
            while (tree.TryGetValue(currentPid, out var currentInfo))
            {
                int parentPid = currentInfo.ParentPid;
                if (parentPid <= 4 || parentPid == currentPid)
                {
                    break;
                }

                if (tree.TryGetValue(parentPid, out var parentInfo) &&
                    string.Equals(parentInfo.ExeFile, exeName, StringComparison.OrdinalIgnoreCase))
                {
                    currentPid = parentPid;
                }
                else
                {
                    break;
                }
            }

            return currentPid;
        }

        /// <summary>
        /// Finds all descendant process IDs belonging to the given root process ID
        /// that share the same executable image name.
        /// </summary>
        /// <param name="rootPid">The root process ID.</param>
        /// <param name="tree">Pre-captured process tree snapshot.</param>
        /// <returns>List of all PIDs belonging to this application cluster.</returns>
        public static List<int> GetClusterProcessIds(int rootPid, Dictionary<int, (int ParentPid, string ExeFile)> tree)
        {
            var results = new List<int> { rootPid };
            if (!tree.TryGetValue(rootPid, out var rootInfo))
            {
                return results;
            }

            string exeName = rootInfo.ExeFile;
            var queue = new Queue<int>();
            queue.Enqueue(rootPid);

            var visited = new HashSet<int> { rootPid };

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (var kvp in tree)
                {
                    int childPid = kvp.Key;
                    var childInfo = kvp.Value;

                    if (childInfo.ParentPid == current &&
                        !visited.Contains(childPid) &&
                        string.Equals(childInfo.ExeFile, exeName, StringComparison.OrdinalIgnoreCase))
                    {
                        visited.Add(childPid);
                        results.Add(childPid);
                        queue.Enqueue(childPid);
                    }
                }
            }

            return results;
        }
    }
}
