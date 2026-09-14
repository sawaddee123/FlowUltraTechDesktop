using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using TiktokStudioSandbox.Core.Interfaces;
using TiktokStudioSandbox.Core.Native;

namespace TiktokStudioSandbox.Core.Services;

public class WindowController : IWindowController
{
    // Cache window handles per port so when hidden, we can restore them!
    private readonly ConcurrentDictionary<int, HashSet<IntPtr>> _cachedPortHandles = new();
    private readonly ConcurrentDictionary<int, HashSet<IntPtr>> _cachedPidHandles = new();

    public List<IntPtr> FindWindowHandlesForProcess(int processId)
    {
        if (processId <= 0 || processId == Environment.ProcessId) return new List<IntPtr>();

        var (_, targetPids) = ProcessTreeHelper.GetProcessTree(processId);
        var handles = new List<IntPtr>();
        int selfPid = Environment.ProcessId;

        Win32.EnumWindows((hWnd, lParam) =>
        {
            Win32.GetWindowThreadProcessId(hWnd, out uint windowPid);
            if (windowPid == (uint)selfPid)
            {
                return true;
            }

            var sb = new StringBuilder(256);
            Win32.GetWindowText(hWnd, sb, 256);
            string title = sb.ToString();
            if (title.Contains("Sandbox", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (targetPids.Contains((int)windowPid))
            {
                handles.Add(hWnd);
            }
            return true;
        }, IntPtr.Zero);

        if (_cachedPidHandles.TryGetValue(processId, out var cached))
        {
            foreach (var h in cached)
            {
                if (!handles.Contains(h)) handles.Add(h);
            }
        }
        else
        {
            _cachedPidHandles[processId] = new HashSet<IntPtr>(handles);
        }

        return handles;
    }

    public List<IntPtr> FindWindowHandlesForPort(int cdpPort, int? processId = null)
    {
        var targetPids = new HashSet<int>();
        if (processId.HasValue && processId.Value > 0 && processId.Value != Environment.ProcessId)
        {
            var (_, pids) = ProcessTreeHelper.GetProcessTree(processId.Value);
            foreach (var p in pids) targetPids.Add(p);
        }

        int? listeningPid = ProcessLauncher.GetPidListeningOnPort(cdpPort);
        if (listeningPid.HasValue && listeningPid.Value != Environment.ProcessId)
        {
            var (_, pids) = ProcessTreeHelper.GetProcessTree(listeningPid.Value);
            foreach (var p in pids) targetPids.Add(p);
        }

        var handles = new List<IntPtr>();
        int selfPid = Environment.ProcessId;

        Win32.EnumWindows((hWnd, lParam) =>
        {
            Win32.GetWindowThreadProcessId(hWnd, out uint windowPid);
            if (windowPid == (uint)selfPid)
            {
                return true; // Strictly exclude Sandbox Manager itself!
            }

            var sb = new StringBuilder(256);
            Win32.GetWindowText(hWnd, sb, 256);
            string title = sb.ToString();
            if (title.Contains("Sandbox", StringComparison.OrdinalIgnoreCase))
            {
                return true; // Strictly exclude Sandbox Manager window!
            }

            if (targetPids.Contains((int)windowPid))
            {
                handles.Add(hWnd);
            }
            return true;
        }, IntPtr.Zero);

        if (!_cachedPortHandles.ContainsKey(cdpPort))
        {
            _cachedPortHandles[cdpPort] = new HashSet<IntPtr>();
        }

        foreach (var h in handles)
        {
            _cachedPortHandles[cdpPort].Add(h);
        }

        return _cachedPortHandles[cdpPort].ToList();
    }

    public bool SetWindowVisibilityForInstance(int? processId, int cdpPort, bool visible)
    {
        var handles = FindWindowHandlesForPort(cdpPort, processId);
        if (handles.Count == 0) return false;

        int command = visible ? Win32.SW_SHOW : Win32.SW_HIDE;
        foreach (var hWnd in handles)
        {
            Win32.ShowWindow(hWnd, command);
            Win32.ShowWindowAsync(hWnd, command);
            if (visible)
            {
                Win32.ShowWindow(hWnd, Win32.SW_RESTORE);
                Win32.SetForegroundWindow(hWnd);
            }
        }
        return true;
    }

    public bool SetWindowVisibility(int processId, bool visible)
    {
        var handles = FindWindowHandlesForProcess(processId);
        if (handles.Count == 0) return false;

        int command = visible ? Win32.SW_SHOW : Win32.SW_HIDE;
        foreach (var hWnd in handles)
        {
            Win32.ShowWindow(hWnd, command);
            Win32.ShowWindowAsync(hWnd, command);
            if (visible)
            {
                Win32.ShowWindow(hWnd, Win32.SW_RESTORE);
                Win32.SetForegroundWindow(hWnd);
            }
        }
        return true;
    }

    public bool IsAnyWindowVisible(int processId)
    {
        var handles = FindWindowHandlesForProcess(processId);
        return handles.Any(Win32.IsWindowVisible);
    }

    public bool IsAnyWindowVisibleForInstance(int? processId, int cdpPort)
    {
        var handles = FindWindowHandlesForPort(cdpPort, processId);
        return handles.Any(Win32.IsWindowVisible);
    }

    public void BringToFront(int processId)
    {
        var handles = FindWindowHandlesForProcess(processId);
        foreach (var hWnd in handles)
        {
            Win32.ShowWindow(hWnd, Win32.SW_RESTORE);
            Win32.SetForegroundWindow(hWnd);
            break;
        }
    }

    private static HashSet<int> GetProcessTreePids(int rootPid)
    {
        if (rootPid <= 0 || rootPid == Environment.ProcessId) return new HashSet<int>();
        var (_, pids) = ProcessTreeHelper.GetProcessTree(rootPid);
        return pids;
    }
}
