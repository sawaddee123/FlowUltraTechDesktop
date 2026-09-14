using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using TiktokStudioSandbox.App.Helpers;

namespace TiktokStudioSandbox.App;

public static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    public static void Main(string[] args)
    {
        try
        {
            App.Log("================ PROCESS STARTING ================");
            App.Log($"[Program] CommandLine: {Environment.CommandLine}");
            App.Log($"[Program] User: {Environment.UserName}, Machine: {Environment.MachineName}");

            // Run WPF on a dedicated STA thread.
            // A brand new thread has no OLE apartment or window hooks yet,
            // allowing SetThreadDesktop('Default') to succeed cleanly without error 170.
            Exception? threadException = null;

            var uiThread = new Thread(() =>
            {
                try
                {
                    DesktopHelper.EnsureInteractiveDesktop(App.Log);

                    var app = new App();
                    app.InitializeComponent();
                    app.Run();
                }
                catch (Exception ex)
                {
                    threadException = ex;
                }
            });

            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.Start();
            uiThread.Join();

            if (threadException != null)
            {
                throw threadException;
            }
        }
        catch (Exception ex)
        {
            App.Log($"[FATAL] Unhandled fatal exception in Program.Main: {ex}");
            MessageBox(IntPtr.Zero, $"Failed to launch TikTok LIVE Studio Sandbox:\n\n{ex.Message}\n\nCheck app_debug.log for full details.", "Fatal Launch Error", 0x10);
        }
    }
}
