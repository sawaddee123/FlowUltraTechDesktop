using System;
using System.IO;
using System.Diagnostics;
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
using TiktokStudioSandbox.Core.Security;
using TiktokStudioSandbox.Core.Services;

namespace TiktokStudioSandbox.App.ViewModels;

public partial class InstanceCardViewModel : ObservableObject
{
    private readonly IInstanceOrchestrator _orchestrator;
    private readonly AppSettings _settings;
    private readonly ICdpAutomationService _cdpService;
    private readonly IProfileManagerService _profileManager;
    private readonly ITelegramNotificationService _telegramService;
    private readonly IRuleEngine _ruleEngine;
    private readonly ITikTokLiveListenerService _liveListenerService;
    private readonly ISlotLeaseClientService _slotLeaseService;
    private IDisposable? _profileWatcherSub;
    private System.Windows.Threading.DispatcherTimer? _storageRefreshTimer;
    private System.Windows.Threading.DispatcherTimer? _chatMonitorTimer;
    private readonly HashSet<string> _seenChatSignatures = new();
    private readonly Queue<string> _chatSignatureQueue = new();
    private const int MaxSeenChatSignatures = 1000;

    private bool TryAddChatSignature(string signature)
    {
        if (!_seenChatSignatures.Add(signature)) return false;
        _chatSignatureQueue.Enqueue(signature);
        while (_chatSignatureQueue.Count > MaxSeenChatSignatures)
        {
            var oldest = _chatSignatureQueue.Dequeue();
            _seenChatSignatures.Remove(oldest);
        }
        return true;
    }
    private string? _currentListeningChannel;

