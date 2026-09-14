using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TiktokStudioSandbox.Core.Native;

namespace TiktokStudioSandbox.Core.Services;

public static class ProcessTreeHelper
{
    public class ProcessSnapshot
    {
        public Dictionary<int, int> ParentMap { get; } = new();
        public Dictionary<int, List<int>> ChildrenMap { get; } = new();
        public Dictionary<int, string> ExeNameMap { get; } = new();
    }

    public static ProcessSnapshot CreateSnapshot()
    {
        var snapshot = new ProcessSnapshot();
        IntPtr hSnapshot = Win32.CreateToolhelp32Snapshot(Win32.TH32CS_SNAPPROCESS, 0);
        if (hSnapshot == IntPtr.Zero || hSnapshot == (IntPtr)(-1))
        {
            return snapshot;
        }

        try
        {
            var entry = new Win32.PROCESSENTRY32
            {
                dwSize = (uint)Marshal.SizeOf(typeof(Win32.PROCESSENTRY32))
            };

            if (Win32.Process32First(hSnapshot, ref entry))
            {
                do
                {
                    int pid = (int)entry.th32ProcessID;
                    int parentPid = (int)entry.th32ParentProcessID;
                    string exeName = entry.szExeFile;

                    snapshot.ExeNameMap[pid] = exeName;
                    snapshot.ParentMap[pid] = parentPid;

                    if (!snapshot.ChildrenMap.TryGetValue(parentPid, out var list))
                    {
                        list = new List<int>();
                        snapshot.ChildrenMap[parentPid] = list;
                    }
                    list.Add(pid);

                } while (Win32.Process32Next(hSnapshot, ref entry));
            }
        }
        finally
        {
            Win32.CloseHandle(hSnapshot);
        }

        return snapshot;
    }

    /// <summary>
    /// Given any PID in an instance (root, child, or listening process), traverses up to the root
    /// and down to all descendants to find the complete process tree belonging to this instance.
    /// Strictly excludes current process (Environment.ProcessId).
    /// </summary>
    public static (int RootPid, HashSet<int> AllPids) GetProcessTree(int startPid, ProcessSnapshot? snapshot = null)
    {
        snapshot ??= CreateSnapshot();
        int selfPid = Environment.ProcessId;

        var allPids = new HashSet<int>();
        if (startPid <= 0 || startPid == selfPid)
        {
            return (startPid, allPids);
        }

        // 1. Trace upwards to find the topmost TikTok LIVE Studio ancestor
        int curr = startPid;
        int rootPid = startPid;

        while (snapshot.ParentMap.TryGetValue(curr, out int parentPid) && parentPid > 0 && parentPid != selfPid)
        {
            if (snapshot.ExeNameMap.TryGetValue(parentPid, out string? parentExe) &&
                parentExe.Contains("TikTok", StringComparison.OrdinalIgnoreCase))
            {
                curr = parentPid;
                rootPid = parentPid;
            }
            else
            {
                break;
            }
        }

        // 2. Trace downwards from rootPid to collect all descendants
        var queue = new Queue<int>();
        queue.Enqueue(rootPid);
        allPids.Add(rootPid);

        while (queue.Count > 0)
        {
            int p = queue.Dequeue();
            if (snapshot.ChildrenMap.TryGetValue(p, out var children))
            {
                foreach (var child in children)
                {
                    if (child != selfPid && allPids.Add(child))
                    {
                        queue.Enqueue(child);
                    }
                }
            }
        }

        // Never include self PID
        allPids.Remove(selfPid);

        return (rootPid, allPids);
    }
}
