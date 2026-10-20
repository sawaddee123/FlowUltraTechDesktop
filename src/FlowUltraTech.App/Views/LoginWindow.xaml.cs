using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using FlowUltraTech.Core;
using FlowUltraTech.Core.Interfaces;
using FlowUltraTech.Core.Models;
using FlowUltraTech.Core.Security;
using FlowUltraTech.Core.Services;

namespace FlowUltraTech.App.Views;

public partial class LoginWindow : Window
{
    private readonly IConfigStorageService _storageService;
    private readonly IAuthClientService _authService;
    private readonly AppSettings _settings;
    private bool _isLoginSuccessful;
    private bool _isCancellingVerifying;
    private string _hwid = string.Empty;

    public LoginWindow()
    {
        InitializeComponent();

        _storageService = new ConfigStorageService();
        _settings = _storageService.LoadSettings();
        _authService = new AuthClientService(defaultBaseEndpoint: AppConstants.DefaultServerEndpoint);

        try
        {
            _hwid = DeepHwidGenerator.GenerateDeepHwid();
            HwidText.Text = _hwid.Length > 20 ? $"{_hwid.Substring(0, 10)}...{_hwid.Substring(_hwid.Length - 8)}" : _hwid;
        }
        catch
        {
            _hwid = Environment.MachineName;
            HwidText.Text = _hwid;
        }

        VersionText.Text = $"v{AppConstants.CurrentVersion}";

        if (!string.IsNullOrWhiteSpace(_settings.LoggedInUsername))
        {
            UsernameBox.Text = _settings.LoggedInUsername;
        }

        Loaded += LoginWindow_Loaded;
        Closing += LoginWindow_Closing;
    }

    private async void LoginWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // If an existing token is stored, attempt auto-verification
        if (!string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            VerifyingPanel.Visibility = Visibility.Visible;
            LoginFormPanel.Visibility = Visibility.Collapsed;

            try
            {
                string endpoint = GetEffectiveEndpoint();
                var res = await _authService.GetProfileAsync(_settings.AuthToken, endpoint);

                if (!res.Success && !string.IsNullOrWhiteSpace(_settings.RefreshToken))
                {
                    var refreshRes = await _authService.RefreshTokenAsync(_settings.RefreshToken, endpoint);
                    if (refreshRes.Success && !string.IsNullOrWhiteSpace(refreshRes.AccessToken))
                    {
                        _settings.AuthToken = refreshRes.AccessToken;
                        _settings.CaptchaSolverJwtToken = refreshRes.AccessToken;
                        if (!string.IsNullOrWhiteSpace(refreshRes.RefreshToken))
                        {
                            _settings.RefreshToken = refreshRes.RefreshToken;
                        }
                        res = refreshRes;
                    }
                }

                if (_isCancellingVerifying) return;

                if (res.Success && !string.IsNullOrWhiteSpace(_settings.AuthToken))
                {
                    // Check remote expiry date
                    if (res.ExpiredAt.HasValue && DateTime.UtcNow > res.ExpiredAt.Value)
                    {
                        SwitchToLoginForm("ใบอนุญาตใช้งานของคุณหมดอายุแล้ว กรุณาติดต่อแอดมินเพื่อต่ออายุ");
                        return;
                    }

                    // Update license details
                    _settings.MaxTiktokAccounts = res.MaxTiktokAccounts > 0 ? res.MaxTiktokAccounts : 1;
                    _settings.LicensePlan = res.Plan ?? _settings.LicensePlan;
                    _settings.LicenseRole = res.Role ?? _settings.LicenseRole;
                    _settings.LicenseExpiresAt = res.ExpiredAt;
                    if (!string.IsNullOrWhiteSpace(res.Username))
                    {
                        _settings.LoggedInUsername = res.Username;
                    }
                    await _storageService.SaveSettingsAsync(_settings);

                    OpenMainWindowAndClose();
                    return;
                }
                else
                {
                    SwitchToLoginForm(string.IsNullOrWhiteSpace(res.Message) ? "กรุณาเข้าสู่ระบบเพื่อใช้งาน" : $"เซสชันหมดอายุ: {res.Message}");
                    return;
                }
            }
            catch (Exception ex)
            {
                if (!_isCancellingVerifying)
                {
                    SwitchToLoginForm($"เชื่อมต่อเซิร์ฟเวอร์ไม่สำเร็จ ({ex.Message})");
                }
                return;
            }
        }

