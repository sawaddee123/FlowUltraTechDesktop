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
            var telegramService = new TelegramNotificationService();
            var tikTokLive = new TikTokLiveListenerService();

            ViewModel = new MainViewModel(storage, orchestrator, proxifier, cdp, profileManager, telegramService, tikTokLive);
            DataContext = ViewModel;
            App.Log("[MainWindow] ViewModel bound complete");

            StateChanged += MainWindow_StateChanged;

            Loaded += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                int darkMode = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

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

    private bool _isHeaderMouseDown;
    private Point _headerMouseDownPos;

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _isHeaderMouseDown = false;
            ToggleMaximize();
            return;
        }

        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            if (WindowState == WindowState.Normal)
            {
                try
                {
                    DragMove();
                }
                catch { }
            }
            else if (WindowState == WindowState.Maximized)
            {
                // In maximized mode, wait for actual mouse move/drag before unmaximizing
                _isHeaderMouseDown = true;
                _headerMouseDownPos = e.GetPosition(this);
            }
        }
    }

    private void Header_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isHeaderMouseDown && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed && WindowState == WindowState.Maximized)
        {
            Point currentPos = e.GetPosition(this);
            Vector diff = currentPos - _headerMouseDownPos;

            // Only unmaximize and drag if moved past minimum drag distance
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                _isHeaderMouseDown = false;

                Point mousePos = PointToScreen(currentPos);
                double percentHorizontal = currentPos.X / ActualWidth;
                double targetWidth = RestoreBounds.Width > 0 ? RestoreBounds.Width : 1260;

                WindowState = WindowState.Normal;
                Left = mousePos.X - (targetWidth * percentHorizontal);
                Top = mousePos.Y - 10;

                try
                {
                    DragMove();
                }
                catch { }
            }
        }
    }

    private void Header_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isHeaderMouseDown = false;
    }

    private void Header_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Released)
        {
            _isHeaderMouseDown = false;
        }
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeWindow_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            RootBorder.BorderThickness = new Thickness(0);
            RootBorder.Padding = new Thickness(
                SystemParameters.WindowResizeBorderThickness.Left,
                SystemParameters.WindowResizeBorderThickness.Top,
                SystemParameters.WindowResizeBorderThickness.Right,
                SystemParameters.WindowResizeBorderThickness.Bottom);

            if (MaximizeIcon != null)
            {
                MaximizeIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.SquareMultiple16;
                MaximizeButton.ToolTip = "ย่อขนาด (Restore)";
            }
        }
        else
        {
            RootBorder.BorderThickness = new Thickness(1);
            RootBorder.Padding = new Thickness(0);

            if (MaximizeIcon != null)
            {
                MaximizeIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Maximize16;
                MaximizeButton.ToolTip = "ขยายเต็มจอ (Maximize)";
            }
        }
    }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

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