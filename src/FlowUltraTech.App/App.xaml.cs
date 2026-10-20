using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace FlowUltraTech.App;

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

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Log($"[FATAL] AppDomain Unhandled: {e.ExceptionObject}");
        };

        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            Log($"[App] ProcessExit fired! Environment.ExitCode={Environment.ExitCode}");
            try
            {
                FlowUltraTech.Core.Services.ProcessLauncher.KillAllStudioProcesses();
            }
            catch { }
        };

        DispatcherUnhandledException += (s, e) =>
        {
            Log($"[FATAL] Dispatcher Unhandled: {e.Exception}");
            MessageBox.Show($"Startup Error: {e.Exception.Message}\n\n{e.Exception.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };
    }

    private static System.Threading.Mutex? _singleInstanceMutex;
    private static bool _ownsMutex = false;

    protected override void OnStartup(StartupEventArgs e)
    {
        Log("[App] OnStartup begin");

        const string mutexName = @"Global\FlowUltraTech_SingleInstance_Mutex_2026";
        bool skipMutex = false;
#if DEBUG
        skipMutex = e.Args.Contains("--preview-main"); // พรีวิวสำหรับนักพัฒนาเท่านั้น
#endif
        try
        {
            if (skipMutex) throw new OperationCanceledException();
            _singleInstanceMutex = new System.Threading.Mutex(true, mutexName, out bool createdNew);
            _ownsMutex = createdNew;

            if (!createdNew)
            {
                Log("[App] Multi-process detected via mutex! Shutting down.");
                MessageBox.Show(
                    "โปรแกรม TikTok LIVE Studio Sandbox กำลังทำงานอยู่แล้วในระบบ\nไม่สามารถเปิดโปรแกรมซ้ำซ้อนได้",
                    "แจ้งเตือนความปลอดภัย",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                if (MainWindow != null)
                {
                    MainWindow.Visibility = Visibility.Collapsed;
                    MainWindow.Close();
                }
                Current.Shutdown();
                return;
            }
        }
        catch (OperationCanceledException)
        {
            Log("[App] Preview mode: single-instance mutex skipped");
        }
        catch (Exception ex)
        {
            Log($"[App] Mutex initialization error: {ex.Message}");
        }

        base.OnStartup(e);
        Log("[App] OnStartup end");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log($"[App] OnExit called with ApplicationExitCode={e.ApplicationExitCode}");
        try
        {
            FlowUltraTech.Core.Services.ProcessLauncher.KillAllStudioProcesses();
        }
        catch { }

        if (_singleInstanceMutex != null)
        {
            if (_ownsMutex)
            {
                try
                {
                    _singleInstanceMutex.ReleaseMutex();
                }
                catch { }
            }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }
        base.OnExit(e);
    }
}

