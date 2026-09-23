using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using FlowUltraTech.Core.Interfaces;
using FlowUltraTech.Core.Models;
using FlowUltraTech.Core.Native;

namespace FlowUltraTech.Core.Services;

public class WindowController : IWindowController
{
    // Cache window handles per port so when hidden, we can restore them!
    private readonly ConcurrentDictionary<int, HashSet<IntPtr>> _cachedPortHandles = new();
    private readonly ConcurrentDictionary<int, HashSet<IntPtr>> _cachedPidHandles = new();

    public static bool IsRealStudioWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !Win32.IsWindow(hWnd)) return false;

        var sbClass = new StringBuilder(256);
        Win32.GetClassName(hWnd, sbClass, 256);
        string className = sbClass.ToString();

        // Strictly only accept Chrome_WidgetWin_1 (the Electron UI window class for TikTok LIVE Studio)
        // Disqualifies ConsoleWindowClass, Chrome_WidgetWin_0, IME, Cicero, etc.
        if (!className.Equals("Chrome_WidgetWin_1", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var sbTitle = new StringBuilder(256);
        Win32.GetWindowText(hWnd, sbTitle, 256);
        string title = sbTitle.ToString();

        // Must have a non-empty title! Background CEF helper/render windows in Chrome_WidgetWin_1 have empty title.
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        // Strictly disqualify background webviews / browser navigation pages / sandboxes
        if (title.Contains("Make Your Day", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("For You", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("Chrome Legacy Window", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("Sandbox", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Minimized windows (IsIconic) are real windows that need to be restored
        if (Win32.IsIconic(hWnd))
        {
            return true;
        }

        if (!Win32.GetWindowRect(hWnd, out var rect)) return false;

        // In Electron apps, the primary UI window has class Chrome_WidgetWin_1 with substantial size
        if (className.Equals("Chrome_WidgetWin_1", StringComparison.OrdinalIgnoreCase))
        {
            return rect.Width >= 400 && rect.Height >= 250;
        }

        // Other windows (e.g. popups or dialogs): only consider if substantial UI size
        return rect.Width >= 500 && rect.Height >= 300;
    }

    public static int GetWindowArea(IntPtr hWnd)
    {
        if (Win32.IsIconic(hWnd))
        {
            var wp = new Win32.WINDOWPLACEMENT();
            wp.length = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Win32.WINDOWPLACEMENT));
            if (Win32.GetWindowPlacement(hWnd, ref wp))
            {
                return wp.rcNormalPosition.Width * wp.rcNormalPosition.Height;
            }
        }
        if (Win32.GetWindowRect(hWnd, out var rect))
        {
            return rect.Width * rect.Height;
        }
        return 0;
    }

    public static int GetWindowScore(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !Win32.IsWindow(hWnd)) return 0;
        int score = 0;

        var sbTitle = new StringBuilder(256);
        Win32.GetWindowText(hWnd, sbTitle, 256);
        string title = sbTitle.ToString();

        // Disqualify webviews immediately
        if (title.Contains("Make Your Day", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("For You", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase))
        {
            return -1000000;
        }

        // Highest priority: Genuine TikTok Studio main window
        if (title.Contains("TikTok", StringComparison.OrdinalIgnoreCase) && 
            (title.Contains("Studio", StringComparison.OrdinalIgnoreCase) || title.Contains("Live", StringComparison.OrdinalIgnoreCase)))
        {
            score += 10000000;
        }
        else if (title.Contains("Studio", StringComparison.OrdinalIgnoreCase) || title.Contains("Live", StringComparison.OrdinalIgnoreCase))
        {
            score += 5000000;
        }
        else if (title.Contains("TikTok", StringComparison.OrdinalIgnoreCase))
        {
            score += 1000000;
        }
        else
        {
            score -= 5000000;
        }

        if (Win32.IsWindowVisible(hWnd))
        {
            score += 2000000;
        }

        int area = GetWindowArea(hWnd);
        // Bonus for large main dashboard windows (e.g. >= 900x550 = 495,000)
        if (area >= 500000)
        {
            score += 3000000;
        }

        score += Math.Min(area, 2000000);
        return score;
    }

    public static void BringToFrontRockSolid(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !Win32.IsWindow(hWnd)) return;

        if (Win32.IsIconic(hWnd))
        {
            Win32.PostMessage(hWnd, Win32.WM_SYSCOMMAND, (IntPtr)Win32.SC_RESTORE, IntPtr.Zero);
            Win32.ShowWindow(hWnd, Win32.SW_RESTORE);
        }
        Win32.ShowWindow(hWnd, Win32.SW_SHOWNORMAL);
        Win32.ShowWindow(hWnd, Win32.SW_SHOW);

        // Check if window coordinates are off-screen (e.g. negative coordinates or disconnected monitor)
        if (Win32.GetWindowRect(hWnd, out var rect))
        {
            if (rect.Left < -5000 || rect.Top < -5000)
            {
                Win32.SetWindowPos(hWnd, Win32.HWND_TOP, 100, 100, 0, 0,
                    Win32.SWP_NOSIZE | Win32.SWP_SHOWWINDOW);
            }
        }

        IntPtr fgWnd = Win32.GetForegroundWindow();
        uint fgThread = 0;
        Win32.GetWindowThreadProcessId(fgWnd, out fgThread);
        uint curThread = Win32.GetCurrentThreadId();

        if (fgThread != curThread && fgThread != 0)
        {
            Win32.AttachThreadInput(curThread, fgThread, true);
            Win32.BringWindowToTop(hWnd);
            Win32.SetForegroundWindow(hWnd);
            Win32.AttachThreadInput(curThread, fgThread, false);
        }
        else
        {
            Win32.BringWindowToTop(hWnd);
            Win32.SetForegroundWindow(hWnd);
        }
    }

    public List<IntPtr> FindWindowHandlesForProcess(int processId)
    {
        if (processId <= 0 || processId == Environment.ProcessId) return new List<IntPtr>();

        var (_, targetPids) = ProcessTreeHelper.GetProcessTree(processId);
        var handles = new List<IntPtr>();
        int selfPid = Environment.ProcessId;

        Win32.EnumWindows((hWnd, lParam) =>
        {
            Win32.GetWindowThreadProcessId(hWnd, out uint windowPid);
            if (windowPid == (uint)selfPid) return true;

            if (!targetPids.Contains((int)windowPid)) return true;

            var sb = new StringBuilder(256);
            Win32.GetWindowText(hWnd, sb, 256);
            string title = sb.ToString();
            if (title.Contains("Sandbox", StringComparison.OrdinalIgnoreCase)) return true;

            if (IsRealStudioWindow(hWnd))
            {
                handles.Add(hWnd);
            }
            return true;
        }, IntPtr.Zero);

        var liveCache = new HashSet<IntPtr>();
        if (_cachedPidHandles.TryGetValue(processId, out var cached))
        {
            foreach (var h in cached)
            {
                if (Win32.IsWindow(h) && IsRealStudioWindow(h))
                {
                    Win32.GetWindowThreadProcessId(h, out uint wPid);
                    if (targetPids.Contains((int)wPid))
                    {
                        liveCache.Add(h);
                    }
                }
            }
        }
        foreach (var h in handles) liveCache.Add(h);
        _cachedPidHandles[processId] = liveCache;

        return liveCache.OrderByDescending(GetWindowScore).ToList();
    }

    public List<IntPtr> FindWindowHandlesForPort(int cdpPort, int? processId = null)
    {
        var targetPids = new HashSet<int>();
        if (processId.HasValue && processId.Value > 0 && processId.Value != Environment.ProcessId)
        {
            var (_, pids) = ProcessTreeHelper.GetProcessTree(processId.Value);
            foreach (var p in pids) targetPids.Add(p);
        }

        // Only query netstat if we do not already have target PIDs from the process tree
        if (targetPids.Count == 0)
        {
            int? listeningPid = ProcessLauncher.GetPidListeningOnPort(cdpPort);
            if (listeningPid.HasValue && listeningPid.Value != Environment.ProcessId)
            {
                var (_, pids) = ProcessTreeHelper.GetProcessTree(listeningPid.Value);
                foreach (var p in pids) targetPids.Add(p);
            }
        }

        var handles = new List<IntPtr>();
        int selfPid = Environment.ProcessId;

        Win32.EnumWindows((hWnd, lParam) =>
        {
            Win32.GetWindowThreadProcessId(hWnd, out uint windowPid);
            if (windowPid == (uint)selfPid) return true;

            if (!targetPids.Contains((int)windowPid)) return true;

            var sb = new StringBuilder(256);
            Win32.GetWindowText(hWnd, sb, 256);
            string title = sb.ToString();
            if (title.Contains("Sandbox", StringComparison.OrdinalIgnoreCase)) return true;

            if (IsRealStudioWindow(hWnd))
            {
                handles.Add(hWnd);
            }
            return true;
        }, IntPtr.Zero);

        var liveCache = new HashSet<IntPtr>();
        if (_cachedPortHandles.TryGetValue(cdpPort, out var cached))
        {
            foreach (var h in cached)
            {
                if (Win32.IsWindow(h) && IsRealStudioWindow(h))
                {
                    Win32.GetWindowThreadProcessId(h, out uint wPid);
                    if (targetPids.Contains((int)wPid))
                    {
                        liveCache.Add(h);
                    }
                }
            }
        }
        foreach (var h in handles) liveCache.Add(h);
        _cachedPortHandles[cdpPort] = liveCache;

        return liveCache.OrderByDescending(GetWindowScore).ToList();
    }

    public void ClearCacheForPort(int cdpPort)
    {
        _cachedPortHandles.TryRemove(cdpPort, out _);
    }

    public List<StudioWindowHandleInfo> GetWindowHandlesInfoForInstance(int? processId, int cdpPort)
    {
        var handles = FindWindowHandlesForPort(cdpPort, processId);
        var result = new List<StudioWindowHandleInfo>();
        if (handles.Count == 0) return result;

        IntPtr primary = handles.First();
        IntPtr modal = IntPtr.Zero;
        if (handles.Count > 1)
        {
            modal = handles.FirstOrDefault(h => h != primary && Win32.GetWindow(h, Win32.GW_OWNER) == primary);
        }

        foreach (var hWnd in handles)
        {
            if (!Win32.IsWindow(hWnd)) continue;

            var sbTitle = new StringBuilder(256);
            Win32.GetWindowText(hWnd, sbTitle, 256);
            string title = sbTitle.ToString();

            var sbClass = new StringBuilder(256);
            Win32.GetClassName(hWnd, sbClass, 256);
            string className = sbClass.ToString();

            Win32.GetWindowThreadProcessId(hWnd, out uint wPid);
            bool isVis = Win32.IsWindowVisible(hWnd);
            bool isIconic = Win32.IsIconic(hWnd);

            int width = 0;
            int height = 0;
            if (Win32.GetWindowRect(hWnd, out var rect))
            {
                width = rect.Width;
                height = rect.Height;
            }

            bool isPrim = (hWnd == primary);
            string role = isPrim ? "หน้าต่างหลัก (Main)" : (hWnd == modal ? "Child Modal" : "Auxiliary / Background");

            result.Add(new StudioWindowHandleInfo
            {
                Handle = hWnd,
                Title = string.IsNullOrWhiteSpace(title) ? "(ไม่มีชื่อ Title)" : title,
                ClassName = className,
                ProcessId = (int)wPid,
                Width = width,
                Height = height,
                IsVisible = isVis,
                IsIconic = isIconic,
                IsPrimary = isPrim,
                WindowRole = role
            });
        }

        return result;
    }

    public bool SetSingleWindowVisibility(IntPtr hWnd, bool visible)
    {
        if (hWnd == IntPtr.Zero || !Win32.IsWindow(hWnd)) return false;

        if (visible)
        {
            if (Win32.IsIconic(hWnd))
            {
                Win32.PostMessage(hWnd, Win32.WM_SYSCOMMAND, (IntPtr)Win32.SC_RESTORE, IntPtr.Zero);
                Win32.ShowWindow(hWnd, Win32.SW_RESTORE);
            }
            Win32.ShowWindow(hWnd, Win32.SW_SHOWNORMAL);
            Win32.ShowWindowAsync(hWnd, Win32.SW_SHOWNORMAL);
            Win32.BringWindowToTop(hWnd);
        }
        else
        {
            Win32.PostMessage(hWnd, Win32.WM_SYSCOMMAND, (IntPtr)Win32.SC_MINIMIZE, IntPtr.Zero);
            Win32.ShowWindow(hWnd, Win32.SW_HIDE);
            Win32.ShowWindowAsync(hWnd, Win32.SW_HIDE);
            Win32.SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0,
                Win32.SWP_NOSIZE | Win32.SWP_NOMOVE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_HIDEWINDOW);
        }
        return true;
    }

    public bool SetAllWindowsVisibilityForInstance(int? processId, int cdpPort, bool visible)
    {
        var handles = FindWindowHandlesForPort(cdpPort, processId);
        if (handles.Count == 0) return false;

        foreach (var hWnd in handles)
        {
            SetSingleWindowVisibility(hWnd, visible);
        }
        return true;
    }


    public bool SetWindowVisibilityForInstance(int? processId, int cdpPort, bool visible)
    {
        var handles = FindWindowHandlesForPort(cdpPort, processId);
        if (handles.Count == 0) return false;

        if (visible)
        {
            var primary = handles.First();
            BringToFrontRockSolid(primary);

            // If there is an active child modal (e.g. Login window or Settings popup) owned by primary, bring it to the front on top of primary
            IntPtr modal = IntPtr.Zero;
            if (handles.Count > 1)
            {
                modal = handles.FirstOrDefault(h => h != primary && Win32.GetWindow(h, Win32.GW_OWNER) == primary);
                if (modal != IntPtr.Zero)
                {
                    BringToFrontRockSolid(modal);
                }
            }

            // Ensure auxiliary / background / old splash windows belonging to this process are hidden so they never clutter the desktop
            foreach (var h in handles)
            {
                if (h != primary && h != modal)
                {
                    if (Win32.IsWindowVisible(h))
                    {
                        Win32.ShowWindow(h, Win32.SW_HIDE);
                    }
                }
            }
        }
        else
        {
            foreach (var hWnd in handles)
            {
                Win32.PostMessage(hWnd, Win32.WM_SYSCOMMAND, (IntPtr)Win32.SC_MINIMIZE, IntPtr.Zero);
                Win32.ShowWindow(hWnd, Win32.SW_HIDE);
                Win32.ShowWindowAsync(hWnd, Win32.SW_HIDE);
                Win32.SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0,
                    Win32.SWP_NOSIZE | Win32.SWP_NOMOVE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_HIDEWINDOW);
            }
        }
        return true;
    }

    public bool SetWindowVisibility(int processId, bool visible)
    {
        var handles = FindWindowHandlesForProcess(processId);
        if (handles.Count == 0) return false;

        if (visible)
        {
            var primary = handles.First();
            BringToFrontRockSolid(primary);

            IntPtr modal = IntPtr.Zero;
            if (handles.Count > 1)
            {
                modal = handles.FirstOrDefault(h => h != primary && Win32.GetWindow(h, Win32.GW_OWNER) == primary);
                if (modal != IntPtr.Zero)
                {
                    BringToFrontRockSolid(modal);
                }
            }

            foreach (var h in handles)
            {
                if (h != primary && h != modal)
                {
                    if (Win32.IsWindowVisible(h))
                    {
                        Win32.ShowWindow(h, Win32.SW_HIDE);
                    }
                }
            }
        }
        else
        {
            foreach (var hWnd in handles)
            {
                Win32.PostMessage(hWnd, Win32.WM_SYSCOMMAND, (IntPtr)Win32.SC_MINIMIZE, IntPtr.Zero);
                Win32.ShowWindow(hWnd, Win32.SW_HIDE);
                Win32.ShowWindowAsync(hWnd, Win32.SW_HIDE);
                Win32.SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0,
                    Win32.SWP_NOSIZE | Win32.SWP_NOMOVE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_HIDEWINDOW);
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
        var primary = handles.FirstOrDefault();
        if (primary != IntPtr.Zero)
        {
            BringToFrontRockSolid(primary);
        }
    }
}
