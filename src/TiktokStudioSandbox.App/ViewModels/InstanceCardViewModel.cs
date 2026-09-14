using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TiktokStudioSandbox.Core.Interfaces;
using TiktokStudioSandbox.Core.Models;
using TiktokStudioSandbox.Core.Services;

namespace TiktokStudioSandbox.App.ViewModels;

public partial class InstanceCardViewModel : ObservableObject
{
    private readonly IInstanceOrchestrator _orchestrator;
    private readonly AppSettings _settings;
    private readonly ICdpAutomationService _cdpService;
    private readonly IProfileManagerService _profileManager;
    private IDisposable? _profileWatcherSub;
    private System.Windows.Threading.DispatcherTimer? _storageRefreshTimer;

    public StudioInstanceProfile Profile { get; }

    [ObservableProperty]
    private string? _loggedInUserId;

    [ObservableProperty]
    private string? _loggedInNickname;

    [ObservableProperty]
    private string? _loggedInDisplayId;

    [ObservableProperty]
    private string? _loggedInAvatarUrl;

    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private bool _isUltraLightLoading;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _profileFolderName;

    public string SandboxBoxName
    {
        get => ProfileFolderName;
        set
        {
            ProfileFolderName = value;
            Profile.ProfileFolderName = value;
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    private int _cdpPort;

    [ObservableProperty]
    private InstanceStatus _status;

    [ObservableProperty]
    private string _statusMessage;

    [ObservableProperty]
    private bool _isWindowHidden;

    [ObservableProperty]
    private InstanceLaunchMode _launchMode;

    [ObservableProperty]
    private string _testChatMessage = "สวัสดีครับจากระบบทดสอบ!";

    [ObservableProperty]
    private string _chatStatusText = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    // Detail Panel State
    [ObservableProperty]
    private bool _isDetailOpen;

    // Script Console
    [ObservableProperty]
    private string _scriptInput = "window.location.href";

    [ObservableProperty]
    private string _consoleOutput = "CDP Script Console พร้อมใช้งาน\nพิมพ์คำสั่ง JavaScript แล้วกด [▶ รันสคริปต์] เพื่อทดสอบกับหน้าต่างนี้...\n";

    [ObservableProperty]
    private string? _lastExportedHtmlPath;

    // ==========================================
    // 🧩 Captcha Auto-Detection & Copy Properties
    // ==========================================
    private System.Windows.Threading.DispatcherTimer? _captchaDetectTimer;
    private string? _lastReportedCaptchaHtml;

    [ObservableProperty]
    private bool _isCaptchaAutoDetect = true;

    [ObservableProperty]
    private bool _autoCopyToClipboardOnDetect = false;


    [ObservableProperty]
    private bool _isCaptchaAlertVisible;

    public Visibility CaptchaAlertVisibility => IsCaptchaAlertVisible ? Visibility.Visible : Visibility.Collapsed;

    partial void OnIsCaptchaAlertVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(CaptchaAlertVisibility));
    }

    [ObservableProperty]
    private string _captchaAlertTitle = string.Empty;

    [ObservableProperty]
    private string _captchaAlertMessage = string.Empty;

    [ObservableProperty]
    private string _detectedCaptchaHtml = string.Empty;

    [ObservableProperty]
    private int _detectedCaptchaCount;

    [ObservableProperty]
    private string _lastCaptchaDetectedTime = string.Empty;

    [ObservableProperty]
    private ObservableCollection<CaptchaElementItem> _detectedCaptchaElements = new();

    [ObservableProperty]
    private CaptchaElementItem? _selectedCaptchaElement;

    [ObservableProperty]
    private int _selectedDetailTabIndex;

    // Session & Tokens
    [ObservableProperty]
    private string _sessionIdToken = "-";

    [ObservableProperty]
    private string _csrfToken = "-";

    [ObservableProperty]
    private string _sessionFetchStatus = string.Empty;

    // ==========================================
    // 🌐 Real-time Network Monitor Properties
    // ==========================================
    public ObservableCollection<NetworkTrafficItem> NetworkTraffic { get; } = new();
    public ICollectionView FilteredNetworkTraffic { get; }

    [ObservableProperty]
    private bool _isNetworkHooking;

    public string NetworkHookButtonText => IsNetworkHooking ? "🔴 หยุดดักจับ API ชั่วคราว" : "▶ เริ่มดักจับ Network API";
    public string NetworkHookBadgeText => IsNetworkHooking ? "🟢 Auto Monitor (เปิดอยู่)" : "⚪ พักการดักจับ";

    [ObservableProperty]
    private NetworkTrafficItem? _selectedTraffic;

    [ObservableProperty]
    private string _networkSearchText = string.Empty;

    [ObservableProperty]
    private string _networkUrlFilter = string.Empty;

    [ObservableProperty]
    private string _networkMethodFilter = "ALL";

    [ObservableProperty]
    private string _networkStatusFilter = "ALL";

    public string[] MethodOptions => ["ALL", "GET", "POST", "OPTIONS", "PUT", "DELETE"];
    public string[] StatusOptions => ["ALL", "2xx (สำเร็จ)", "4xx/5xx (Error)", "Pending (รอดำเนินการ)"];

    // ==========================================
    // 🍪 Real-time Cookies & Storage Properties
    // ==========================================
    public ObservableCollection<CookieItem> Cookies { get; } = new();
    public ICollectionView FilteredCookies { get; }

    public ObservableCollection<StorageEntry> LocalStorageItems { get; } = new();
    public ICollectionView FilteredLocalStorage { get; }

    public ObservableCollection<StorageEntry> SessionStorageItems { get; } = new();
    public ICollectionView FilteredSessionStorage { get; }

    [ObservableProperty]
    private string _cookieNameFilter = string.Empty;

    [ObservableProperty]
    private string _cookieDomainFilter = string.Empty;

    [ObservableProperty]
    private string _cookieValueFilter = string.Empty;

    [ObservableProperty]
    private string _storageKeyFilter = string.Empty;

    [ObservableProperty]
    private string _storageValueFilter = string.Empty;

    [ObservableProperty]
    private bool _isStorageAutoRefresh = true;

    [ObservableProperty]
    private string _storageStatus = "พร้อมอัปเดตเรียลไทม์อัตโนมัติ";

    // ==========================================
    // 📋 Real-time Debug Logs Properties
    // ==========================================
    public IInstanceLogger Logger { get; }
    public ObservableCollection<InstanceLogEntry> Logs { get; } = new();
    public ICollectionView FilteredLogs { get; }

    [ObservableProperty]
    private string _logLevelFilter = "ALL";

    [ObservableProperty]
    private string _logCategoryFilter = "ALL";

    [ObservableProperty]
    private string _logSearchText = string.Empty;

    [ObservableProperty]
    private InstanceLogEntry? _selectedLog;

    public string[] LogLevelOptions => ["ALL", "DEBUG", "INFO", "WARN", "ERROR"];
    public string[] LogCategoryOptions => ["ALL", "Process", "CDP", "Window", "Profile", "Network", "Storage", "Chat", "System", "General"];

    public string LogFilePath => Logger.LogFilePath;
    public string LogCountSummary => $"ทั้งหมด {Logs.Count} รายการ (Error: {Logs.Count(x => x.Level == InstanceLogLevel.Error)}, Warn: {Logs.Count(x => x.Level == InstanceLogLevel.Warn)})";
    public string SelectedLogFullText => SelectedLog != null ? SelectedLog.FullLine : string.Empty;
    public Visibility SelectedLogDetailsVisibility => SelectedLog != null ? Visibility.Visible : Visibility.Collapsed;

    // ==========================================
    // Quick Status & Visual Computed Properties
    // ==========================================
    public bool IsRunning => Status == InstanceStatus.Running;
    public bool IsNotRunning => Status != InstanceStatus.Running;
    public bool IsStopped => Status == InstanceStatus.Stopped;
    public bool IsStarting => Status == InstanceStatus.Starting;

    public Visibility DetailVisibility => IsDetailOpen ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RunningVisibility => IsRunning ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NotRunningVisibility => IsNotRunning ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ErrorVisibility => HasError ? Visibility.Visible : Visibility.Collapsed;

