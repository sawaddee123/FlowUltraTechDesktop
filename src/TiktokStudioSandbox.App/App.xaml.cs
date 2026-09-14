using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace TiktokStudioSandbox.App;

public partial class App : Application
{
    public static string LogPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_debug.log");

    public static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\r\n");
        }
        catch { }
    }

    public App()
    {
        Log("================ APP INSTANTIATED ================");

        ShutdownMode = ShutdownMode.OnMainWindowClose;

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Log($"[FATAL] AppDomain Unhandled: {e.ExceptionObject}");
        };

        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            Log($"[App] ProcessExit fired! Environment.ExitCode={Environment.ExitCode}");
        };

        DispatcherUnhandledException += (s, e) =>
        {
            Log($"[FATAL] Dispatcher Unhandled: {e.Exception}");
            MessageBox.Show($"Startup Error: {e.Exception.Message}\n\n{e.Exception.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Log("[App] OnStartup begin");
        base.OnStartup(e);
        Log("[App] OnStartup end");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log($"[App] OnExit called with ApplicationExitCode={e.ApplicationExitCode}");
        base.OnExit(e);
    }
}