    public StudioInstanceProfile Profile { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayNameAndHandle))]
    [NotifyPropertyChangedFor(nameof(LoginBadgeText))]
    [NotifyPropertyChangedFor(nameof(AvatarVisibility))]
    [NotifyPropertyChangedFor(nameof(FallbackAvatarVisibility))]
    [NotifyPropertyChangedFor(nameof(AvatarImageSource))]
    private string? _loggedInUserId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayNameAndHandle))]
    [NotifyPropertyChangedFor(nameof(AvatarImageSource))]
    private string? _loggedInNickname;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayNameAndHandle))]
    [NotifyPropertyChangedFor(nameof(LoginBadgeText))]
    [NotifyPropertyChangedFor(nameof(AvatarImageSource))]
    private string? _loggedInDisplayId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvatarVisibility))]
    [NotifyPropertyChangedFor(nameof(FallbackAvatarVisibility))]
    [NotifyPropertyChangedFor(nameof(AvatarImageSource))]
    private string? _loggedInAvatarUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayNameAndHandle))]
    [NotifyPropertyChangedFor(nameof(LoginBadgeText))]
    [NotifyPropertyChangedFor(nameof(AvatarVisibility))]
    [NotifyPropertyChangedFor(nameof(FallbackAvatarVisibility))]
    [NotifyPropertyChangedFor(nameof(LoggedInVisibility))]
    [NotifyPropertyChangedFor(nameof(NotLoggedInVisibility))]
    [NotifyPropertyChangedFor(nameof(AvatarImageSource))]
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
    private string? _activeLeaseId;

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
    // Quick Status & Visual Computed Properties
    // ==========================================
    public bool IsRunning => Status == InstanceStatus.Running;
    public bool IsNotRunning => Status != InstanceStatus.Running;
    public bool IsStopped => Status == InstanceStatus.Stopped;
    public bool IsStarting => Status == InstanceStatus.Starting;
    public bool IsRunningOrStarting => IsRunning || IsStarting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(StartButtonToolTip))]
    private bool _isQuotaReached;

    public bool CanStart => !IsRunning && !IsStarting && !IsQuotaReached;

    public string StartButtonToolTip
    {
        get
        {
            if (IsRunning) return "หน้าต่างกำลังทำงานอยู่แล้ว";
            if (IsStarting) return "กำลังเริ่มต้นทำงาน... (สามารถกดปุ่มหยุดเพื่อยกเลิกได้)";
            if (IsQuotaReached) return "เปิดครบโควต้าสูงสุดแล้ว (Quota Reached)";
            return "เริ่มเปิดใช้งาน TikTok Studio";
        }
    }

    public Visibility DetailVisibility => IsDetailOpen ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RunningVisibility => IsRunning ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RunningOrStartingVisibility => IsRunningOrStarting ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NotRunningVisibility => IsNotRunning ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ErrorVisibility => HasError ? Visibility.Visible : Visibility.Collapsed;

    // Profile & Login UI Visual Properties
    public string DisplayNameAndHandle => IsLoggedIn ? $"{LoggedInNickname} (@{LoggedInDisplayId})" : "ยังไม่เข้าสู่ระบบ";
    public string LoginBadgeText => IsLoggedIn ? $"🟢 @{LoggedInDisplayId}" : "⚪ ยังไม่ล็อกอิน";

    public System.Windows.Media.ImageSource? AvatarImageSource
    {
        get
        {
            if (string.IsNullOrWhiteSpace(LoggedInAvatarUrl)) return null;
            try
            {
                if (File.Exists(LoggedInAvatarUrl))
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(LoggedInAvatarUrl, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
                else if (Uri.TryCreate(LoggedInAvatarUrl, UriKind.Absolute, out var uriResult) && 
                         (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.UriSource = uriResult;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
            }
            catch
            {
                // Fallback gracefully
            }
            return null;
        }
    }

    public Visibility AvatarVisibility => AvatarImageSource != null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FallbackAvatarVisibility => AvatarImageSource == null ? Visibility.Visible : Visibility.Collapsed;
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
        IInstanceLogManager? logManager = null,
        ITelegramNotificationService? telegramService = null,
        IRuleEngine? ruleEngine = null,
        ITikTokLiveListenerService? liveListenerService = null,
        ISlotLeaseClientService? slotLeaseService = null)
    {
        Profile = profile;
        _settings = settings;
        _orchestrator = orchestrator;
        _cdpService = cdpService ?? new CdpAutomationService();
        _profileManager = profileManager ?? new ProfileManagerService();
        _telegramService = telegramService ?? new TelegramNotificationService();
        _ruleEngine = ruleEngine ?? new RuleEngine();
        _liveListenerService = liveListenerService ?? new TikTokLiveListenerService();
        _slotLeaseService = slotLeaseService ?? new SlotLeaseClientService(defaultBaseEndpoint: _settings.AuthApiEndpoint);
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

        // Setup CollectionView filtering for Live Chat
        FilteredLiveChatMessages = CollectionViewSource.GetDefaultView(LiveChatMessages);
        FilteredLiveChatMessages.Filter = FilterLiveChatMessageItem;

        _name = profile.Name;
        _profileFolderName = profile.ProfileFolderName;
        _cdpPort = profile.CdpPort;

        // Startup sanitization: Starting can NEVER be valid on initial app launch
        if (profile.Status == InstanceStatus.Starting)
        {
            profile.Status = InstanceStatus.Stopped;
            profile.StatusMessage = "Stopped";
            profile.ProcessId = null;
        }

        _status = profile.Status;
        _statusMessage = profile.StatusMessage;
        _isWindowHidden = profile.IsWindowHidden;
        _launchMode = profile.LaunchMode;

        _loggedInUserId = profile.LoggedInUserId;
        _loggedInNickname = profile.LoggedInNickname;
        _loggedInDisplayId = profile.LoggedInDisplayId;
        _loggedInAvatarUrl = profile.LoggedInAvatarUrl;
        _isLoggedIn = profile.IsLoggedIn;

        string defaultChannel = !string.IsNullOrWhiteSpace(profile.StreamerUsername)
            ? profile.StreamerUsername
            : (!string.IsNullOrWhiteSpace(profile.LoggedInDisplayId)
                ? profile.LoggedInDisplayId
                : (!string.IsNullOrWhiteSpace(profile.LoggedInNickname) ? profile.LoggedInNickname : profile.Name));
        _streamerUsername = NormalizeUsername(defaultChannel);

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
                else
                {
                    // If no process is listening on this CDP port, ensure it is NOT shown as Running or Starting!
                    if (Application.Current != null)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (Profile.Status != InstanceStatus.Stopped)
                            {
                                Profile.Status = InstanceStatus.Stopped;
                                Profile.ProcessId = null;
                                Profile.StatusMessage = "Stopped";
                                SyncFromProfile();
                            }
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

        // Acquire slot lease if auth token exists
        if (!string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            try
            {
                string hwid = DeepHwidGenerator.GenerateDeepHwid();
                var leaseResult = await _slotLeaseService.AcquireSlotAsync(
                    ProfileFolderName,
                    hwid,
                    _settings.AuthToken,
                    _settings.AuthApiEndpoint);

                if (leaseResult.Success && !string.IsNullOrWhiteSpace(leaseResult.LeaseId))
                {
                    ActiveLeaseId = leaseResult.LeaseId;
                    _slotLeaseService.RegisterActiveLease(
                        leaseResult.LeaseId,
                        ProfileFolderName,
                        hwid,
                        _settings.AuthToken,
                        onWatchdogTriggered: () =>
                        {
                            AppendConsole($"[{DateTime.Now:HH:mm:ss}] ⚠️ Slot lease watchdog triggered for '{Name}'. Stopping instance.");
                            _ = StopAsync();
                        });
                    _slotLeaseService.StartHeartbeatTimer();
                    Logger.Info("Lease", $"Acquired slot lease {ActiveLeaseId} for '{Name}'");
                }
                else if (!leaseResult.Success && (leaseResult.Message?.Contains("QUOTA_EXCEEDED") == true || leaseResult.Message?.Contains("โควต้า") == true))
                {
                    StatusMessage = $"ไม่สามารถเริ่มได้: โควต้าจำนวนหน้าต่างเต็ม ({leaseResult.Message})";
                    AppendConsole($"[{DateTime.Now:HH:mm:ss}] ❌ เริ่มหน้าต่างไม่สำเร็จ: โควต้าหน้าต่างเต็ม");
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Lease", $"Slot lease acquisition error: {ex.Message}");
            }
        }

        Profile.IsWindowHidden = false;
        StatusMessage = "กำลังเริ่มโปรแกรม...";
        SyncFromProfile();
        StartLoginWatcher(autoClose: false);

        // Safety timeout watchdog: Ensure instance NEVER hangs in Starting forever
        _ = Task.Run(async () =>
        {
            await Task.Delay(25000);
            if (Profile.Status == InstanceStatus.Starting)
            {
                if (Application.Current != null)
                {
                    await Application.Current.Dispatcher.InvokeAsync(async () =>
                    {
                        if (Profile.Status == InstanceStatus.Starting)
                        {
                            Logger.Warn("Instance", $"Instance '{Name}' launch timed out after 25s. Auto-recovering to Stopped state.");
                            await StopAsync();
                            StatusMessage = "การเริ่มโปรแกรมใช้เวลานานเกินกำหนด (Timeout 25s) - รีเซ็ตสถานะแล้ว";
                            SyncFromProfile();
                        }
                    });
                }
            }
        });

        bool ok = await _orchestrator.StartInstanceAsync(Profile, _settings);
        SyncFromProfile();
        if (ok)
        {
            Profile.IsWindowHidden = false;
            _orchestrator.ShowWindow(Profile);
            SyncFromProfile();
            AppendConsole($"[{DateTime.Now:HH:mm:ss}] 🚀 หน้าต่างเริ่มทำงานแล้ว (CDP Port: {CdpPort}) - เริ่มระบบ Auto Monitor ทันที");
            _ = StartAutoMonitoringAsync();
        }
        else
        {
            // If launch failed, ensure Status is never left in Starting
            if (Profile.Status == InstanceStatus.Starting)
            {
                Profile.Status = InstanceStatus.Stopped;
            }
            SyncFromProfile();

            // Release lease if launch failed
            if (!string.IsNullOrWhiteSpace(ActiveLeaseId))
            {
                try
                {
                    string leaseToRelease = ActiveLeaseId;
                    ActiveLeaseId = null;
                    _slotLeaseService.UnregisterActiveLease(leaseToRelease);
                    await _slotLeaseService.ReleaseSlotAsync(leaseToRelease, _settings.AuthToken, _settings.AuthApiEndpoint);
                }
                catch { }
            }
            AppendConsole($"[{DateTime.Now:HH:mm:ss}] ❌ เริ่มหน้าต่างไม่สำเร็จ: {StatusMessage}");
        }
    }

    [RelayCommand]
    public async Task StopAsync()
    {
        Logger.Info("Instance", $"Stop command triggered for '{Name}'");
        StatusMessage = "กำลังปิดโปรแกรม...";

        // Release slot lease
        if (!string.IsNullOrWhiteSpace(ActiveLeaseId))
        {
            try
            {
                string leaseToRelease = ActiveLeaseId;
                ActiveLeaseId = null;
                _slotLeaseService.UnregisterActiveLease(leaseToRelease);
                await _slotLeaseService.ReleaseSlotAsync(leaseToRelease, _settings.AuthToken, _settings.AuthApiEndpoint);
                Logger.Info("Lease", $"Released slot lease {leaseToRelease} for '{Name}'");
            }
            catch (Exception ex)
            {
                Logger.Warn("Lease", $"Failed to release slot lease: {ex.Message}");
            }
        }

        _profileWatcherSub?.Dispose();
        _profileWatcherSub = null;
        if (IsNetworkHooking)
        {
            await StopNetworkHookInternalAsync();
        }
        _storageRefreshTimer?.Stop();
        _captchaDetectTimer?.Stop();
        _chatMonitorTimer?.Stop();
        IsLiveChatMonitoring = false;
        IsCaptchaAlertVisible = false;
        _lastReportedCaptchaHtml = null;
        await _orchestrator.StopInstanceAsync(Profile);
        Profile.Status = InstanceStatus.Stopped;
        Profile.ProcessId = null;
        Profile.StatusMessage = "Stopped";
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
            AppendConsole($"[{DateTime.Now:HH:mm:ss}] ไม่สามารถรันได้: หน้าต่างนี้ยังไม่ได้เปิดทำงาน (สถานะ: {StatusDisplay}) กรุณากดปุ่ม Start ก่อน\n");
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
            AppendConsole($"[{time}] Error: {ex.Message}\n");
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
            AppendConsole($"[{DateTime.Now:HH:mm:ss}] ไม่สามารถ Export ได้: หน้าต่างนี้ยังไม่ได้เปิดทำงาน (สถานะ: {StatusDisplay}) กรุณากดปุ่ม [Start] ก่อน\n");
            StatusMessage = "กรุณาเปิดสตูดิโอก่อน Export HTML";
            return null;
        }

        string time = DateTime.Now.ToString("HH:mm:ss");
        AppendConsole($"[{time}] กำลังดึง Full HTML จากหน้าต่างผ่าน CDP (พอร์ต {CdpPort})...");
        StatusMessage = "กำลังดึง Full HTML...";
        Logger.Info("CDP", $"Fetching full HTML for '{Name}' on port {CdpPort}...");

        try
        {
            var result = await _cdpService.GetFullHtmlAsync(CdpPort);
            if (result == null || string.IsNullOrWhiteSpace(result.Html))
            {
                AppendConsole($"[{DateTime.Now:HH:mm:ss}] ดึง Full HTML ไม่สำเร็จ (ไม่พบหน้าเว็บที่เปิดอยู่ หรือพอร์ต CDP ไม่ตอบสนอง)\n");
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
                    AppendConsole($"[{DateTime.Now:HH:mm:ss}] ยกเลิกการ Export Full HTML (ไม่ได้เลือกไฟล์ปลายทาง)\n");
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
            StatusMessage = $"Export Full HTML สำเร็จ ({fileInfo.Name})";

            AppendConsole($"[{DateTime.Now:HH:mm:ss}] Export Full HTML สำเร็จเรียบร้อย!");
            AppendConsole($"   ตำแหน่ง: {targetFilePath}");
            AppendConsole($"   ขนาด: {fileInfo.Length:N0} bytes | หน้าเว็บ: {result.Title} ({result.Url})");
            AppendConsole($"   สามารถเปิดไฟล์ด้วย Browser หรือ Editor ได้\n");

            Logger.Info("CDP", $"Successfully exported full HTML to '{targetFilePath}' ({fileInfo.Length} bytes)");
            return targetFilePath;
        }
        catch (Exception ex)
        {
            AppendConsole($"[{DateTime.Now:HH:mm:ss}] เกิดข้อผิดพลาดในการ Export HTML: {ex.Message}\n");
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



    public void SyncFromProfile()
    {
        string defaultChannel = !string.IsNullOrWhiteSpace(Profile.StreamerUsername)
            ? Profile.StreamerUsername
            : (!string.IsNullOrWhiteSpace(Profile.LoggedInDisplayId)
                ? Profile.LoggedInDisplayId
                : (!string.IsNullOrWhiteSpace(Profile.LoggedInNickname) ? Profile.LoggedInNickname : Profile.Name));
        StreamerUsername = NormalizeUsername(defaultChannel);
        OnPropertyChanged(nameof(ActiveChannelDisplay));
        OnPropertyChanged(nameof(LiveChatMonitorBadgeText));
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

        OnPropertyChanged(nameof(AvatarImageSource));
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
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(StartButtonToolTip));
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

        var info = _profileManager.ReadProfile(Profile);
        if (info != null && !string.IsNullOrEmpty(info.SessionId) && (string.IsNullOrEmpty(SessionIdToken) || SessionIdToken == "-" || SessionIdToken.Contains("ไม่พบ")))
        {
            SessionIdToken = info.SessionId;
        }

        if (string.IsNullOrWhiteSpace(Profile.StreamerUsername) && !string.IsNullOrWhiteSpace(LoggedInDisplayId))
        {
            StreamerUsername = NormalizeUsername(LoggedInDisplayId);
        }

        OnPropertyChanged(nameof(AvatarImageSource));
        OnPropertyChanged(nameof(DisplayNameAndHandle));
        OnPropertyChanged(nameof(LoginBadgeText));
        OnPropertyChanged(nameof(AvatarVisibility));
        OnPropertyChanged(nameof(FallbackAvatarVisibility));
        OnPropertyChanged(nameof(LoggedInVisibility));
        OnPropertyChanged(nameof(NotLoggedInVisibility));
        OnPropertyChanged(nameof(ActiveChannelDisplay));

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
            Profile.IsWindowHidden = false;
            _orchestrator.ShowWindow(Profile);
            SyncFromProfile();
            StartLoginWatcher(autoClose: true);
            StatusMessage = "นำหน้าต่าง TikTok LIVE Studio ขึ้นมาบนหน้าจอแล้ว (เข้าสู่ระบบหรือสแกน QR ได้ทันที - ระบบจะตรวจจับและปิดหน้าต่างอัตโนมัติเมื่อล็อกอินสำเร็จ)";
            Logger.Info("Login", "Studio is already running. Brought window to front for QR login.");
            return;
        }

        Profile.IsWindowHidden = false;
        StatusMessage = "กำลังเปิดหน้าต่าง TikTok LIVE Studio เพื่อให้คุณล็อกอินตามปกติ...";
        StartLoginWatcher(autoClose: true);

        // Safety timeout watchdog: Ensure instance NEVER hangs in Starting forever during login
        _ = Task.Run(async () =>
        {
            await Task.Delay(25000);
            if (Profile.Status == InstanceStatus.Starting)
            {
                if (Application.Current != null)
                {
                    await Application.Current.Dispatcher.InvokeAsync(async () =>
                    {
                        if (Profile.Status == InstanceStatus.Starting)
                        {
                            Logger.Warn("Login", $"Login launch for '{Name}' timed out after 25s. Auto-recovering to Stopped state.");
                            await StopAsync();
                            StatusMessage = "การเปิดหน้าต่างเข้าสู่ระบบใช้เวลานานเกินกำหนด (Timeout 25s) - รีเซ็ตสถานะแล้ว";
                            SyncFromProfile();
                        }
                    });
                }
            }
        });

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
            Profile.IsWindowHidden = false;
            _orchestrator.ShowWindow(Profile);
            SyncFromProfile();
            StatusMessage = "เปิด TikTok LIVE Studio เรียบร้อย! กรุณาสแกน QR Code หรือเข้าสู่ระบบบนหน้าจอ (ระบบจะบันทึกโปรไฟล์และปิดสตูดิโอให้อัตโนมัติ)";
            Logger.Info("Login", "Opened TikTok LIVE Studio for user login.");
            _ = StartAutoMonitoringAsync();
        }
        else
        {
            if (Profile.Status == InstanceStatus.Starting)
            {
                Profile.Status = InstanceStatus.Stopped;
            }
            SyncFromProfile();
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
                    SyncFromProfile();
                    if (autoClose)
                    {
                        StatusMessage = $"ตรวจพบการเข้าสู่ระบบ: @{info.DisplayId} ({info.Nickname}) - กำลังบันทึกข้อมูลและปิดสตูดิโอ...";
                        AppendConsole($"[{DateTime.Now:HH:mm:ss}] เข้าสู่ระบบสำเร็จ: @{info.DisplayId} ({info.Nickname}) กำลังรอ 1.5 วินาทีเพื่อให้ Chromium บันทึกไฟล์ลงดิสก์แล้วปิดหน้าต่าง...");

                        // Allow Chromium 1.5 seconds to flush cookies/LevelDB/session files safely to disk
                        await Task.Delay(1500);

                        await StopAsync();

                        StatusMessage = $"เข้าสู่ระบบสำเร็จและปิดสตูดิโอเรียบร้อย: @{info.DisplayId} ({info.Nickname})";
                        AppendConsole($"[{DateTime.Now:HH:mm:ss}] เซสชันและคุกกี้ถูกบันทึกเรียบร้อย สามารถเปิดใช้งานหรือสั่งรันในโหมดต่างๆ ได้ทันที");
                        Logger.Info("Login", $"Profile saved and studio stopped automatically: @{info.DisplayId}");
                    }
                    else
                    {
                        StatusMessage = $"เข้าสู่ระบบสำเร็จเรียบร้อย: @{info.DisplayId} ({info.Nickname})";
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
            SyncFromProfile();
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
            ? $"ตั้งค่า Windows ให้ส่ง Browser Login มาที่โปรไฟล์นี้แล้ว เมื่อกดยืนยันในเบราว์เซอร์จะเด้งเข้าจอนี้โดยตรง" 
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
                if (ok)
                {
                    _ = Task.Run(async () =>
                    {
                        for (int i = 0; i < 6; i++)
                        {
                            await Task.Delay(1000);
                            RunOnUi(() =>
                            {
                                RefreshProfile();
                                SyncFromProfile();
                            });
                            if (IsLoggedIn) break;
                        }
                    });
                }
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
    private static void RunOnUi(Action action)
    {
        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }

}
