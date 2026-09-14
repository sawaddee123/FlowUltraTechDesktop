using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TiktokStudioSandbox.App.Helpers;

public static class DesktopHelper
{
    private const uint DESKTOP_ALL_ACCESS = 0x01FF;

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
            string currentDesk = GetCurrentDesktopName();
            logger?.Invoke($"[DesktopHelper] Current thread desktop: '{currentDesk}'");

            if (string.Equals(currentDesk, "Default", StringComparison.OrdinalIgnoreCase))
            {
                logger?.Invoke("[DesktopHelper] Thread is already attached to interactive 'Default' desktop.");
                return true;
            }

            logger?.Invoke("[DesktopHelper] Attempting to attach thread to interactive 'Default' desktop...");
            IntPtr hDefault = OpenDesktop("Default", 0, false, DESKTOP_ALL_ACCESS);
            if (hDefault == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                logger?.Invoke($"[DesktopHelper] OpenDesktop('Default') failed. Error code: {err}");
                return false;
            }

            bool switched = SetThreadDesktop(hDefault);
            if (!switched)
            {
                int err = Marshal.GetLastWin32Error();
                logger?.Invoke($"[DesktopHelper] SetThreadDesktop('Default') failed. Error code: {err}");
                return false;
            }

            string newDesk = GetCurrentDesktopName();
            logger?.Invoke($"[DesktopHelper] Successfully switched thread desktop to: '{newDesk}'!");
            return true;
        }
        catch (Exception ex)
        {
            logger?.Invoke($"[DesktopHelper] Exception during desktop switch: {ex.Message}");
            return false;
        }
    }
}
