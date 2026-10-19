using System.Diagnostics;
using System.IO;
using System.Windows;
using FlowUltraTech.App.ViewModels;
using FlowUltraTech.Core;
using FlowUltraTech.Core.Interfaces;
using FlowUltraTech.Core.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Wpf.Ui.Controls;

namespace FlowUltraTech.App;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    private readonly Dictionary<Guid, WebView2> _overlayPreviewWebViews = new();
    private static Task<CoreWebView2Environment>? _webViewEnvTask;

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
            var overlayServer = new OverlayServerService();

            ViewModel = new MainViewModel(storage, orchestrator, proxifier, cdp, profileManager, telegramService, tikTokLive, overlayService: overlayServer);
            DataContext = ViewModel;
            App.Log("[MainWindow] ViewModel bound complete");

            ViewModel.RequestLogoutRedirect += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    _isLoggingOut = true;
                    var loginWindow = new Views.LoginWindow();
                    Application.Current.MainWindow = loginWindow;
                    loginWindow.Show();
                    Close();
                });
            };

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
                App.Log($"[MainWindow] Closing event fired! Cancel={e.Cancel}, IsLoggingOut={_isLoggingOut}");
                foreach (var wv in _overlayPreviewWebViews.Values)
                {
                    try { wv.Dispose(); } catch { }
                }
                _overlayPreviewWebViews.Clear();

                try
                {
                    ProcessLauncher.KillAllStudioProcesses();
                }
                catch { }

                try
                {
                    ViewModel?.Cleanup();
                }
                catch { }

                if (!_isLoggingOut)
                {
                    App.Log("[MainWindow] Normal close. Exiting application.");
                    Application.Current.Shutdown();
                }
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

    private static Task<CoreWebView2Environment> GetOrCreateWebViewEnvironmentAsync()
    {
        if (_webViewEnvTask == null || _webViewEnvTask.IsFaulted || _webViewEnvTask.IsCanceled)
        {
            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FlowUltraTech",
                "WebView2_OverlayPreview");
            Directory.CreateDirectory(userDataFolder);
            _webViewEnvTask = CoreWebView2Environment.CreateAsync(null, userDataFolder);
        }
        return _webViewEnvTask;
    }

    private async void OverlayPreviewHost_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Border hostBorder ||
            hostBorder.DataContext is not InstanceCardViewModel vm)
        {
            return;
        }

        try
        {
            if (vm.OverlayService != null && !vm.OverlayService.IsRunning)
            {
                int preferredPort = ViewModel?.Settings.OverlayServerPort > 0 ? ViewModel.Settings.OverlayServerPort : 19450;
                await vm.OverlayService.StartAsync(preferredPort);
                vm.NotifyOverlayEndpointChanged();
            }

            string targetUrl = $"{vm.OverlayEndpointUrl}?preview=1";

            if (hostBorder.Child is WebView2 existingWebView)
            {
                if (existingWebView.CoreWebView2 != null)
                {
                    existingWebView.CoreWebView2.Navigate(targetUrl);
                }
                return;
            }

            var webView = new WebView2
            {
                DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 13, 17, 26),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            hostBorder.Child = webView;
            _overlayPreviewWebViews[vm.Profile.Id] = webView;

            var env = await GetOrCreateWebViewEnvironmentAsync();
            if (!hostBorder.IsLoaded || !ReferenceEquals(hostBorder.Child, webView))
            {
                return;
            }

            await webView.EnsureCoreWebView2Async(env);
            if (webView.CoreWebView2 != null)
            {
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                webView.CoreWebView2.Navigate($"{vm.OverlayEndpointUrl}?preview=1");
            }
        }
        catch (Exception ex)
        {
            App.Log($"[MainWindow] WebView2 OverlayPreview init error: {ex.Message}");
            hostBorder.Child = new System.Windows.Controls.TextBlock
            {
                Text = "พรีวิวพร้อมใช้งานผ่านเบราว์เซอร์ (คลิก 'เปิดเว็บ')",
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(12)
            };
        }
    }

    private void OverlayPreviewHost_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Border hostBorder)
        {
            return;
        }

        if (hostBorder.DataContext is InstanceCardViewModel vm)
        {
            _overlayPreviewWebViews.Remove(vm.Profile.Id);
        }

        if (hostBorder.Child is WebView2 webView)
        {
            hostBorder.Child = null;
            try
            {
                webView.Dispose();
            }
            catch { }
        }
    }

    private async void ReloadOverlayPreview_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not InstanceCardViewModel vm)
        {
            return;
        }

        try
        {
            if (vm.OverlayService != null)
            {
                if (!vm.OverlayService.IsRunning)
                {
                    int preferredPort = ViewModel?.Settings.OverlayServerPort > 0 ? ViewModel.Settings.OverlayServerPort : 19450;
                    await vm.OverlayService.StartAsync(preferredPort);
                    vm.NotifyOverlayEndpointChanged();
                }
                await vm.OverlayService.UpdateConfigAsync(vm.Profile.Id, vm.Profile.Overlay);
            }

            if (_overlayPreviewWebViews.TryGetValue(vm.Profile.Id, out var webView) && webView.CoreWebView2 != null)
            {
                webView.CoreWebView2.Navigate($"{vm.OverlayEndpointUrl}?preview=1");
            }

            vm.OverlayStatusText = $"รีเฟรชพรีวิวสดแล้ว ({DateTime.Now:HH:mm:ss})";
        }
        catch (Exception ex)
        {
            App.Log($"[MainWindow] ReloadOverlayPreview error: {ex.Message}");
        }
    }

    private void OpenOverlayInBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not InstanceCardViewModel vm)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = vm.OverlayEndpointUrl,
                UseShellExecute = true
            });
            vm.OverlayStatusText = "เปิดหน้า Overlay ในเบราว์เซอร์แล้ว";
        }
        catch (Exception ex)
        {
            App.Log($"[MainWindow] OpenOverlayInBrowser error: {ex.Message}");
        }
    }

    private bool _isLoggingOut;
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