    // Profile & Login UI Visual Properties
    public string DisplayNameAndHandle => IsLoggedIn ? $"{LoggedInNickname} (@{LoggedInDisplayId})" : "ยังไม่เข้าสู่ระบบ";
    public string LoginBadgeText => IsLoggedIn ? $"🟢 @{LoggedInDisplayId}" : "⚪ ยังไม่ล็อกอิน";
    public Visibility AvatarVisibility => !string.IsNullOrEmpty(LoggedInAvatarUrl) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FallbackAvatarVisibility => string.IsNullOrEmpty(LoggedInAvatarUrl) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LoggedInVisibility => IsLoggedIn ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NotLoggedInVisibility => !IsLoggedIn ? Visibility.Visible : Visibility.Collapsed;

    public string DetailButtonText => IsDetailOpen ? "▲ ปิด Detail" : "⚙️ Detail & สคริปต์";

    public string StatusDisplay => Status switch
    {
        InstanceStatus.Running => "🟢 กำลังทำงาน",
        InstanceStatus.Starting => "⏳ กำลังเริ่ม...",
        InstanceStatus.Error => "🔴 ข้อผิดพลาด",
        _ => "⚪ หยุดทำงาน"
    };

    public string WindowVisibilityDisplay => IsRunning 
        ? (IsWindowHidden ? "🙈 ซ่อนจอ (SW_HIDE)" : "👁️ แสดงปกติ") 
        : "⚪ ยังไม่เปิด";

    public string ModeBadgeText => LaunchMode switch
    {
        InstanceLaunchMode.DefaultProfile => "🏠 บัญชีหลัก (Default Profile)",
        _ => "📁 โหมด: Direct Profile (แยกโฟลเดอร์)"
    };