        // No token saved, display login form directly
        VerifyingPanel.Visibility = Visibility.Collapsed;
        LoginFormPanel.Visibility = Visibility.Visible;
        if (!string.IsNullOrWhiteSpace(UsernameBox.Text))
        {
            PasswordBoxInput.Focus();
        }
        else
        {
            UsernameBox.Focus();
        }
    }

    private void SwitchToLoginForm(string? errorMessage = null)
    {
        VerifyingPanel.Visibility = Visibility.Collapsed;
        LoginFormPanel.Visibility = Visibility.Visible;
        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            ShowError(errorMessage);
        }
        if (!string.IsNullOrWhiteSpace(UsernameBox.Text))
        {
            PasswordBoxInput.Focus();
        }
        else
        {
            UsernameBox.Focus();
        }
    }

    private void CancelVerifying_Click(object sender, RoutedEventArgs e)
    {
        _isCancellingVerifying = true;
        _settings.AuthToken = string.Empty;
        SwitchToLoginForm("ยกเลิกการตรวจสอบอัตโนมัติ กรุณากรอกรหัสผ่านเพื่อเข้าสู่ระบบ");
    }

    private void LoginWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isLoginSuccessful)
        {
            App.Log("[LoginWindow] Closed by user without successful authentication. Exiting application.");
            Application.Current.Shutdown();
        }
    }

    // มีเซิร์ฟเวอร์กลางเพียงตัวเดียว ไม่ให้ผู้ใช้กรอกหรือแก้ URL อีกต่อไป
    private static string GetEffectiveEndpoint() => AppConstants.DefaultServerEndpoint;

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        await PerformLoginAsync();
    }

    private async void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await PerformLoginAsync();
        }
    }

    private async Task PerformLoginAsync()
    {
        string username = UsernameBox.Text?.Trim() ?? string.Empty;
        string password = PasswordBoxInput.Password ?? string.Empty;

        if (string.IsNullOrWhiteSpace(username))
        {
            ShowError("กรุณากรอกชื่อผู้ใช้งาน");
            UsernameBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            ShowError("กรุณากรอกรหัสผ่าน");
            PasswordBoxInput.Focus();
            return;
        }

        HideError();
        SetBusyState(true);

        try
        {
            string endpoint = GetEffectiveEndpoint();
            _settings.AuthApiEndpoint = endpoint;
            _settings.CaptchaSolverEndpoint = endpoint;

            var res = await _authService.LoginAsync(username, password, _hwid, endpoint);

            if (res.Success && !string.IsNullOrWhiteSpace(res.AccessToken))
            {
                _settings.AuthToken = res.AccessToken;
                _settings.RefreshToken = res.RefreshToken ?? string.Empty;
                _settings.CaptchaSolverJwtToken = res.AccessToken;
                _settings.MaxTiktokAccounts = res.MaxTiktokAccounts > 0 ? res.MaxTiktokAccounts : 1;
                _settings.LoggedInUsername = res.Username ?? username;
                _settings.LicenseRole = res.Role ?? "user";
                _settings.LicensePlan = res.Plan ?? "standard";
                _settings.LicenseExpiresAt = res.ExpiredAt;

                // Check version requirement
                if (res.AppVersion != null && !string.IsNullOrWhiteSpace(res.AppVersion.MinRequiredVersion))
                {
                    var appVer = typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 1, 0);
                    var currentVerStr = $"{appVer.Major}.{appVer.Minor}.{Math.Max(0, appVer.Build)}";
                    if (Version.TryParse(res.AppVersion.MinRequiredVersion.Trim().TrimStart('v', 'V'), out var minVer) && appVer < minVer)
                    {
                        MessageBox.Show(
                            $"เวอร์ชันของโปรแกรมล้าสมัย (เวอร์ชันปัจจุบัน {currentVerStr} ต่ำกว่าขั้นต่ำ {res.AppVersion.MinRequiredVersion})\nกรุณาดาวน์โหลดเวอร์ชันใหม่ได้ที่:\n{res.AppVersion.DownloadUrl}",
                            "โปรดอัปเดตโปรแกรม (Update Required)",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning
                        );
                    }
                }

                // Sync cloud settings
                if (res.Settings != null)
                {
                    if ((_settings.Rules == null || _settings.Rules.Count == 0) && res.Settings.Rules != null && res.Settings.Rules.Count > 0)
                    {
                        _settings.Rules = res.Settings.Rules;
                    }
                    if (string.IsNullOrWhiteSpace(_settings.TelegramBotToken) && !string.IsNullOrWhiteSpace(res.Settings.TelegramBotToken))
                    {
                        _settings.TelegramBotToken = res.Settings.TelegramBotToken;
                    }
                    if (string.IsNullOrWhiteSpace(_settings.TelegramChatId) && !string.IsNullOrWhiteSpace(res.Settings.TelegramChatId))
                    {
                        _settings.TelegramChatId = res.Settings.TelegramChatId;
                    }
                }

                await _storageService.SaveSettingsAsync(_settings);
                OpenMainWindowAndClose();
            }
            else
            {
                ShowError(string.IsNullOrWhiteSpace(res.Message) ? "ชื่อผู้ใช้หรือรหัสผ่านไม่ถูกต้อง" : res.Message);
                PasswordBoxInput.SelectAll();
                PasswordBoxInput.Focus();
            }
        }
        catch (Exception ex)
        {
            ShowError($"เกิดข้อผิดพลาดในการเชื่อมต่อ: {ex.Message}");
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private void OpenMainWindowAndClose()
    {
        _isLoginSuccessful = true;
        App.Log("[LoginWindow] Authentication successful! Launching MainWindow...");

        var mainWindow = new MainWindow();
        Application.Current.MainWindow = mainWindow;
        mainWindow.Show();

        Close();
    }

    private void SetBusyState(bool isBusy)
    {
        LoginButton.IsEnabled = !isBusy;
        UsernameBox.IsEnabled = !isBusy;
        PasswordBoxInput.IsEnabled = !isBusy;
        LoginSpinner.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        LoginButtonText.Text = isBusy ? "กำลังตรวจสอบ..." : "เข้าสู่ระบบ";
    }

    private void ShowError(string message)
    {
        ErrorMessageText.Text = message;
        ErrorBanner.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorBanner.Visibility = Visibility.Collapsed;
    }

    private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
