using System;
using System.Runtime.InteropServices;
using System.Text;

namespace FlowUltraTech.App.Helpers;

public static class DesktopHelper
{
    private const uint DESKTOP_ALL_ACCESS = 0x01FF;
    private const uint WINSTA_ALL_ACCESS = 0x037F;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenWindowStation(string lpszWinSta, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessWindowStation(IntPtr hWinSta);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDesktop(uint dwThreadId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, StringBuilder pvInfo, int nLength, out int lpnLengthNeeded);

    public static string GetCurrentDesktopName()
    {
        try
        {
            uint tid = GetCurrentThreadId();
            IntPtr cur = GetThreadDesktop(tid);
            var sb = new StringBuilder(256);
            if (GetUserObjectInformation(cur, 2, sb, 256, out _))
            {
                return sb.ToString();
            }
        }
        catch { }
        return string.Empty;
    }

    public static bool EnsureInteractiveDesktop(Action<string>? logger = null)
    {
        try
        {
            // 1. Attach process to interactive WindowStation WinSta0 if not already attached
            IntPtr hWinSta = OpenWindowStation("WinSta0", false, WINSTA_ALL_ACCESS);
            if (hWinSta != IntPtr.Zero)
            {
                SetProcessWindowStation(hWinSta);
            }

            // 2. Open interactive 'Default' desktop
            IntPtr hDefault = OpenDesktop("Default", 0, false, DESKTOP_ALL_ACCESS);
            if (hDefault != IntPtr.Zero)
            {
                // 3. SetThreadDesktop MUST be called on this thread BEFORE any GetThreadDesktop or GDI/USER call!
                bool switched = SetThreadDesktop(hDefault);
                if (switched)
                {
                    logger?.Invoke("[DesktopHelper] Successfully switched thread desktop to interactive 'Default' desktop!");
                    return true;
                }
                else
                {
                    int err = Marshal.GetLastWin32Error();
                    logger?.Invoke($"[DesktopHelper] SetThreadDesktop('Default') failed. Error code: {err}");
                }
            }
            else
            {
                int err = Marshal.GetLastWin32Error();
                logger?.Invoke($"[DesktopHelper] OpenDesktop('Default') failed. Error code: {err}");
            }
        }
        catch (Exception ex)
        {
            logger?.Invoke($"[DesktopHelper] Exception during desktop switch: {ex.Message}");
        }
        return false;
    }
}
