using System.Windows;
using TiktokStudioSandbox.App.ViewModels;
using TiktokStudioSandbox.Core.Interfaces;
using TiktokStudioSandbox.Core.Services;
using Wpf.Ui.Controls;

namespace TiktokStudioSandbox.App;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        App.Log("[MainWindow] Constructor start");

        try
        {
            InitializeComponent();
            App.Log("[MainWindow] InitializeComponent complete");

            var storage = new ConfigStorageService();
            var windowCtrl = new WindowController();
            var launcher = new ProcessLauncher(windowCtrl);
            var cdp = new CdpAutomationService();
            var orchestrator = new InstanceOrchestrator(launcher, windowCtrl, cdp);
            var proxifier = new ProxifierService();
            var profileManager = new ProfileManagerService();

            ViewModel = new MainViewModel(storage, orchestrator, proxifier, cdp, profileManager);
            DataContext = ViewModel;
            App.Log("[MainWindow] ViewModel bound complete");

            Loaded += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                ShowWindow(hwnd, 1); // SW_SHOWNORMAL
                SetForegroundWindow(hwnd);
                Activate();
                Focus();

                App.Log($"[MainWindow] UI Window displayed and active! HWND: 0x{hwnd:X}, Rect: [{Left},{Top} - {ActualWidth}x{ActualHeight}]");
            };

            IsVisibleChanged += (s, e) =>
            {
                App.Log($"[MainWindow] IsVisibleChanged: NewValue={e.NewValue}");
            };

            Closing += (s, e) =>
            {
                App.Log($"[MainWindow] Closing event fired! Cancel={e.Cancel}");
            };

            Closed += (s, e) =>
            {
                App.Log("[MainWindow] Closed event fired");
            };
        }
        catch (Exception ex)
        {
            App.Log($"[MainWindow] Exception in constructor: {ex}");
            throw;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
}