    public InstanceCardViewModel(
        StudioInstanceProfile profile, 
        AppSettings settings,
        IInstanceOrchestrator orchestrator,
        ICdpAutomationService? cdpService = null,
        IProfileManagerService? profileManager = null,
        IInstanceLogManager? logManager = null)
    {
        Profile = profile;
        _settings = settings;
        _orchestrator = orchestrator;
        _cdpService = cdpService ?? new CdpAutomationService();
        _profileManager = profileManager ?? new ProfileManagerService();
        var logMgr = logManager ?? InstanceLogManager.Shared;
        Logger = logMgr.GetLogger(profile);

        // Setup CollectionView filtering for Logs
        FilteredLogs = CollectionViewSource.GetDefaultView(Logs);
        FilteredLogs.Filter = FilterLogEntry;

        foreach (var entry in Logger.GetRecentLogs())
        {
            Logs.Add(entry);
        }

        Logger.LogAdded += (entry) =>
        {
            void AddEntry()
            {
                if (Logs.Count > 1500) Logs.RemoveAt(0);
                Logs.Add(entry);
                OnPropertyChanged(nameof(LogCountSummary));
            }

            if (Application.Current != null)
            {
                Application.Current.Dispatcher.Invoke(AddEntry);
            }
            else
            {
                AddEntry();
            }
        };

        // Setup CollectionView filtering for Network
        FilteredNetworkTraffic = CollectionViewSource.GetDefaultView(NetworkTraffic);
        FilteredNetworkTraffic.Filter = FilterNetworkTrafficItem;

        // Setup CollectionView filtering for Cookies & Storage
        FilteredCookies = CollectionViewSource.GetDefaultView(Cookies);
        FilteredCookies.Filter = FilterCookieItem;

        FilteredLocalStorage = CollectionViewSource.GetDefaultView(LocalStorageItems);
        FilteredLocalStorage.Filter = FilterLocalStorageItem;

        FilteredSessionStorage = CollectionViewSource.GetDefaultView(SessionStorageItems);
        FilteredSessionStorage.Filter = FilterSessionStorageItem;

        _name = profile.Name;
        _profileFolderName = profile.ProfileFolderName;
        _cdpPort = profile.CdpPort;
        _status = profile.Status;
        _statusMessage = profile.StatusMessage;
        _isWindowHidden = profile.IsWindowHidden;
        _launchMode = profile.LaunchMode;

        _loggedInUserId = profile.LoggedInUserId;
        _loggedInNickname = profile.LoggedInNickname;
        _loggedInDisplayId = profile.LoggedInDisplayId;
        _loggedInAvatarUrl = profile.LoggedInAvatarUrl;
        _isLoggedIn = profile.IsLoggedIn;

        // Auto load existing profile info from disk if present
        RefreshProfile();

        // Initialize storage timer if auto-refresh is active
        EnsureStorageTimer();
        if (IsStorageAutoRefresh)
        {
            _storageRefreshTimer?.Start();
        }

        // Auto monitor if instance is already running on app start
        _ = Task.Run(async () =>
        {
            try
            {
                int? portPid = ProcessLauncher.GetPidListeningOnPort(Profile.CdpPort);
                if (portPid.HasValue)
                {
                    if (Application.Current != null)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            Profile.Status = InstanceStatus.Running;
                            Profile.ProcessId = portPid.Value;
                            Profile.StatusMessage = $"Running on port {Profile.CdpPort}";
                            SyncFromProfile();
                            await StartAutoMonitoringAsync();
                        });
                    }
                }
            }
            catch { }
        });
    }

    // Filter & State Change Listeners
    partial void OnIsLoggedInChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayNameAndHandle));
        OnPropertyChanged(nameof(LoginBadgeText));
        OnPropertyChanged(nameof(AvatarVisibility));
        OnPropertyChanged(nameof(FallbackAvatarVisibility));
        OnPropertyChanged(nameof(LoggedInVisibility));
        OnPropertyChanged(nameof(NotLoggedInVisibility));
    }

    partial void OnIsNetworkHookingChanged(bool value)
    {
        OnPropertyChanged(nameof(NetworkHookButtonText));
        OnPropertyChanged(nameof(NetworkHookBadgeText));
    }

    partial void OnLogLevelFilterChanged(string value) => FilteredLogs?.Refresh();
    partial void OnLogCategoryFilterChanged(string value) => FilteredLogs?.Refresh();
    partial void OnLogSearchTextChanged(string value) => FilteredLogs?.Refresh();
    partial void OnSelectedLogChanged(InstanceLogEntry? value)
    {
        OnPropertyChanged(nameof(SelectedLogFullText));
        OnPropertyChanged(nameof(SelectedLogDetailsVisibility));
    }

    partial void OnNetworkSearchTextChanged(string value) => FilteredNetworkTraffic?.Refresh();
    partial void OnNetworkUrlFilterChanged(string value) => FilteredNetworkTraffic?.Refresh();
    partial void OnNetworkMethodFilterChanged(string value) => FilteredNetworkTraffic?.Refresh();
    partial void OnNetworkStatusFilterChanged(string value) => FilteredNetworkTraffic?.Refresh();

    partial void OnCookieNameFilterChanged(string value) => FilteredCookies?.Refresh();
    partial void OnCookieDomainFilterChanged(string value) => FilteredCookies?.Refresh();
    partial void OnCookieValueFilterChanged(string value) => FilteredCookies?.Refresh();

    partial void OnStorageKeyFilterChanged(string value)
    {
        FilteredLocalStorage?.Refresh();
        FilteredSessionStorage?.Refresh();
    }

    partial void OnStorageValueFilterChanged(string value)
    {
        FilteredLocalStorage?.Refresh();
        FilteredSessionStorage?.Refresh();
    }

    private void EnsureStorageTimer()
    {
        if (_storageRefreshTimer == null)
        {
            try
            {
                _storageRefreshTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3)
                };
                _storageRefreshTimer.Tick += async (_, _) =>
                {
                    if (IsRunning)
                    {
                        await FetchCookiesAndStorageAsync();
                    }
                };
            }
            catch { }
        }
    }

    partial void OnIsStorageAutoRefreshChanged(bool value)
    {
        EnsureStorageTimer();
        if (value)
        {
            _storageRefreshTimer?.Start();
            if (IsRunning)
            {
                _ = FetchCookiesAndStorageAsync();
            }
        }
        else
        {
            _storageRefreshTimer?.Stop();
        }
    }

    private void EnsureCaptchaTimer()
    {
        if (_captchaDetectTimer == null)
        {
            try
            {
                _captchaDetectTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(5)
                };
                _captchaDetectTimer.Tick += async (_, _) =>
                {
                    if (IsRunning && IsCaptchaAutoDetect)
                    {
                        await CheckCaptchaInternalAsync(isAuto: true);
                    }
                };
            }
            catch { }
        }
    }

    partial void OnIsCaptchaAutoDetectChanged(bool value)
    {
        EnsureCaptchaTimer();
        if (value)
        {
            _captchaDetectTimer?.Start();
            if (IsRunning)
            {
                _ = CheckCaptchaInternalAsync(isAuto: true);
            }
        }
        else
        {
            _captchaDetectTimer?.Stop();
        }
    }

    private bool FilterNetworkTrafficItem(object obj)
    {
        if (obj is not NetworkTrafficItem item) return false;

        if (!string.IsNullOrEmpty(NetworkMethodFilter) && NetworkMethodFilter != "ALL")
        {
            if (!string.Equals(item.Method, NetworkMethodFilter, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrEmpty(NetworkStatusFilter) && NetworkStatusFilter != "ALL")
        {
            if (NetworkStatusFilter.StartsWith("2xx") && (item.StatusCode < 200 || item.StatusCode >= 300)) return false;
            if (NetworkStatusFilter.StartsWith("4xx") && item.StatusCode < 400) return false;
            if (NetworkStatusFilter.StartsWith("Pending") && !item.StatusDisplay.Contains("Pending", StringComparison.OrdinalIgnoreCase)) return false;
        }

        if (!string.IsNullOrWhiteSpace(NetworkUrlFilter))
        {
            if (item.Url == null || !item.Url.Contains(NetworkUrlFilter, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrWhiteSpace(NetworkSearchText))
        {
            bool match = (item.Url != null && item.Url.Contains(NetworkSearchText, StringComparison.OrdinalIgnoreCase)) ||
                         (item.Method != null && item.Method.Contains(NetworkSearchText, StringComparison.OrdinalIgnoreCase)) ||
                         (item.StatusDisplay != null && item.StatusDisplay.Contains(NetworkSearchText, StringComparison.OrdinalIgnoreCase)) ||
                         (item.ResourceType != null && item.ResourceType.Contains(NetworkSearchText, StringComparison.OrdinalIgnoreCase));
            if (!match) return false;
        }

        return true;
    }

    private bool FilterCookieItem(object obj)
    {
        if (obj is not CookieItem cookie) return false;

        if (!string.IsNullOrWhiteSpace(CookieNameFilter) && (cookie.Name == null || !cookie.Name.Contains(CookieNameFilter, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (!string.IsNullOrWhiteSpace(CookieDomainFilter) && (cookie.Domain == null || !cookie.Domain.Contains(CookieDomainFilter, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (!string.IsNullOrWhiteSpace(CookieValueFilter) && (cookie.Value == null || !cookie.Value.Contains(CookieValueFilter, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }

    private bool FilterLocalStorageItem(object obj)
    {
        if (obj is not StorageEntry entry) return false;

        if (!string.IsNullOrWhiteSpace(StorageKeyFilter) && (entry.Key == null || !entry.Key.Contains(StorageKeyFilter, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (!string.IsNullOrWhiteSpace(StorageValueFilter) && (entry.Value == null || !entry.Value.Contains(StorageValueFilter, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }

    private bool FilterSessionStorageItem(object obj)
    {
        if (obj is not StorageEntry entry) return false;

        if (!string.IsNullOrWhiteSpace(StorageKeyFilter) && (entry.Key == null || !entry.Key.Contains(StorageKeyFilter, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (!string.IsNullOrWhiteSpace(StorageValueFilter) && (entry.Value == null || !entry.Value.Contains(StorageValueFilter, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }

    private bool FilterLogEntry(object obj)
    {
        if (obj is not InstanceLogEntry entry) return false;

        if (!string.IsNullOrEmpty(LogLevelFilter) && LogLevelFilter != "ALL")
        {
            if (!string.Equals(entry.LevelDisplay, LogLevelFilter, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrEmpty(LogCategoryFilter) && LogCategoryFilter != "ALL")
        {
            if (!string.Equals(entry.Category, LogCategoryFilter, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrWhiteSpace(LogSearchText))
        {
            bool match = (entry.Message != null && entry.Message.Contains(LogSearchText, StringComparison.OrdinalIgnoreCase)) ||
                         (entry.Category != null && entry.Category.Contains(LogSearchText, StringComparison.OrdinalIgnoreCase)) ||
                         (entry.Details != null && entry.Details.Contains(LogSearchText, StringComparison.OrdinalIgnoreCase));
            if (!match) return false;
        }

        return true;
    }

    [RelayCommand]
    public void ClearLogs()
    {
        Logs.Clear();
        Logger.Clear();
        SelectedLog = null;
        OnPropertyChanged(nameof(LogCountSummary));
    }

    [RelayCommand]
    public void CopyAllLogs()
    {
        var sb = new StringBuilder();
        foreach (InstanceLogEntry item in FilteredLogs)
        {
            sb.AppendLine(item.FullLine);
        }
        if (sb.Length > 0)
        {
            Clipboard.SetText(sb.ToString());
        }
    }

    [RelayCommand]
    public void CopySelectedLog()
    {
        if (SelectedLog != null)
        {
            Clipboard.SetText(SelectedLog.FullLine);
        }
    }

    [RelayCommand]
    public void OpenLogFile()
    {
        try
        {
            if (!System.IO.File.Exists(Logger.LogFilePath))
            {
                System.IO.File.WriteAllText(Logger.LogFilePath, $"=== Log file for {Name} ===\n");
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Logger.LogFilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error("System", $"Cannot open log file: {ex.Message}", ex);
        }
    }

    [RelayCommand]
    public void OpenLogsFolder()
    {
        try
        {
            string dir = System.IO.Path.GetDirectoryName(Logger.LogFilePath) ?? string.Empty;
            if (System.IO.Directory.Exists(dir))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Error("System", $"Cannot open logs folder: {ex.Message}", ex);
        }
    }

    [RelayCommand]
    public void ResetLogFilters()
    {
        LogLevelFilter = "ALL";
        LogCategoryFilter = "ALL";
        LogSearchText = string.Empty;
        FilteredLogs?.Refresh();
    }

    [RelayCommand]
    public void ToggleDetail()
    {
        IsDetailOpen = !IsDetailOpen;
        OnPropertyChanged(nameof(DetailButtonText));
        OnPropertyChanged(nameof(DetailVisibility));
    }

    [RelayCommand]
    public void SwitchMode()
    {
        LaunchMode = LaunchMode switch
        {
            InstanceLaunchMode.DirectProfile => InstanceLaunchMode.DefaultProfile,
            _ => InstanceLaunchMode.DirectProfile
        };

        Profile.LaunchMode = LaunchMode;
        OnPropertyChanged(nameof(ModeBadgeText));
        StatusMessage = $"สลับเป็น {ModeBadgeText}";
    }

    [RelayCommand]
    public async Task StartAsync()
    {
        Logger.Info("Instance", $"Start command triggered for '{Name}' (Port: {CdpPort}, Mode: {LaunchMode})");
        ProcessLauncher.BindProtocolToProfile(Profile, _settings);

        if (!IsLoggedIn)
        {
            Logger.Info("Instance", $"Instance '{Name}' is not logged in yet. Prompting interactive login window...");
            await OpenStudioForLoginAsync();
            return;
        }

        StatusMessage = "กำลังเริ่มโปรแกรม...";
        SyncFromProfile();
        StartLoginWatcher(autoClose: false);
        bool ok = await _orchestrator.StartInstanceAsync(Profile, _settings);
        SyncFromProfile();
        if (ok)
        {
            _orchestrator.ShowWindow(Profile);
            SyncFromProfile();
            AppendConsole($"[{DateTime.Now:HH:mm:ss}] 🚀 หน้าต่างเริ่มทำงานแล้ว (CDP Port: {CdpPort}) - เริ่มระบบ Auto Monitor ทันที");
            _ = StartAutoMonitoringAsync();
        }
        else
        {
            AppendConsole($"[{DateTime.Now:HH:mm:ss}] ❌ เริ่มหน้าต่างไม่สำเร็จ: {StatusMessage}");
        }
    }

    [RelayCommand]
    public async Task StopAsync()
    {
        Logger.Info("Instance", $"Stop command triggered for '{Name}'");
        StatusMessage = "กำลังปิดโปรแกรม...";
        _profileWatcherSub?.Dispose();
        _profileWatcherSub = null;
        if (IsNetworkHooking)
        {
            await StopNetworkHookInternalAsync();
        }
        _storageRefreshTimer?.Stop();
        _captchaDetectTimer?.Stop();
        IsCaptchaAlertVisible = false;
        _lastReportedCaptchaHtml = null;
        SyncFromProfile();
        await _orchestrator.StopInstanceAsync(Profile);
        SyncFromProfile();
        AppendConsole($"[{DateTime.Now:HH:mm:ss}] ⏹️ หน้าต่างหยุดทำงานแล้ว");
    }

    [RelayCommand]
    public void ToggleVisibility()
    {
        Logger.Info("Window", $"ToggleVisibility triggered for '{Name}'");
        _orchestrator.ToggleWindowVisibility(Profile);
        SyncFromProfile();
    }

    [RelayCommand]
    public async Task SendTestChatAsync()
    {
        if (Status != InstanceStatus.Running)
        {
            Logger.Warn("Chat", $"Cannot send chat: '{Name}' is not running.");
            ChatStatusText = "หน้าต่างยังไม่ได้เปิดทำงาน (กรุณากด Start ก่อน)";
            return;
        }

        if (string.IsNullOrWhiteSpace(TestChatMessage)) return;

        Logger.Info("Chat", $"Sending test chat: \"{TestChatMessage}\"");
        ChatStatusText = "กำลังส่งข้อความ...";
        bool ok = await _orchestrator.SendChatAsync(Profile, TestChatMessage);
        ChatStatusText = ok ? "ส่งข้อความสำเร็จ!" : "ส่งไม่สำเร็จ (ตรวจพอร์ต CDP หรือห้องไลฟ์)";
    }

    // ==========================================
    // Script Execution Commands
    // ==========================================
    [RelayCommand]
    public async Task ExecuteScriptAsync()
    {
        if (Status != InstanceStatus.Running)
        {
            Logger.Warn("Script", $"Cannot execute script: '{Name}' is not running.");
            AppendConsole($"[{DateTime.Now:HH:mm:ss}] ⚠️ ไม่สามารถรันได้: หน้าต่างนี้ยังไม่ได้เปิดทำงาน (สถานะ: {StatusDisplay}) กรุณากดปุ่ม Start ก่อน\n");
            return;
        }

        if (string.IsNullOrWhiteSpace(ScriptInput)) return;

        string time = DateTime.Now.ToString("HH:mm:ss");
        AppendConsole($"[{time}] > {ScriptInput}");
        Logger.Debug("Script", $"Execute JavaScript: {ScriptInput}");

        try
        {
            string result = await _cdpService.ExecuteScriptAsync(CdpPort, ScriptInput);
            AppendConsole($"[{time}] < {result}\n");
            Logger.Debug("Script", $"Script result: {result}");
        }
        catch (Exception ex)
        {
            AppendConsole($"[{time}] ❌ Error: {ex.Message}\n");
            Logger.Error("Script", $"JavaScript execution failed: {ex.Message}", ex);
        }
    }

    [RelayCommand]
    public void SetScriptSnippet(string? snippet)
    {
        if (!string.IsNullOrWhiteSpace(snippet))
        {
            ScriptInput = snippet;
        }
    }

    [RelayCommand]
    public void ClearConsole()
    {
        ConsoleOutput = string.Empty;
    }

    [RelayCommand]
    public void CopyConsole()
    {
        if (!string.IsNullOrEmpty(ConsoleOutput))
        {
            Clipboard.SetText(ConsoleOutput);
        }
    }

    [RelayCommand]
    public async Task<string?> ExportFullHtmlAsync(string? targetFilePath = null)
    {
        if (Status != InstanceStatus.Running)
        {
            Logger.Warn("CDP", $"Cannot export HTML: '{Name}' is not running.");
            AppendConsole($"[{DateTime.Now:HH:mm:ss}] ⚠️ ไม่สามารถ Export ได้: หน้าต่างนี้ยังไม่ได้เปิดทำงาน (สถานะ: {StatusDisplay}) กรุณากดปุ่ม [Start] ก่อน\n");
            StatusMessage = "กรุณาเปิดสตูดิโอก่อน Export HTML";
            return null;
        }

        string time = DateTime.Now.ToString("HH:mm:ss");
        AppendConsole($"[{time}] ⏳ กำลังดึง Full HTML จากหน้าต่างผ่าน CDP (พอร์ต {CdpPort})...");
        StatusMessage = "กำลังดึง Full HTML...";
        Logger.Info("CDP", $"Fetching full HTML for '{Name}' on port {CdpPort}...");

        try
        {
            var result = await _cdpService.GetFullHtmlAsync(CdpPort);
            if (result == null || string.IsNullOrWhiteSpace(result.Html))
            {
                AppendConsole($"[{DateTime.Now:HH:mm:ss}] ❌ ดึง Full HTML ไม่สำเร็จ (ไม่พบหน้าเว็บที่เปิดอยู่ หรือพอร์ต CDP ไม่ตอบสนอง)\n");
                StatusMessage = "ดึง Full HTML ไม่สำเร็จ";
                Logger.Warn("CDP", $"Failed to retrieve HTML for '{Name}' on port {CdpPort}");
                return null;
            }

            // Prompt user with SaveFileDialog if no path provided
            if (string.IsNullOrWhiteSpace(targetFilePath))
            {
                string defaultName = $"TikTokStudio_{ProfileFolderName}_{DateTime.Now:yyyyMMdd_HHmmss}.html";
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = $"Export Full HTML - {Name} (Port: {CdpPort})",
                    Filter = "HTML Files (*.html;*.htm)|*.html;*.htm|All Files (*.*)|*.*",
                    FileName = defaultName,
                    DefaultExt = "html"
                };

                bool? dialogResult = dialog.ShowDialog();
                if (dialogResult != true)
                {
                    AppendConsole($"[{DateTime.Now:HH:mm:ss}] ℹ️ ยกเลิกการ Export Full HTML (ไม่ได้เลือกไฟล์ปลายทาง)\n");
                    StatusMessage = "ยกเลิกการบันทึกไฟล์";
                    return null;
                }

                targetFilePath = dialog.FileName;
            }

            string dir = System.IO.Path.GetDirectoryName(targetFilePath) ?? string.Empty;
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }

            string headerComment = $"<!-- [TikTok LIVE Studio Sandbox] Exported: {result.FetchedAt:yyyy-MM-dd HH:mm:ss} | Profile: {Name} ({ProfileFolderName}) | Port: {CdpPort} | URL: {result.Url} | Title: {result.Title} -->\n";
            string finalHtml;
            if (result.Html.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase))
            {
                int idx = result.Html.IndexOf('>');
                if (idx >= 0)
                {
                    finalHtml = result.Html.Substring(0, idx + 1) + "\n" + headerComment + result.Html.Substring(idx + 1);
                }
                else
                {
                    finalHtml = headerComment + result.Html;
                }
            }
            else
            {
                finalHtml = headerComment + result.Html;
            }

            await System.IO.File.WriteAllTextAsync(targetFilePath, finalHtml, Encoding.UTF8);

            var fileInfo = new System.IO.FileInfo(targetFilePath);
            LastExportedHtmlPath = targetFilePath;
            StatusMessage = $"✅ Export Full HTML สำเร็จ ({fileInfo.Name})";

            AppendConsole($"[{DateTime.Now:HH:mm:ss}] ✅ Export Full HTML สำเร็จเรียบร้อย!");
            AppendConsole($"   📁 ตำแหน่ง: {targetFilePath}");
            AppendConsole($"   📊 ขนาด: {fileInfo.Length:N0} bytes | หน้าเว็บ: {result.Title} ({result.Url})");
            AppendConsole($"   💡 สามารถดับเบิลคลิกเปิดไฟล์ด้วย Browser หรือ Editor ได้ทันที\n");

            Logger.Info("CDP", $"Successfully exported full HTML to '{targetFilePath}' ({fileInfo.Length} bytes)");
            return targetFilePath;
        }
        catch (Exception ex)
        {
            AppendConsole($"[{DateTime.Now:HH:mm:ss}] ❌ เกิดข้อผิดพลาดในการ Export HTML: {ex.Message}\n");
            StatusMessage = $"Export HTML ไม่สำเร็จ: {ex.Message}";
            Logger.Error("CDP", $"Exception exporting full HTML: {ex.Message}", ex);
            return null;
        }
    }

    [RelayCommand]
    public void OpenLastExportedHtml()
    {
        if (!string.IsNullOrEmpty(LastExportedHtmlPath) && System.IO.File.Exists(LastExportedHtmlPath))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = LastExportedHtmlPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Logger.Error("System", $"Cannot open exported HTML file: {ex.Message}", ex);
            }
        }
    }

    // ==========================================
    // 🧩 Captcha Detection & Copy Commands
    // ==========================================
    public async Task<CaptchaDetectionResult> CheckCaptchaInternalAsync(bool isAuto)
    {
        if (Status != InstanceStatus.Running) return new CaptchaDetectionResult();

        try
        {
            var result = await _cdpService.DetectCaptchaElementsAsync(CdpPort);

            // Real Captcha Active on Screen
            if (result.IsActive)
            {
                DetectedCaptchaHtml = result.CombinedHtml;
                DetectedCaptchaCount = result.Elements.Count;
                LastCaptchaDetectedTime = DateTime.Now.ToString("HH:mm:ss");
                CaptchaAlertTitle = $"🚨 ตรวจพบ Captcha จริง! ({result.TypeDescription})";
                CaptchaAlertMessage = $"พบ Captcha กำลังแสดงผล: {result.TypeDescription}" + 
                    (!string.IsNullOrEmpty(result.CountdownText) ? $" | เหลือเวลา: {result.CountdownText}" : "");
                IsCaptchaAlertVisible = true;

                DetectedCaptchaElements.Clear();
                foreach (var el in result.Elements)
                {
                    DetectedCaptchaElements.Add(el);
                }
                if (DetectedCaptchaElements.Count > 0 && SelectedCaptchaElement == null)
                {
                    SelectedCaptchaElement = DetectedCaptchaElements[0];
                }

                if (AutoCopyToClipboardOnDetect)
                {
                    try
                    {
                        Clipboard.SetText(result.CombinedHtml);
                        StatusMessage = $"🚨 ตรวจพบ Captcha จริง ({result.TypeDescription}) และคัดลอก Full HTML ลง Clipboard แล้ว!";
                    }
                    catch { }
                }
                else
                {
                    StatusMessage = $"🚨 ตรวจพบ Captcha จริง ({result.TypeDescription})! สามารถกดคัดลอก Full HTML หรือสั่งแก้ได้ทันที";
                }

                if (_lastReportedCaptchaHtml != result.CombinedHtml)
                {
                    _lastReportedCaptchaHtml = result.CombinedHtml;
                    AppendConsole($"[{DateTime.Now:HH:mm:ss}] 🚨 ====================================================================");
                    AppendConsole($"[{DateTime.Now:HH:mm:ss}] 🚨 [CAPTCHA DETECTED] ตรวจพบ Captcha กำลังแสดงผลอยู่จริงบนหน้าจอ!");
                    AppendConsole($"[{DateTime.Now:HH:mm:ss}] 🚨 บัญชี: '{Name}' | CDP Port: {CdpPort} | ประเภท: {result.TypeDescription}");
                    if (!string.IsNullOrEmpty(result.CountdownText))
                    {
                        AppendConsole($"[{DateTime.Now:HH:mm:ss}] ⏳ ตัวนับเวลาถอยหลัง (Countdown): {result.CountdownText}");
                    }
                    if (!string.IsNullOrEmpty(result.PromptText))
                    {
                        AppendConsole($"[{DateTime.Now:HH:mm:ss}] 💡 คำแนะนำโจทย์: {result.PromptText}");
                    }
                    AppendConsole($"[{DateTime.Now:HH:mm:ss}] 🚨 รายละเอียดองค์ประกอบ ({result.Elements.Count} รายการ):");
                    for (int i = 0; i < result.Elements.Count; i++)
                    {
                        var el = result.Elements[i];
                        AppendConsole($"[{DateTime.Now:HH:mm:ss}]    ├─ [#{i + 1}] {el.Summary} (Depth: {el.Depth}, Visible={el.IsVisible})");
                    }
                    AppendConsole($"[{DateTime.Now:HH:mm:ss}] 📋 Full HTML ถูกดึงมาเรียบร้อยแล้ว ({result.CombinedHtml.Length:N0} ตัวอักษร)");
                    AppendConsole($"[{DateTime.Now:HH:mm:ss}] 🚨 ====================================================================\n");

                    Logger.Info("Captcha", $"Auto-detected ACTIVE captcha on '{Name}' (Port: {CdpPort}): {result.TypeDescription}. Countdown='{result.CountdownText ?? "N/A"}'");
                }
            }
            else
            {
                // Result is NOT active (either not present at all, or elements are hidden/unmounted in DOM)
                if (isAuto && IsCaptchaAlertVisible)
                {
                    IsCaptchaAlertVisible = false;
                    _lastReportedCaptchaHtml = null;
                    DetectedCaptchaElements.Clear();
                    SelectedCaptchaElement = null;
                    AppendConsole($"[{DateTime.Now:HH:mm:ss}] ✅ [Auto Detect] Captcha ในหน้าต่างหายไปแล้ว (ผ่านการตรวจสอบเรียบร้อย)\n");
                    StatusMessage = "ตรวจไม่พบ Captcha (หน้าต่างปกติ)";
                }

                // If user manually scanned, let them know if hidden elements were found or none at all
                if (!isAuto)
                {
                    DetectedCaptchaElements.Clear();
                    foreach (var el in result.Elements)
                    {
                        DetectedCaptchaElements.Add(el);
                    }
                    DetectedCaptchaCount = result.Elements.Count;
                    DetectedCaptchaHtml = result.CombinedHtml;
                    if (result.Elements.Count > 0)
                    {
                        SelectedCaptchaElement = result.Elements[0];
                    }

                    if (result.Elements.Count > 0)
                    {
                        StatusMessage = $"พบ {result.Elements.Count} Element ใน DOM แต่ถูกซ่อน/ปิดอยู่ (ไม่ใช่ Captcha ที่กำลังแสดงผล)";
                        AppendConsole($"[{DateTime.Now:HH:mm:ss}] ℹ️ พบ {result.Elements.Count} Captcha elements ใน DOM แต่ทั้งหมดอยู่ในสถานะซ่อนหรือปิดอยู่ (display: none หรือ Modal ปิดแล้ว) - หน้าจอปกติ ไม่มี Captcha กวน\n");
                    }
                    else
                    {
                        StatusMessage = "ตรวจไม่พบ Captcha ในหน้าต่างนี้ (ปกติ)";
                        AppendConsole($"[{DateTime.Now:HH:mm:ss}] 🟢 ไม่พบโครงสร้าง Captcha ใดๆ ในหน้าต่างนี้\n");
                    }
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.Error("Captcha", $"Error checking captcha: {ex.Message}", ex);
            return new CaptchaDetectionResult();
        }
    }

    [RelayCommand]
    public async Task<string?> CopyCaptchaHtmlAsync()
    {
        if (Status != InstanceStatus.Running)
        {
            Logger.Warn("Captcha", $"Cannot copy captcha: '{Name}' is not running.");
            AppendConsole($"[{DateTime.Now:HH:mm:ss}] ⚠️ ไม่สามารถตรวจ Captcha ได้: หน้าต่างนี้ยังไม่ได้เปิดทำงาน กรุณากด Start ก่อน\n");
            StatusMessage = "กรุณาเปิดสตูดิโอก่อนตรวจจับ Captcha";
            return null;
        }

        string time = DateTime.Now.ToString("HH:mm:ss");
        AppendConsole($"[{time}] 🔍 กำลังค้นหา Captcha ในหน้าต่างพอร์ต {CdpPort}...");
        StatusMessage = "กำลังค้นหา Captcha และดึง Full HTML...";

        var result = await CheckCaptchaInternalAsync(isAuto: false);
        if (result.Found && !string.IsNullOrWhiteSpace(result.CombinedHtml))
        {
            try
            {
                Clipboard.SetText(result.CombinedHtml);
                string activeStatus = result.IsActive ? "🚨 กำลังแสดงผลอยู่จริง" : "ℹ️ ซ่อนอยู่ใน DOM";
                StatusMessage = $"✅ คัดลอก Full HTML ({result.Elements.Count} Elements, {activeStatus}) สำเร็จ!";
                AppendConsole($"[{DateTime.Now:HH:mm:ss}] ✅ คัดลอก Full HTML สำเร็จเรียบร้อย! ({activeStatus})");
                AppendConsole($"   📊 พบทั้งหมด: {result.Elements.Count} Element | ประเภท: {result.TypeDescription}");
                for (int i = 0; i < result.Elements.Count; i++)
                {
                    var el = result.Elements[i];
                    AppendConsole($"      [{i + 1}] {el.Summary} (Depth {el.Depth}, Visible={el.IsVisible})");
                }
                AppendConsole($"   📋 โค้ด Full HTML ถูกคัดลอกลง Clipboard แล้ว ({result.CombinedHtml.Length:N0} ตัวอักษร)\n");
            }
            catch (Exception ex)
            {
                AppendConsole($"[{DateTime.Now:HH:mm:ss}] ❌ ไม่สามารถคัดลอกลง Clipboard ได้: {ex.Message}\n");
            }
            return result.CombinedHtml;
        }
        else
        {
            StatusMessage = "ไม่พบ Element ที่มี Captcha ในหน้าต่างนี้";
            AppendConsole($"[{DateTime.Now:HH:mm:ss}] ℹ️ ไม่พบ Element ใดๆ ที่มี ID, Class หรือ Attribute ระบุถึง Captcha ในหน้าต่างนี้\n");
            return null;
        }
    }

    [RelayCommand]
    public void CopyDetectedCaptcha()
    {
        if (!string.IsNullOrEmpty(DetectedCaptchaHtml))
        {
            try
            {
                Clipboard.SetText(DetectedCaptchaHtml);
                StatusMessage = $"📋 คัดลอก Captcha Full HTML ({DetectedCaptchaCount} รายการ) ลง Clipboard เรียบร้อย!";
            }
            catch (Exception ex)
            {
                StatusMessage = $"คัดลอกไม่สำเร็จ: {ex.Message}";
            }
        }
    }


    [RelayCommand]
    public void DismissCaptchaAlert()
    {
        IsCaptchaAlertVisible = false;
    }

    [RelayCommand]
    public void OpenCaptchaTab()
    {
        IsDetailOpen = true;
        SelectedDetailTabIndex = 4; // Subtab 5: Captcha Inspector
    }

    [RelayCommand]
    public void CopyElementHtml(CaptchaElementItem? item)
    {
        var target = item ?? SelectedCaptchaElement;
        if (target != null && !string.IsNullOrEmpty(target.Html))
        {
            try
            {
                Clipboard.SetText(target.Html);
                StatusMessage = $"📋 คัดลอก {target.Summary} ลง Clipboard สำเร็จ!";
                AppendConsole($"[{DateTime.Now:HH:mm:ss}] 📋 คัดลอกเฉพาะ Element {target.Summary} ลง Clipboard แล้ว\n");
            }
            catch (Exception ex)
            {
                StatusMessage = $"คัดลอกไม่สำเร็จ: {ex.Message}";
            }
        }
    }

    [RelayCommand]
    public void ClearCaptchaResults()
    {
        DetectedCaptchaElements.Clear();
        SelectedCaptchaElement = null;
        DetectedCaptchaHtml = string.Empty;
        DetectedCaptchaCount = 0;
        IsCaptchaAlertVisible = false;
        _lastReportedCaptchaHtml = null;
        StatusMessage = "ล้างข้อมูล Captcha เรียบร้อย";
        AppendConsole($"[{DateTime.Now:HH:mm:ss}] 🗑️ ล้างข้อมูลผลลัพธ์ Captcha เรียบร้อยแล้ว\n");
    }

    [RelayCommand]
    public async Task ScanCaptchaNowAsync()
    {
        await CopyCaptchaHtmlAsync();
    }

    [RelayCommand]
    public async Task<string?> ExportCaptchaHtmlAsync(string? targetFilePath = null)
    {
        if (string.IsNullOrEmpty(DetectedCaptchaHtml))
        {
            var res = await CheckCaptchaInternalAsync(isAuto: false);
            if (!res.Found)
            {
                StatusMessage = "ไม่พบ Captcha ให้บันทึก";
                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(targetFilePath))
        {
            string defaultName = $"Captcha_{ProfileFolderName}_{DateTime.Now:yyyyMMdd_HHmmss}.html";
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = $"Export Captcha HTML - {Name}",
                Filter = "HTML Files (*.html;*.htm)|*.html;*.htm|All Files (*.*)|*.*",
                FileName = defaultName,
                DefaultExt = "html"
            };

            if (dialog.ShowDialog() != true) return null;
            targetFilePath = dialog.FileName;
        }

        string header = $"<!-- [TikTok LIVE Studio Sandbox] Captcha Full HTML Dump | Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss} | Profile: {Name} ({ProfileFolderName}) | Port: {CdpPort} -->\n";
        string content = header + DetectedCaptchaHtml;
        await System.IO.File.WriteAllTextAsync(targetFilePath, content, Encoding.UTF8);

        StatusMessage = $"✅ บันทึก Captcha Full HTML สำเร็จ: {System.IO.Path.GetFileName(targetFilePath)}";
        AppendConsole($"[{DateTime.Now:HH:mm:ss}] 💾 บันทึก Captcha Full HTML เป็นไฟล์เรียบร้อย: {targetFilePath}\n");
        return targetFilePath;
    }

    // ==========================================
    // 🌐 Network Hook Commands & Auto Monitor
    // ==========================================
    public async Task StartAutoMonitoringAsync()
    {
        if (Status != InstanceStatus.Running) return;

        // 1. Ensure Storage Auto-Refresh timer is running
        IsStorageAutoRefresh = true;
        EnsureStorageTimer();
        _storageRefreshTimer?.Start();

        // 2. Ensure Captcha Auto-Detection timer is running (every 5 seconds)
        if (IsCaptchaAutoDetect)
        {
            EnsureCaptchaTimer();
            _captchaDetectTimer?.Start();
        }

        // 3. Start Network Hooking automatically
        if (!IsNetworkHooking)
        {
            await StartNetworkHookInternalAsync();
        }

        // 4. Trigger initial snapshot fetch of Cookies & Storage
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            if (IsRunning)
            {
                if (Application.Current != null)
                {
                    await Application.Current.Dispatcher.InvokeAsync(async () =>
                    {
                        await FetchCookiesAndStorageAsync();
                    });
                }
                else
                {
                    await FetchCookiesAndStorageAsync();
                }
            }
        });
    }

    public async Task StartNetworkHookInternalAsync()
    {
        if (Status != InstanceStatus.Running) return;

        IsNetworkHooking = true;
        await _cdpService.StartNetworkHookAsync(CdpPort, 
            onRequest: (item) =>
            {
                void AddItem()
                {
                    if (NetworkTraffic.Count > 1000) NetworkTraffic.RemoveAt(0);
                    NetworkTraffic.Add(item);
                }

                if (Application.Current != null)
                {
                    Application.Current.Dispatcher.Invoke(AddItem);
                }
                else
                {
                    AddItem();
                }
            },
            onResponse: (item) =>
            {
                // Item triggers INotifyPropertyChanged so datagrid updates in-place
            });
        AppendConsole($"[{DateTime.Now:HH:mm:ss}] 🌐 [Auto Monitor] เริ่มดักจับ Network API อัตโนมัติบนพอร์ต {CdpPort} เรียบร้อย");
    }

    public async Task StopNetworkHookInternalAsync()
    {
        IsNetworkHooking = false;
        await _cdpService.StopNetworkHookAsync(CdpPort);
        AppendConsole($"[{DateTime.Now:HH:mm:ss}] ⏹️ [Network] หยุดดักจับ API บนพอร์ต {CdpPort}");
    }

    [RelayCommand]
    public async Task ToggleNetworkHookAsync()
    {
        if (Status != InstanceStatus.Running)
        {
            StatusMessage = "หน้าต่างยังไม่ได้เปิดทำงาน (กรุณากด Start ก่อนดักจับ Network)";
            return;
        }

        if (!IsNetworkHooking)
        {
            await StartNetworkHookInternalAsync();
        }
        else
        {
            await StopNetworkHookInternalAsync();
        }
    }

    [RelayCommand]
    public void ClearTraffic()
    {
        NetworkTraffic.Clear();
        SelectedTraffic = null;
    }

    [RelayCommand]
    public void ResetNetworkFilters()
    {
        NetworkSearchText = string.Empty;
        NetworkUrlFilter = string.Empty;
        NetworkMethodFilter = "ALL";
        NetworkStatusFilter = "ALL";
        FilteredNetworkTraffic?.Refresh();
    }

    [RelayCommand]
    public void CopyResponseBody()
    {
        if (SelectedTraffic != null && !string.IsNullOrEmpty(SelectedTraffic.ResponseBody))
        {
            Clipboard.SetText(SelectedTraffic.ResponseBody);
        }
    }

    [RelayCommand]
    public void CopyPayload()
    {
        if (SelectedTraffic != null && !string.IsNullOrEmpty(SelectedTraffic.PostData))
        {
            Clipboard.SetText(SelectedTraffic.PostData);
        }
    }

    [RelayCommand]
    public void CopyRequestHeaders()
    {
        if (SelectedTraffic != null && !string.IsNullOrEmpty(SelectedTraffic.RequestHeaders))
        {
            Clipboard.SetText(SelectedTraffic.RequestHeaders);
        }
    }

    [RelayCommand]
    public void CopyTrafficUrl()
    {
        if (SelectedTraffic != null && !string.IsNullOrEmpty(SelectedTraffic.Url))
        {
            Clipboard.SetText(SelectedTraffic.Url);
        }
    }

    // ==========================================
    // 🍪 Cookies & Storage Commands
    // ==========================================
    [RelayCommand]
    public async Task FetchCookiesAndStorageAsync()
    {
        if (Status != InstanceStatus.Running)
        {
            StorageStatus = "หน้าต่างยังไม่ได้เปิดทำงาน (กรุณากด Start ก่อน)";
            return;
        }

        StorageStatus = "กำลังดึงข้อมูล Cookies & Storage จาก Chromium...";
        try
        {
            var dump = await _cdpService.GetCookiesAndStorageAsync(CdpPort);

            Cookies.Clear();
            foreach (var c in dump.Cookies) Cookies.Add(c);

            LocalStorageItems.Clear();
            foreach (var pair in dump.LocalStorage)
            {
                LocalStorageItems.Add(new StorageEntry { Key = pair.Key, Value = pair.Value });
            }

            SessionStorageItems.Clear();
            foreach (var pair in dump.SessionStorage)
            {
                SessionStorageItems.Add(new StorageEntry { Key = pair.Key, Value = pair.Value });
            }

            SessionIdToken = dump.SessionIdToken ?? "ไม่พบ sessionid";
            CsrfToken = dump.CsrfToken ?? "ไม่พบ tt_csrf_token";
            StorageStatus = $"อัปเดตเรียลไทม์สำเร็จ ({dump.Cookies.Count} Cookies, {dump.LocalStorage.Count} LocalStorage) เวลา {dump.FetchedAt:HH:mm:ss}";
            SessionFetchStatus = StorageStatus;
        }
        catch (Exception ex)
        {
            StorageStatus = $"ดึงข้อมูลไม่สำเร็จ: {ex.Message}";
        }
    }

    [RelayCommand]
    public Task FetchSessionInfoAsync() => FetchCookiesAndStorageAsync();

    [RelayCommand]
    public void ResetCookieFilters()
    {
        CookieNameFilter = string.Empty;
        CookieDomainFilter = string.Empty;
        CookieValueFilter = string.Empty;
        FilteredCookies?.Refresh();
    }

    [RelayCommand]
    public void ResetStorageFilters()
    {
        StorageKeyFilter = string.Empty;
        StorageValueFilter = string.Empty;
        FilteredLocalStorage?.Refresh();
        FilteredSessionStorage?.Refresh();
    }

    [RelayCommand]
    public void CopyCookieValue(CookieItem? item)
    {
        if (item != null && !string.IsNullOrEmpty(item.Value))
        {
            Clipboard.SetText(item.Value);
            StorageStatus = $"คัดลอก Cookie '{item.Name}' แล้ว!";
        }
    }

    [RelayCommand]
    public void CopyStorageItem(StorageEntry? item)
    {
        if (item != null && !string.IsNullOrEmpty(item.Value))
        {
            Clipboard.SetText(item.Value);
            StorageStatus = $"คัดลอก '{item.Key}' แล้ว!";
        }
    }

    [RelayCommand]
    public void CopyAllCookiesString()
    {
        if (Cookies.Count == 0) return;
        var sb = new StringBuilder();
        foreach (var c in Cookies)
        {
            sb.Append($"{c.Name}={c.Value}; ");
        }
        Clipboard.SetText(sb.ToString().TrimEnd());
        StorageStatus = "คัดลอก Cookie Header String ทั้งหมดแล้ว!";
    }

    [RelayCommand]
    public void CopySessionId()
    {
        if (!string.IsNullOrEmpty(SessionIdToken) && SessionIdToken != "-")
        {
            Clipboard.SetText(SessionIdToken);
            SessionFetchStatus = "คัดลอก sessionid แล้ว!";
        }
    }

    [RelayCommand]
    public void CopyCsrfToken()
    {
        if (!string.IsNullOrEmpty(CsrfToken) && CsrfToken != "-")
        {
            Clipboard.SetText(CsrfToken);
            SessionFetchStatus = "คัดลอก tt_csrf_token แล้ว!";
        }
    }

    public void SyncFromProfile()
    {
        Name = Profile.Name;
        ProfileFolderName = Profile.ProfileFolderName;
        OnPropertyChanged(nameof(SandboxBoxName));
        CdpPort = Profile.CdpPort;
        Status = Profile.Status;
        StatusMessage = Profile.StatusMessage;
        IsWindowHidden = Profile.IsWindowHidden;
        LaunchMode = Profile.LaunchMode;
        HasError = Status == InstanceStatus.Error || (!string.IsNullOrEmpty(StatusMessage) && StatusMessage.Contains("Error", StringComparison.OrdinalIgnoreCase));
        
        LoggedInUserId = Profile.LoggedInUserId;
        LoggedInNickname = Profile.LoggedInNickname;
        LoggedInDisplayId = Profile.LoggedInDisplayId;
        LoggedInAvatarUrl = Profile.LoggedInAvatarUrl;
        IsLoggedIn = Profile.IsLoggedIn;

        OnPropertyChanged(nameof(DisplayNameAndHandle));
        OnPropertyChanged(nameof(LoginBadgeText));
        OnPropertyChanged(nameof(AvatarVisibility));
        OnPropertyChanged(nameof(FallbackAvatarVisibility));
        OnPropertyChanged(nameof(LoggedInVisibility));
        OnPropertyChanged(nameof(NotLoggedInVisibility));
        OnPropertyChanged(nameof(ModeBadgeText));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsNotRunning));
        OnPropertyChanged(nameof(IsStopped));
        OnPropertyChanged(nameof(IsStarting));
        OnPropertyChanged(nameof(StatusDisplay));
        OnPropertyChanged(nameof(WindowVisibilityDisplay));
        OnPropertyChanged(nameof(DetailVisibility));
        OnPropertyChanged(nameof(RunningVisibility));
        OnPropertyChanged(nameof(NotRunningVisibility));
        OnPropertyChanged(nameof(ErrorVisibility));
    }

    [RelayCommand]
    public void RefreshProfile()
    {
        bool hasProfile = _profileManager.UpdateProfileInfo(Profile);
        LoggedInUserId = Profile.LoggedInUserId;
        LoggedInNickname = Profile.LoggedInNickname;
        LoggedInDisplayId = Profile.LoggedInDisplayId;
        LoggedInAvatarUrl = Profile.LoggedInAvatarUrl;
        IsLoggedIn = Profile.IsLoggedIn;

        OnPropertyChanged(nameof(DisplayNameAndHandle));
        OnPropertyChanged(nameof(LoginBadgeText));
        OnPropertyChanged(nameof(AvatarVisibility));
        OnPropertyChanged(nameof(FallbackAvatarVisibility));
        OnPropertyChanged(nameof(LoggedInVisibility));
        OnPropertyChanged(nameof(NotLoggedInVisibility));

        if (hasProfile)
        {
            StatusMessage = $"พบข้อมูลโปรไฟล์ @{LoggedInDisplayId} ({LoggedInNickname})";
            try
            {
                new ConfigStorageService().SaveSettings(_settings);
            }
            catch { }
        }
    }

    [RelayCommand]
    public async Task OpenStudioForLoginAsync()
    {
        Logger.Info("Login", $"Opening TikTok LIVE Studio for interactive login for '{Name}'...");
        ProcessLauncher.BindProtocolToProfile(Profile, _settings);

        if (IsRunning)
        {
            _orchestrator.ShowWindow(Profile);
            SyncFromProfile();
            StartLoginWatcher(autoClose: true);
            StatusMessage = "นำหน้าต่าง TikTok LIVE Studio ขึ้นมาบนหน้าจอแล้ว (เข้าสู่ระบบหรือสแกน QR ได้ทันที - ระบบจะตรวจจับและปิดหน้าต่างอัตโนมัติเมื่อล็อกอินสำเร็จ)";
            Logger.Info("Login", "Studio is already running. Brought window to front for QR login.");
            return;
        }

        StatusMessage = "กำลังเปิดหน้าต่าง TikTok LIVE Studio เพื่อให้คุณล็อกอินตามปกติ...";
        StartLoginWatcher(autoClose: true);

        var loginSettings = new AppSettings
        {
            TikTokStudioExePath = _settings.TikTokStudioExePath,
            AutoHideOnLaunch = false, // Keep visible so user sees the login screen!
            DefaultChromiumFlags = _settings.DefaultChromiumFlags,
            ProxifierExePath = _settings.ProxifierExePath,
            ProxifierProfilePath = _settings.ProxifierProfilePath
        };

        bool ok = await _orchestrator.StartInstanceAsync(Profile, loginSettings);
        SyncFromProfile();

        if (ok)
        {
            _orchestrator.ShowWindow(Profile);
            SyncFromProfile();
            StatusMessage = "เปิด TikTok LIVE Studio เรียบร้อย! กรุณาสแกน QR Code หรือเข้าสู่ระบบบนหน้าจอ (ระบบจะบันทึกโปรไฟล์และปิดสตูดิโอให้อัตโนมัติ)";
            Logger.Info("Login", "Opened TikTok LIVE Studio for user login.");
            _ = StartAutoMonitoringAsync();
        }
        else
        {
            StatusMessage = "ไม่สามารถเปิด TikTok LIVE Studio ได้ (โปรดตรวจดูว่าปิดโปรแกรมเดิมอยู่หรือไม่)";
            Logger.Error("Login", "Failed to launch TikTok LIVE Studio for login.");
        }
    }

    [RelayCommand]
    public Task StartUltraLightLoginAsync() => OpenStudioForLoginAsync();

    private void StartLoginWatcher(bool autoClose = true)
    {
        _profileWatcherSub?.Dispose();
        Logger.Info("Login", $"Started login watcher for '{Name}' (AutoClose: {autoClose})");
        _profileWatcherSub = _profileManager.WatchProfile(Profile, async (info) =>
        {
            if (info.IsLoggedIn)
            {
                Logger.Info("Login", $"Login detected: @{info.DisplayId} ({info.Nickname}), UID: {info.UserId}");
                _profileWatcherSub?.Dispose();
                _profileWatcherSub = null;

                async Task ProcessLoginAsync()
                {
                    RefreshProfile();
                    if (autoClose)
                    {
                        StatusMessage = $"✅ ตรวจพบการเข้าสู่ระบบ: @{info.DisplayId} ({info.Nickname}) - กำลังบันทึกข้อมูลและปิดสตูดิโอ...";
                        AppendConsole($"[{DateTime.Now:HH:mm:ss}] ✅ เข้าสู่ระบบสำเร็จ: @{info.DisplayId} ({info.Nickname}) กำลังรอ 1.5 วินาทีเพื่อให้ Chromium บันทึกไฟล์ลงดิสก์แล้วปิดหน้าต่าง...");

                        // Allow Chromium 1.5 seconds to flush cookies/LevelDB/session files safely to disk
                        await Task.Delay(1500);

                        await StopAsync();

                        StatusMessage = $"✅ เข้าสู่ระบบสำเร็จและปิดสตูดิโอเรียบร้อย! @{info.DisplayId} ({info.Nickname})";
                        AppendConsole($"[{DateTime.Now:HH:mm:ss}] 💾 เซสชันและคุกกี้ถูกบันทึกเรียบร้อย สามารถเปิดใช้งานหรือสั่งรันในโหมดต่างๆ ได้ทันที");
                        Logger.Info("Login", $"Profile saved and studio stopped automatically: @{info.DisplayId}");
                    }
                    else
                    {
                        StatusMessage = $"✅ เข้าสู่ระบบสำเร็จเรียบร้อย! @{info.DisplayId} ({info.Nickname})";
                    }
                }

                if (Application.Current != null)
                {
                    await Application.Current.Dispatcher.InvokeAsync(ProcessLoginAsync);
                }
                else
                {
                    await ProcessLoginAsync();
                }
            }
        });
    }

    [RelayCommand]
    public void CopyError()
    {
        if (!string.IsNullOrWhiteSpace(StatusMessage))
        {
            try
            {
                Clipboard.SetText(StatusMessage);
                ChatStatusText = "คัดลอกข้อความ Error ลง Clipboard แล้ว!";
            }
            catch (Exception ex)
            {
                ChatStatusText = $"คัดลอกไม่สำเร็จ: {ex.Message}";
            }
        }
    }

    [RelayCommand]
    public void CloneSessionFromDefault()
    {
        Logger.Info("Session", $"Attempting to clone session from Default Profile for '{Name}'...");
        bool ok = ProcessLauncher.CloneSessionFromDefaultProfile(Profile);
        if (ok)
        {
            RefreshProfile();
            StatusMessage = $"คัดลอก Cookies & Session จากบัญชีหลักเรียบร้อยแล้ว! (พบบัญชี @{LoggedInDisplayId})";
            Logger.Info("Session", $"Session cloned successfully! Active account: @{LoggedInDisplayId}");
        }
        else
        {
            StatusMessage = "ไม่พบ Session ของบัญชีหลัก หรือคัดลอกไม่สำเร็จ (โปรดล็อกอินใน Default Profile ก่อน)";
            Logger.Warn("Session", "Clone session failed or Default Profile has no session.");
        }
    }

    [RelayCommand]
    public void BindProtocol()
    {
        Logger.Info("Protocol", $"Binding protocol live-studio-app:// to profile '{ProfileFolderName}'...");
        bool ok = ProcessLauncher.BindProtocolToProfile(Profile, _settings);
        StatusMessage = ok 
            ? $"🎯 ตั้งค่า Windows ให้ส่ง Browser Login มาที่โปรไฟล์นี้แล้ว! เมื่อกดยืนยันในเบราว์เซอร์จะเด้งเข้าจอนี้โดยตรง" 
            : "ไม่สามารถตั้งค่า Protocol ใน Windows ได้";
        Logger.Info("Protocol", $"Bind protocol result: {ok}");
    }

    [RelayCommand]
    public void ForwardCallback()
    {
        try
        {
            string clip = Clipboard.GetText()?.Trim() ?? string.Empty;
            Logger.Info("Protocol", $"Forwarding callback from clipboard: {(clip.Length > 60 ? clip.Substring(0, 60) + "..." : clip)}");
            if (clip.StartsWith("live-studio-app:", StringComparison.OrdinalIgnoreCase))
            {
                bool ok = ProcessLauncher.ForwardLoginCallback(Profile, clip, _settings);
                StatusMessage = ok ? "ส่ง Login Callback เข้าแอปเรียบร้อยแล้ว!" : "ส่ง Login Callback ไม่สำเร็จ";
                Logger.Info("Protocol", $"ForwardCallback result: {ok}");
            }
            else if (clip.Contains("tiktok.com/ucenter_web", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "ลิงก์ใน Clipboard คือหน้าเว็บล็อกอิน (ไม่ใช่ Callback)";
                Logger.Warn("Protocol", "Clipboard contains login web URL instead of callback scheme.");
            }
            else
            {
                StatusMessage = "ไม่พบลิงก์ 'live-studio-app://' ใน Clipboard";
                Logger.Warn("Protocol", "No live-studio-app:// URL in clipboard.");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"เกิดข้อผิดพลาด: {ex.Message}";
            Logger.Error("Protocol", $"Error forwarding callback: {ex.Message}", ex);
        }
    }

    private void AppendConsole(string text)
    {
        ConsoleOutput += text + "\n";
    }
}
