using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using FlowUltraTech.App.Helpers;

namespace FlowUltraTech.App;

public static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            App.Log("================ PROCESS STARTING ================");
            App.Log($"[Program] CommandLine: {Environment.CommandLine}");
            App.Log($"[Program] User: {Environment.UserName}, Machine: {Environment.MachineName}");

            DesktopHelper.EnsureInteractiveDesktop(App.Log);

            var app = new App();
            app.InitializeComponent();
            try
            {
                var savedSettings = new FlowUltraTech.Core.Services.ConfigStorageService().LoadSettings();
                Theme.ThemeService.Apply(!string.Equals(savedSettings.ThemeMode, "light", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception themeEx)
            {
                App.Log($"[Program] Theme init failed: {themeEx.Message}");
            }
#if DEBUG
            // โหมดพรีวิวสำหรับนักพัฒนา (ไม่รวมใน Release): เปิดหน้าหลักตรงๆ โดยไม่ต้องล็อกอิน
            if (Array.IndexOf(args, "--preview-main") >= 0)
            {
                Helpers.PreviewRunner.Run(app, args);
                return;
            }
#endif
            var loginWindow = new Views.LoginWindow();
            app.Run(loginWindow);
        }
        catch (Exception ex)
        {
            App.Log($"[FATAL] Unhandled fatal exception in Program.Main: {ex}");
            MessageBox(IntPtr.Zero, $"Failed to launch TikTok LIVE Studio Sandbox:\n\n{ex.Message}\n\nCheck app_debug.log for full details.", "Fatal Launch Error", 0x10);
        }
    }
}
