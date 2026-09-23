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
            var mainWindow = new MainWindow();
            app.Run(mainWindow);
        }
        catch (Exception ex)
        {
            App.Log($"[FATAL] Unhandled fatal exception in Program.Main: {ex}");
            MessageBox(IntPtr.Zero, $"Failed to launch TikTok LIVE Studio Sandbox:\n\n{ex.Message}\n\nCheck app_debug.log for full details.", "Fatal Launch Error", 0x10);
        }
    }
}
