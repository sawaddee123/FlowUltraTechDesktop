using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowUltraTech.App.Services;
using FlowUltraTech.App.Views;
using FlowUltraTech.Core;
using FlowUltraTech.Core.Interfaces;
using FlowUltraTech.Core.Models;
using System.Net.Http;
using System.Text.Json;
using FlowUltraTech.Core.Security;
using FlowUltraTech.Core.Services;

namespace FlowUltraTech.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IConfigStorageService _storageService;
    private readonly IInstanceOrchestrator _orchestrator;
    private readonly IProxifierService _proxifierService;
    private readonly ICdpAutomationService _cdpService;
    private readonly IProfileManagerService _profileManager;
    private readonly ITelegramNotificationService _telegramService;
    private readonly ITikTokLiveListenerService _liveListenerService;
    private readonly ISlotLeaseClientService _slotLeaseService;
    private readonly IAuthClientService _authService;
    private readonly IRealtimeClientService _realtimeService;
    private System.Windows.Threading.DispatcherTimer? _telemetryReportTimer;
    private System.Windows.Threading.DispatcherTimer? _telemetryDebounceTimer;

    public AppSettings Settings { get; }

    [ObservableProperty]
    private int _selectedNavIndex = 0;

    public bool IsNavInstances => SelectedNavIndex == 0;
    public bool IsNavCaptcha => SelectedNavIndex == 1;
    public bool IsNavProxies => SelectedNavIndex == 2;
    public bool IsNavRules => SelectedNavIndex == 3;
    public bool IsNavSettings => SelectedNavIndex == 4;

    partial void OnSelectedNavIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsNavInstances));
        OnPropertyChanged(nameof(IsNavCaptcha));
        OnPropertyChanged(nameof(IsNavProxies));
        OnPropertyChanged(nameof(IsNavRules));
        OnPropertyChanged(nameof(IsNavSettings));
    }

    public int RunningInstancesCount => Instances.Count(i => i.Status == InstanceStatus.Running);
    public int TotalCaptchaAlertCount => Instances.Count(i => i.IsCaptchaAlertVisible);
    public bool HasAnyCaptchaAlert => Instances.Any(i => i.IsCaptchaAlertVisible);
    public string RunningStatusSummary => $"{RunningInstancesCount}/{Instances.Count} บัญชีทำงานอยู่";
    public string AppVersionText => $"Sandbox v{AppConstants.CurrentVersion}";
    public string CdpCoreVersionText => $"CDP Core v{AppConstants.CurrentVersion}";
    public string WindowTitle => $"FlowUltraTech v{AppConstants.CurrentVersion}";

    [ObservableProperty]
    private ObservableCollection<InstanceCardViewModel> _instances = new();

    [ObservableProperty]
    private ObservableCollection<ProxyItem> _proxies = new();

    [ObservableProperty]
    private ObservableCollection<AutomationRule> _rules = new();

    [ObservableProperty]
    private string _statusNotification = "พร้อมใช้งาน";

    [ObservableProperty]
    private string _studioExePath = string.Empty;

    [ObservableProperty]
    private string _proxifierExePath = string.Empty;

    [ObservableProperty]
    private string _proxifierProfilePath = string.Empty;

    [ObservableProperty]
    private bool _autoHideOnLaunch;

    // Captcha Sound & Capture Settings
    [ObservableProperty]
    private bool _enableCaptchaSoundAlert = true;

    [ObservableProperty]
    private bool _loopCaptchaSoundAlert = true;

    [ObservableProperty]
    private int _captchaSoundVolume = 80;

    [ObservableProperty]
    private string _captchaSoundFilePath = string.Empty;

    [ObservableProperty]
    private string _captchaSaveDirectory = string.Empty;

    [ObservableProperty]
    private bool _autoSaveCaptchaImage = true;

    [ObservableProperty]
    private bool _autoSolveCaptcha = true;

    [ObservableProperty]
    private int _autoSolveDelaySeconds = 5;

    // Captcha Solver API Settings (SimpleSolveCaptchaProject)
    [ObservableProperty]
    private CaptchaSolverType _captchaSolverMode = CaptchaSolverType.LocalApiServer;

    public bool IsLocalApiSolverMode => true;
    public bool IsMockSolverMode => false;

    public event Action? RequestLogoutRedirect;

    [ObservableProperty]
    private string _captchaSolverEndpoint = "http://127.0.0.1:8001";

    [ObservableProperty]
    private string _captchaSolverJwtToken = string.Empty;

    [ObservableProperty]
    private string _solverApiTestStatus = string.Empty;

    // Telegram Notification Settings
    [ObservableProperty]
    private bool _enableTelegramAlert = false;

    [ObservableProperty]
    private string _telegramBotToken = string.Empty;

    [ObservableProperty]
    private string _telegramChatId = string.Empty;

    [ObservableProperty]
    private bool _telegramAlertOnDetected = true;

    [ObservableProperty]
    private bool _telegramAlertOnSolved = true;

    [ObservableProperty]
    private bool _telegramAlertOnFailed = true;

    [ObservableProperty]
    private bool _telegramSendPhoto = true;

    [ObservableProperty]
    private string _telegramTestStatus = string.Empty;

    [ObservableProperty]
    private DebugViewModel _debug;

    [ObservableProperty]
    private InstanceCardViewModel? _selectedCaptchaInstance;

    public System.Windows.Visibility HasSelectedCaptchaVisibility => SelectedCaptchaInstance != null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public System.Windows.Visibility NoSelectedCaptchaVisibility => SelectedCaptchaInstance == null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    partial void OnSelectedCaptchaInstanceChanged(InstanceCardViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedCaptchaVisibility));
        OnPropertyChanged(nameof(NoSelectedCaptchaVisibility));
    }

    // License Quota & Slot Auth Settings
    [ObservableProperty]
    private int _maxTiktokAccounts = 1;

    [ObservableProperty]
    private string _authApiEndpoint = AppConstants.DefaultServerEndpoint;

    [ObservableProperty]
    private string _authToken = string.Empty;

    [ObservableProperty]
    private string _usernameInput = string.Empty;

    [ObservableProperty]
    private string _passwordInput = string.Empty;

    [ObservableProperty]
    private string _loginErrorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoggingIn;

    [ObservableProperty]
    private string _loggedInUsername = string.Empty;

    [ObservableProperty]
    private string _licenseRole = string.Empty;

    [ObservableProperty]
    private string _licensePlan = "ยังไม่ได้เข้าสู่ระบบ";

    [ObservableProperty]
    private string _licenseExpiryText = "-";

    public bool IsUserLoggedIn => !string.IsNullOrWhiteSpace(AuthToken);
    public bool IsUserNotLoggedIn => !IsUserLoggedIn;

    [ObservableProperty]
    private string _currentMachineHwid = "กำลังโหลด...";

    partial void OnAuthTokenChanged(string value)
    {
        OnPropertyChanged(nameof(IsUserLoggedIn));
        OnPropertyChanged(nameof(IsUserNotLoggedIn));
    }

    public bool IsQuotaFull => Settings.MaxTiktokAccounts > 0 && RunningInstancesCount >= Settings.MaxTiktokAccounts;
    public string QuotaStatusSummary => $"โควต้า: {RunningInstancesCount}/{MaxTiktokAccounts} จอ";

    partial void OnMaxTiktokAccountsChanged(int value)
    {
        Settings.MaxTiktokAccounts = value;
        UpdateQuotaState();
    }

    public MainViewModel(
        IConfigStorageService storageService,
        IInstanceOrchestrator orchestrator,
        IProxifierService proxifierService,
        ICdpAutomationService cdpService,
        IProfileManagerService? profileManager = null,
        ITelegramNotificationService? telegramService = null,
        ITikTokLiveListenerService? liveListenerService = null,
        ISlotLeaseClientService? slotLeaseService = null,
        IAuthClientService? authClientService = null,
        IRealtimeClientService? realtimeService = null)
    {
        _storageService = storageService;
        _orchestrator = orchestrator;
        _proxifierService = proxifierService;
        _cdpService = cdpService;
        _profileManager = profileManager ?? new Core.Services.ProfileManagerService();
        _telegramService = telegramService ?? new TelegramNotificationService();
        _liveListenerService = liveListenerService ?? new TikTokLiveListenerService();
        _debug = new DebugViewModel(cdpService);

        Settings = _storageService.LoadSettings();
        _realtimeService = realtimeService ?? new RealtimeClientService();
        _slotLeaseService = slotLeaseService ?? new SlotLeaseClientService(defaultBaseEndpoint: AppConstants.DefaultServerEndpoint, realtimeService: _realtimeService);
        if (_slotLeaseService.RealtimeService == null)
        {
            _slotLeaseService.RealtimeService = _realtimeService;
        }
        _authService = authClientService ?? new AuthClientService(defaultBaseEndpoint: AppConstants.DefaultServerEndpoint);

        _realtimeService.OnQuotaReduced += HandleRealtimeQuotaReduced;
        _realtimeService.OnLicenseKickout += HandleRealtimeLicenseKickout;
        _realtimeService.OnLicenseUpdated += HandleRealtimeLicenseUpdated;
        _realtimeService.OnAppUpdateBroadcast += HandleRealtimeAppUpdateBroadcast;
        _realtimeService.OnConnected += (s, e) =>
        {
            System.Diagnostics.Debug.WriteLine("[Realtime] Connected to central server, emitting instance telemetry...");
            SendInstancesTelemetryReport();
        };

        StudioExePath = Settings.TikTokStudioExePath;
        ProxifierExePath = Settings.ProxifierExePath;
        ProxifierProfilePath = Settings.ProxifierProfilePath;
        AutoHideOnLaunch = Settings.AutoHideOnLaunch;
        EnableCaptchaSoundAlert = Settings.EnableCaptchaSoundAlert;
        LoopCaptchaSoundAlert = Settings.LoopCaptchaSoundAlert;
        CaptchaSoundVolume = Settings.CaptchaSoundVolume;
        CaptchaSoundFilePath = Settings.CaptchaSoundFilePath;
        CaptchaSaveDirectory = !string.IsNullOrWhiteSpace(Settings.CaptchaSaveDirectory)
            ? Settings.CaptchaSaveDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPictures), "TikTokStudioCaptures");
        AutoSaveCaptchaImage = Settings.AutoSaveCaptchaImage;
        AutoSolveCaptcha = Settings.AutoSolveCaptcha;
        AutoSolveDelaySeconds = Settings.AutoSolveDelaySeconds > 0 ? Settings.AutoSolveDelaySeconds : 5;

        CaptchaSolverMode = Settings.CaptchaSolverMode;
        CaptchaSolverEndpoint = AppConstants.DefaultServerEndpoint;
        CaptchaSolverJwtToken = Settings.CaptchaSolverJwtToken ?? string.Empty;

        MaxTiktokAccounts = Settings.MaxTiktokAccounts > 0 ? Settings.MaxTiktokAccounts : 1;
        AuthApiEndpoint = AppConstants.DefaultServerEndpoint;
        AuthToken = Settings.AuthToken ?? string.Empty;

        LoggedInUsername = Settings.LoggedInUsername ?? string.Empty;
        LicenseRole = Settings.LicenseRole ?? string.Empty;
        LicensePlan = !string.IsNullOrWhiteSpace(Settings.LicensePlan) ? Settings.LicensePlan : "ยังไม่ได้เข้าสู่ระบบ";
        LicenseExpiryText = Settings.LicenseExpiresAt.HasValue ? Settings.LicenseExpiresAt.Value.ToString("dd/MM/yyyy HH:mm") : "-";

        if (!string.IsNullOrWhiteSpace(AuthToken))
        {
            _ = _realtimeService.ConnectAsync(AuthApiEndpoint, AuthToken);
            _ = Task.Run(async () =>
            {
                try { await RefreshLicenseAsync(); } catch { }
                try { await SyncWithCloudAsync(); } catch { }
            });
        }

        // Check for latest/mandatory version on startup
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);
            await CheckLatestVersionOnStartupAsync();
        });

        EnableTelegramAlert = Settings.EnableTelegramAlert;
        TelegramBotToken = Settings.TelegramBotToken ?? string.Empty;
        TelegramChatId = Settings.TelegramChatId ?? string.Empty;
        TelegramAlertOnDetected = Settings.TelegramAlertOnDetected;
        TelegramAlertOnSolved = Settings.TelegramAlertOnSolved;
        TelegramAlertOnFailed = Settings.TelegramAlertOnFailed;
        TelegramSendPhoto = Settings.TelegramSendPhoto;

        foreach (var inst in Settings.Instances)
        {
            var card = new InstanceCardViewModel(inst, Settings, _orchestrator, _cdpService, _profileManager, telegramService: _telegramService, liveListenerService: _liveListenerService, slotLeaseService: _slotLeaseService, authClientService: _authService);
            AttachCardEvents(card);
            Instances.Add(card);
        }

        foreach (var p in Settings.Proxies)
        {
            Proxies.Add(p);
        }

        foreach (var r in Settings.Rules)
        {
            Rules.Add(r);
        }

        SelectedCaptchaInstance = Instances.FirstOrDefault();
        UpdateQuotaState();
        StartLicenseWatchdog();
        StartTelemetryReporting();

        // Initialize HWID asynchronously to prevent blocking the UI thread during launch
        _ = Task.Run(async () =>
        {
            string hwid = await DeepHwidGenerator.GenerateDeepHwidAsync();
            if (Application.Current?.Dispatcher != null)
            {
                _ = Application.Current.Dispatcher.InvokeAsync(() => CurrentMachineHwid = hwid);
            }
            else
            {
                CurrentMachineHwid = hwid;
            }
        });
    }

    public void UpdateQuotaState()
    {
        bool isFull = Settings.MaxTiktokAccounts > 0 && RunningInstancesCount >= Settings.MaxTiktokAccounts;
        foreach (var card in Instances)
        {
            card.IsQuotaReached = isFull && card.Status != InstanceStatus.Running;
        }
        OnPropertyChanged(nameof(IsQuotaFull));
        OnPropertyChanged(nameof(QuotaStatusSummary));
        OnPropertyChanged(nameof(RunningStatusSummary));
    }

    private void AttachCardEvents(InstanceCardViewModel card)
    {
        card.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(InstanceCardViewModel.Status) or nameof(InstanceCardViewModel.IsCaptchaAlertVisible))
            {
                OnPropertyChanged(nameof(RunningInstancesCount));
                OnPropertyChanged(nameof(TotalCaptchaAlertCount));
                OnPropertyChanged(nameof(HasAnyCaptchaAlert));
                OnPropertyChanged(nameof(RunningStatusSummary));
                UpdateQuotaState();
            }

            if (e.PropertyName is nameof(InstanceCardViewModel.Status)
                or nameof(InstanceCardViewModel.IsCaptchaAlertVisible)
                or nameof(InstanceCardViewModel.IsSolvingCaptcha)
                or nameof(InstanceCardViewModel.DetectedCaptchaTypeDescription)
                or nameof(InstanceCardViewModel.DetectedCaptchaCountdown)
                or nameof(InstanceCardViewModel.LoggedInNickname)
                or nameof(InstanceCardViewModel.LoggedInDisplayId)
                or nameof(InstanceCardViewModel.IsLoggedIn)
                or nameof(InstanceCardViewModel.ActiveLeaseId))
            {
                TriggerInstanceReportDebounced();
            }
        };
    }

    private void StartTelemetryReporting()
    {
        _telemetryReportTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        _telemetryReportTimer.Tick += (s, e) => SendInstancesTelemetryReport();
        _telemetryReportTimer.Start();

        var initTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        initTimer.Tick += (s, e) =>
        {
            initTimer.Stop();
            SendInstancesTelemetryReport();
        };
        initTimer.Start();
    }

    private void TriggerInstanceReportDebounced()
    {
        if (_telemetryDebounceTimer == null)
        {
            _telemetryDebounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _telemetryDebounceTimer.Tick += (s, e) =>
            {
                _telemetryDebounceTimer.Stop();
                SendInstancesTelemetryReport();
            };
        }
        _telemetryDebounceTimer.Stop();
        _telemetryDebounceTimer.Start();
    }

    public void SendInstancesTelemetryReport()
    {
        if (!_realtimeService.IsConnected)
            return;

        // Quick snapshot of instance card states from UI thread (< 0.1ms)
        var snapshots = Instances.Select(inst => new
        {
            inst.Name,
            inst.ProfileFolderName,
            inst.Profile.CdpPort,
            inst.Status,
            inst.Profile.ProcessId,
            inst.Profile.StartedAt,
            inst.Profile.ProxyId,
            inst.IsLoggedIn,
            inst.LoggedInNickname,
            inst.LoggedInDisplayId,
            inst.LoggedInAvatarUrl,
            inst.ActiveLeaseId,
            inst.IsCaptchaAlertVisible,
            inst.IsSolvingCaptcha,
            inst.DetectedCaptchaTypeDescription,
            inst.DetectedCaptchaCountdown,
            inst.LastCaptchaDetectedTime,
            inst.DetectedCaptchaCount
        }).ToList();

        // Offload heavy process validation, PID listening checks (netstat), and WebSocket emit to background task
        _ = Task.Run(async () =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var instList = new System.Collections.Generic.List<object>();
                int runningCount = 0;

                foreach (var inst in snapshots)
                {
                    bool isActuallyRunning = false;
                    int? effectivePid = inst.ProcessId;

                    if (inst.Status == InstanceStatus.Running)
                    {
                        if (effectivePid.HasValue && effectivePid.Value > 0)
                        {
                            try
                            {
                                var proc = System.Diagnostics.Process.GetProcessById(effectivePid.Value);
                                isActuallyRunning = !proc.HasExited;
                            }
                            catch
                            {
                                isActuallyRunning = false;
                            }
                        }

                        if (!isActuallyRunning && inst.CdpPort > 0)
                        {
                            var portPid = ProcessLauncher.GetPidListeningOnPort(inst.CdpPort);
                            if (portPid.HasValue)
                            {
                                isActuallyRunning = true;
                                effectivePid = portPid.Value;
                            }
                        }
                    }

                    if (isActuallyRunning)
                    {
                        runningCount++;
                    }

                    long uptimeSec = 0;
                    if (isActuallyRunning && inst.StartedAt.HasValue)
                    {
                        uptimeSec = Math.Max(0, (long)(DateTime.UtcNow - inst.StartedAt.Value.ToUniversalTime()).TotalSeconds);
                    }

                    string proxyStr = "Direct";
                    if (inst.ProxyId.HasValue)
                    {
                        var p = Proxies.FirstOrDefault(x => x.Id == inst.ProxyId.Value);
                        if (p != null)
                        {
                            proxyStr = $"{p.Host}:{p.Port}";
                        }
                    }

                    instList.Add(new
                    {
                        name = inst.Name,
                        accountName = inst.Name,
                        profileFolderName = inst.ProfileFolderName,
                        cdpPort = inst.CdpPort,
                        status = isActuallyRunning ? "Running" : (inst.Status == InstanceStatus.Running ? "Stopped" : inst.Status.ToString()),
                        isTikTokRunning = isActuallyRunning,
                        processId = effectivePid,
                        startedAt = inst.StartedAt?.ToString("o"),
                        uptimeSeconds = uptimeSec,
                        proxy = proxyStr,
                        isLoggedIn = inst.IsLoggedIn,
                        channelName = inst.LoggedInNickname ?? string.Empty,
                        channelHandle = inst.LoggedInDisplayId ?? string.Empty,
                        channelAvatar = inst.LoggedInAvatarUrl ?? string.Empty,
                        activeLeaseId = inst.ActiveLeaseId ?? string.Empty,
                        captcha = new
                        {
                            hasAlert = inst.IsCaptchaAlertVisible,
                            isSolving = inst.IsSolvingCaptcha,
                            typeDescription = inst.DetectedCaptchaTypeDescription,
                            countdown = inst.DetectedCaptchaCountdown,
                            lastDetectedTime = inst.LastCaptchaDetectedTime,
                            count = inst.DetectedCaptchaCount
                        }
                    });
                }

                var payload = new
                {
                    appVersion = AppConstants.CurrentVersion,
                    hwid = CurrentMachineHwid,
                    hostname = Environment.MachineName,
                    isProgramOpen = true,
                    runningCount = runningCount,
                    totalCount = instList.Count,
                    instances = instList
                };

                await _realtimeService.ReportInstancesAsync(payload);
                sw.Stop();
                double elapsedMs = sw.Elapsed.TotalMilliseconds;

                if (elapsedMs > 200)
                {
                    System.Diagnostics.Debug.WriteLine($"[Telemetry] ⚠️ Telemetry report took {elapsedMs:F1}ms (Lag Warning) for {instList.Count} instances.");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[Telemetry] Emitted instance report ({runningCount}/{instList.Count} running, took {elapsedMs:F1}ms)");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Telemetry] Error in telemetry reporting: {ex.Message}");
            }
        });
    }

    [RelayCommand]
    public void NavigateTo(string? indexStr)
    {
        if (int.TryParse(indexStr, out int idx))
        {
            SelectedNavIndex = idx;
        }
    }

    [RelayCommand]
    public void NavigateInstances() => SelectedNavIndex = 0;

    [RelayCommand]
    public void NavigateCaptcha() => SelectedNavIndex = 1;

    [RelayCommand]
    public void NavigateProxies() => SelectedNavIndex = 2;

    [RelayCommand]
    public void NavigateRules() => SelectedNavIndex = 3;

    [RelayCommand]
    public void NavigateSettings() => SelectedNavIndex = 4;

    [RelayCommand]
    public async Task ScanAllCaptchasAsync()
    {
        StatusNotification = "กำลังสแกนหา Captcha ในทุกบัญชีที่เปิดทำงาน...";
        int foundCount = 0;
        int runningCount = 0;
        foreach (var inst in Instances)
        {
            if (inst.Status == InstanceStatus.Running)
            {
                runningCount++;
                var res = await inst.CheckCaptchaInternalAsync(isAuto: false);
                if (res.Found) foundCount++;
            }
        }

        if (runningCount == 0)
        {
            StatusNotification = "ไม่มีบัญชีใดเปิดทำงานอยู่ (กรุณากดเริ่มทำงานก่อนสแกน)";
        }
        else
        {
            StatusNotification = foundCount > 0
                ? $"สแกนเสร็จสิ้น: ตรวจพบ Captcha ใน {foundCount} บัญชี"
                : $"สแกนเสร็จสิ้น ({runningCount} บัญชี): ไม่พบ Captcha";
        }
    }

    [RelayCommand]
    public async Task StartAllAsync()
    {
        StatusNotification = "กำลังเริ่มทุก Instance...";
        int started = 0;
        foreach (var card in Instances)
        {
            if (Settings.MaxTiktokAccounts > 0 && RunningInstancesCount >= Settings.MaxTiktokAccounts)
            {
                StatusNotification = $"เริ่มได้ {started} บัญชี (ครบโควต้าสูงสุด {Settings.MaxTiktokAccounts} จอ)";
                return;
            }
            if (card.IsNotRunning)
            {
                await card.StartAsync();
                if (card.IsRunning) started++;
            }
        }
        StatusNotification = "เริ่มทำงานทุก Instance สำเร็จ";
    }

    [RelayCommand]
    public async Task StopAllAsync()
    {
        StatusNotification = "กำลังปิดทุก Instance...";
        foreach (var card in Instances)
        {
            await card.StopAsync();
        }
        Core.Services.ProcessLauncher.KillAllStudioProcesses();
        StatusNotification = "ปิดทุก Instance และโปรเซสทั้งหมดสำเร็จ";
    }

    [RelayCommand]
    public void HideAllWindows()
    {
        int count = 0;
        foreach (var card in Instances)
        {
            if (card.Status == InstanceStatus.Running || card.Status == InstanceStatus.Starting)
            {
                card.HideWindow();
                count++;
            }
        }
        StatusNotification = $"ซ่อนหน้าต่างทั้งหมดเรียบร้อย ({count} หน้าต่าง)";
    }

    [RelayCommand]
    public void ShowAllWindows()
    {
        int count = 0;
        foreach (var card in Instances)
        {
            if (card.Status == InstanceStatus.Running || card.Status == InstanceStatus.Starting)
            {
                card.ShowWindow();
                count++;
            }
        }
        StatusNotification = $"แสดงหน้าต่างทั้งหมดเรียบร้อย ({count} หน้าต่าง)";
    }

    [RelayCommand]
    public void AddInstance()
    {
        int count = Instances.Count + 1;
        int nextPort = 9220 + count;
        var newProfile = new StudioInstanceProfile
        {
            Name = $"Account {count:D2}",
            ProfileFolderName = $"Acc{count:D2}",
            CdpPort = nextPort,
            LaunchMode = InstanceLaunchMode.DirectProfile
        };

        Settings.Instances.Add(newProfile);
        var card = new InstanceCardViewModel(newProfile, Settings, _orchestrator, _cdpService, _profileManager, telegramService: _telegramService, liveListenerService: _liveListenerService, slotLeaseService: _slotLeaseService, authClientService: _authService);
        AttachCardEvents(card);
        Instances.Add(card);
        _storageService.SaveSettings(Settings);
        UpdateQuotaState();
        OnPropertyChanged(nameof(RunningInstancesCount));
        OnPropertyChanged(nameof(TotalCaptchaAlertCount));
        OnPropertyChanged(nameof(HasAnyCaptchaAlert));
        OnPropertyChanged(nameof(RunningStatusSummary));
        StatusNotification = $"เพิ่ม {newProfile.Name} (พอร์ต {newProfile.CdpPort})";

        // Sync to cloud if authenticated
        if (!string.IsNullOrWhiteSpace(AuthToken))
        {
            var authSvc = _authService;
            var token = AuthToken;
            var endpoint = AuthApiEndpoint;
            _ = Task.Run(async () =>
            {
                try
                {
                    var dto = new ProfileSyncDto
                    {
                        ClientProfileId = newProfile.Id.ToString(),
                        Name = newProfile.Name,
                        ProfileFolderName = newProfile.ProfileFolderName,
                        CdpPort = newProfile.CdpPort,
                        LaunchMode = newProfile.LaunchMode.ToString(),
                        CustomChromiumFlags = newProfile.CustomChromiumFlags,
                        StreamerUsername = newProfile.StreamerUsername
                    };
                    await authSvc.UpsertProfileAsync(token, dto, endpoint);
                }
                catch { }
            });
        }
    }

    [RelayCommand]
    public async Task RemoveInstanceAsync(InstanceCardViewModel? card)
    {
        if (card == null) return;
        await card.StopAsync();
        Instances.Remove(card);
        Settings.Instances.Remove(card.Profile);
        _storageService.SaveSettings(Settings);
        UpdateQuotaState();
        OnPropertyChanged(nameof(RunningInstancesCount));
        OnPropertyChanged(nameof(TotalCaptchaAlertCount));
        OnPropertyChanged(nameof(HasAnyCaptchaAlert));
        OnPropertyChanged(nameof(RunningStatusSummary));
        StatusNotification = $"ลบ {card.Name} เรียบร้อยแล้ว";

        // Sync deletion to cloud if authenticated
        if (!string.IsNullOrWhiteSpace(AuthToken))
        {
            var authSvc = _authService;
            var token = AuthToken;
            var endpoint = AuthApiEndpoint;
            var folder = card.ProfileFolderName;
            _ = Task.Run(async () =>
            {
                try
                {
                    await authSvc.DeleteProfileAsync(token, folder, endpoint);
                }
                catch { }
            });
        }
    }

    [RelayCommand]
    public void AddProxy()
    {
        int count = Proxies.Count + 1;
        var proxy = new ProxyItem
        {
            Name = $"Proxy #{count}",
            Host = "127.0.0.1",
            Port = 10800 + count,
            Protocol = ProxyProtocol.Socks5,
            ProxifierId = 100 + count
        };

        Proxies.Add(proxy);
        Settings.Proxies.Add(proxy);
        _storageService.SaveSettings(Settings);
        StatusNotification = $"เพิ่ม {proxy.Name} เรียบร้อยแล้ว";

        if (!string.IsNullOrWhiteSpace(AuthToken))
        {
            var authSvc = _authService;
            var token = AuthToken;
            var endpoint = AuthApiEndpoint;
            _ = Task.Run(async () =>
            {
                try
                {
                    var dto = new ProxySyncDto
                    {
                        ClientProxyId = proxy.Id.ToString(),
                        Name = proxy.Name,
                        Host = proxy.Host,
                        Port = proxy.Port,
                        Protocol = proxy.Protocol.ToString().ToLowerInvariant(),
                        Username = proxy.Username,
                        Password = proxy.Password,
                        ProxifierId = proxy.ProxifierId,
                        IsActive = proxy.IsActive
                    };
                    await authSvc.UpsertProxyAsync(token, dto, endpoint);
                }
                catch { }
            });
        }
    }

    [RelayCommand]
    public void RemoveProxy(ProxyItem? proxy)
    {
        if (proxy == null) return;
        Proxies.Remove(proxy);
        Settings.Proxies.Remove(proxy);
        _storageService.SaveSettings(Settings);
        StatusNotification = $"ลบ {proxy.Name} เรียบร้อยแล้ว";

        if (!string.IsNullOrWhiteSpace(AuthToken))
        {
            var authSvc = _authService;
            var token = AuthToken;
            var endpoint = AuthApiEndpoint;
            var id = proxy.ProxifierId.ToString();
            _ = Task.Run(async () =>
            {
                try
                {
                    await authSvc.DeleteProxyAsync(token, id, endpoint);
                }
                catch { }
            });
        }
    }

    [RelayCommand]
    public void GenerateProxifierConfig()
    {
        try
        {
            string profilePath = ProxifierProfilePath;
            if (string.IsNullOrWhiteSpace(profilePath))
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                profilePath = Path.Combine(appData, "Proxifier", "Profiles", "TikTokSandbox.ppx");
                ProxifierProfilePath = profilePath;
                Settings.ProxifierProfilePath = profilePath;
            }

            _proxifierService.UpdateProfileFile(profilePath, Settings.Instances, Settings.Proxies);
            _storageService.SaveSettings(Settings);
            StatusNotification = $"อัปเดตกฎ Proxifier XML สำเร็จ: {profilePath}";
        }
        catch (Exception ex)
        {
            StatusNotification = $"เกิดข้อผิดพลาด Proxifier: {ex.Message}";
        }
    }

    [RelayCommand]
    public void LaunchProxifier()
    {
        GenerateProxifierConfig();
        bool ok = _proxifierService.ReloadProxifier(ProxifierExePath, ProxifierProfilePath);
        StatusNotification = ok ? "โหลดโปรไฟล์เข้า Proxifier สำเร็จ!" : "ไม่สามารถโหลด Proxifier (โปรดตรวจ Path)";
    }

    [RelayCommand]
    public void AddRule()
    {
        var rule = new AutomationRule
        {
            Name = $"Auto Reply #{Rules.Count + 1}",
            TriggerType = RuleTriggerType.ChatKeyword,
            Pattern = "!hello",
            ResponseTemplate = "สวัสดี @{User}!"
        };
        Rules.Add(rule);
        Settings.Rules.Add(rule);
        _storageService.SaveSettings(Settings);
        StatusNotification = "เพิ่มกฎใหม่เรียบร้อย";
    }

    [RelayCommand]
    public void RemoveRule(AutomationRule? rule)
    {
        if (rule == null) return;
        Rules.Remove(rule);
        Settings.Rules.Remove(rule);
        _storageService.SaveSettings(Settings);
        StatusNotification = $"ลบกฎ {rule.Name} เรียบร้อย";
    }


    [RelayCommand]
    public async Task SaveSettingsAsync()
    {
        Settings.TikTokStudioExePath = StudioExePath;
        Settings.ProxifierExePath = ProxifierExePath;
        Settings.ProxifierProfilePath = ProxifierProfilePath;
        Settings.AutoHideOnLaunch = AutoHideOnLaunch;
        Settings.EnableCaptchaSoundAlert = EnableCaptchaSoundAlert;
        Settings.LoopCaptchaSoundAlert = LoopCaptchaSoundAlert;
        Settings.CaptchaSoundVolume = CaptchaSoundVolume;
        Settings.CaptchaSoundFilePath = CaptchaSoundFilePath;
        Settings.CaptchaSaveDirectory = CaptchaSaveDirectory;
        Settings.AutoSaveCaptchaImage = AutoSaveCaptchaImage;
        Settings.AutoSolveCaptcha = AutoSolveCaptcha;
        Settings.AutoSolveDelaySeconds = AutoSolveDelaySeconds;

        Settings.CaptchaSolverMode = CaptchaSolverMode;
        Settings.CaptchaSolverEndpoint = AppConstants.DefaultServerEndpoint;
        Settings.CaptchaSolverJwtToken = CaptchaSolverJwtToken;

        Settings.EnableTelegramAlert = EnableTelegramAlert;
        Settings.TelegramBotToken = TelegramBotToken;
        Settings.TelegramChatId = TelegramChatId;
        Settings.TelegramAlertOnDetected = TelegramAlertOnDetected;
        Settings.TelegramAlertOnSolved = TelegramAlertOnSolved;
        Settings.TelegramAlertOnFailed = TelegramAlertOnFailed;
        Settings.TelegramSendPhoto = TelegramSendPhoto;

        Settings.MaxTiktokAccounts = MaxTiktokAccounts;
        Settings.AuthApiEndpoint = AppConstants.DefaultServerEndpoint;
        Settings.AuthToken = AuthToken;

        await _storageService.SaveSettingsAsync(Settings);
        UpdateQuotaState();
        StatusNotification = "บันทึกการตั้งค่าทั้งหมดเรียบร้อยแล้ว";

        if (!string.IsNullOrWhiteSpace(AuthToken))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var cloudDto = new CloudSettingsDto
                    {
                        TelegramBotToken = TelegramBotToken,
                        TelegramChatId = TelegramChatId,
                        AutoSolveDelaySeconds = AutoSolveDelaySeconds,
                        CaptchaSoundVolume = CaptchaSoundVolume,
                        AutoSaveCaptchaImage = AutoSaveCaptchaImage,
                        Rules = Rules.ToList()
                    };
                    await _authService.SaveCloudSettingsAsync(AuthToken, cloudDto, AuthApiEndpoint);
                }
                catch { }
            });
        }
    }

    [RelayCommand]
    public async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(UsernameInput) || string.IsNullOrWhiteSpace(PasswordInput))
        {
            LoginErrorMessage = "กรุณากรอกชื่อผู้ใช้และรหัสผ่าน";
            return;
        }

        IsLoggingIn = true;
        LoginErrorMessage = "กำลังตรวจสอบสิทธิ์และเชื่อมต่อเซิร์ฟเวอร์...";

        string hwid = DeepHwidGenerator.GenerateDeepHwid();
        var res = await _authService.LoginAsync(UsernameInput, PasswordInput, hwid);

        IsLoggingIn = false;
        if (res.Success && !string.IsNullOrWhiteSpace(res.AccessToken))
        {
            AuthToken = res.AccessToken;
            Settings.AuthToken = res.AccessToken;
            Settings.RefreshToken = res.RefreshToken ?? string.Empty;
            CaptchaSolverJwtToken = res.AccessToken;
            Settings.CaptchaSolverJwtToken = res.AccessToken;

            MaxTiktokAccounts = res.MaxTiktokAccounts;
            Settings.MaxTiktokAccounts = res.MaxTiktokAccounts;

            LoggedInUsername = res.Username ?? UsernameInput;
            Settings.LoggedInUsername = LoggedInUsername;

            LicenseRole = res.Role ?? "user";
            Settings.LicenseRole = LicenseRole;

            LicensePlan = res.Plan ?? "standard";
            Settings.LicensePlan = LicensePlan;

            Settings.LicenseExpiresAt = res.ExpiredAt;
            LicenseExpiryText = res.ExpiredAt.HasValue ? res.ExpiredAt.Value.ToString("dd/MM/yyyy HH:mm") : "ไม่มีวันหมดอายุ";

            LoginErrorMessage = string.Empty;
            PasswordInput = string.Empty;

            // Check application version requirement
            if (res.AppVersion != null && !string.IsNullOrWhiteSpace(res.AppVersion.MinRequiredVersion))
            {
                var appVer = typeof(MainViewModel).Assembly.GetName().Version ?? new Version(0, 1, 0);
                var currentVerStr = $"{appVer.Major}.{appVer.Minor}.{Math.Max(0, appVer.Build)}";
                if (Version.TryParse(res.AppVersion.MinRequiredVersion.Trim().TrimStart('v', 'V'), out var minVer) && appVer < minVer)
                {
                    RunOnUi(() =>
                    {
                        MessageBox.Show(
                            $"เวอร์ชันของโปรแกรมล้าสมัย (เวอร์ชันปัจจุบัน {currentVerStr} ต่ำกว่าขั้นต่ำ {res.AppVersion.MinRequiredVersion})\nกรุณาดาวน์โหลดเวอร์ชันใหม่ได้ที่:\n{res.AppVersion.DownloadUrl}",
                            "โปรดอัปเดตโปรแกรม (Update Required)",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning
                        );
                    });
                }
            }

            // Sync cloud settings & rules if available
            if (res.Settings != null)
            {
                if (Rules.Count == 0 && res.Settings.Rules != null && res.Settings.Rules.Count > 0)
                {
                    foreach (var r in res.Settings.Rules)
                    {
                        Rules.Add(r);
                    }
                    Settings.Rules = Rules.ToList();
                }

                if (string.IsNullOrWhiteSpace(TelegramBotToken) && !string.IsNullOrWhiteSpace(res.Settings.TelegramBotToken))
                {
                    TelegramBotToken = res.Settings.TelegramBotToken;
                    Settings.TelegramBotToken = TelegramBotToken;
                }
                if (string.IsNullOrWhiteSpace(TelegramChatId) && !string.IsNullOrWhiteSpace(res.Settings.TelegramChatId))
                {
                    TelegramChatId = res.Settings.TelegramChatId;
                    Settings.TelegramChatId = TelegramChatId;
                }
            }

            await _storageService.SaveSettingsAsync(Settings);
            UpdateQuotaState();
            if (!string.IsNullOrWhiteSpace(res.AccessToken))
            {
                _ = _realtimeService.ConnectAsync(AuthApiEndpoint, res.AccessToken);
                _ = SyncWithCloudAsync();
            }
            StatusNotification = $"เข้าสู่ระบบสำเร็จในชื่อ @{LoggedInUsername} (โควต้า {MaxTiktokAccounts} จอ)";
        }
        else
        {
            LoginErrorMessage = res.Message;
        }
    }

    [RelayCommand]
    public async Task SyncWithCloudAsync()
    {
        if (string.IsNullOrWhiteSpace(AuthToken)) return;

        try
        {
            // 1. Sync Profiles
            var profResult = await _authService.GetProfilesAsync(AuthToken, AuthApiEndpoint);
            if (profResult.Success)
            {
                var cloudProfiles = profResult.Profiles ?? new();
                bool localChanged = false;

                if (cloudProfiles.Count > 0)
                {
                    foreach (var cp in cloudProfiles)
                    {
                        var localProf = Settings.Instances.FirstOrDefault(p => 
                            string.Equals(p.ProfileFolderName, cp.ProfileFolderName, StringComparison.OrdinalIgnoreCase));

                        if (localProf == null)
                        {
                            var newProfile = new StudioInstanceProfile
                            {
                                Name = cp.Name,
                                ProfileFolderName = cp.ProfileFolderName,
                                CdpPort = cp.CdpPort > 0 ? cp.CdpPort : 9221,
                                LaunchMode = Enum.TryParse<InstanceLaunchMode>(cp.LaunchMode, out var lm) ? lm : InstanceLaunchMode.DirectProfile,
                                CustomChromiumFlags = cp.CustomChromiumFlags ?? string.Empty,
                                StreamerUsername = cp.StreamerUsername ?? string.Empty
                            };
                            if (Guid.TryParse(cp.ClientProfileId, out var parsedId)) newProfile.Id = parsedId;
                            if (Guid.TryParse(cp.ProxyId, out var parsedProxyId)) newProfile.ProxyId = parsedProxyId;
                            if (cp.TiktokAccount != null)
                            {
                                newProfile.IsLoggedIn = cp.TiktokAccount.IsLoggedIn;
                                newProfile.LoggedInUserId = cp.TiktokAccount.TiktokUid;
                                newProfile.LoggedInDisplayId = cp.TiktokAccount.DisplayId;
                                newProfile.LoggedInNickname = cp.TiktokAccount.Nickname;
                                newProfile.LoggedInAvatarUrl = cp.TiktokAccount.AvatarUrl;
                            }

                            RunOnUi(() =>
                            {
                                Settings.Instances.Add(newProfile);
                                var card = new InstanceCardViewModel(newProfile, Settings, _orchestrator, _cdpService, _profileManager, telegramService: _telegramService, liveListenerService: _liveListenerService, slotLeaseService: _slotLeaseService, authClientService: _authService);
                                AttachCardEvents(card);
                                Instances.Add(card);
                            });
                            localChanged = true;
                        }
                        else
                        {
                            if (localProf.Status == InstanceStatus.Stopped)
                            {
                                if (!string.IsNullOrWhiteSpace(cp.Name) && localProf.Name != cp.Name)
                                {
                                    localProf.Name = cp.Name;
                                    localChanged = true;
                                }
                                if (cp.TiktokAccount != null && cp.TiktokAccount.IsLoggedIn && !localProf.IsLoggedIn)
                                {
                                    localProf.IsLoggedIn = true;
                                    localProf.LoggedInUserId = cp.TiktokAccount.TiktokUid;
                                    localProf.LoggedInDisplayId = cp.TiktokAccount.DisplayId;
                                    localProf.LoggedInNickname = cp.TiktokAccount.Nickname;
                                    localProf.LoggedInAvatarUrl = cp.TiktokAccount.AvatarUrl;
                                    localChanged = true;
                                }
                            }
                        }
                    }
                }

                var missingInCloud = Settings.Instances
                    .Where(local => !cloudProfiles.Any(c => string.Equals(c.ProfileFolderName, local.ProfileFolderName, StringComparison.OrdinalIgnoreCase)))
                    .Select(local => new ProfileSyncDto
                    {
                        ClientProfileId = local.Id.ToString(),
                        Name = local.Name,
                        ProfileFolderName = local.ProfileFolderName,
                        CdpPort = local.CdpPort,
                        LaunchMode = local.LaunchMode.ToString(),
                        ProxyId = local.ProxyId?.ToString(),
                        StreamerUsername = local.StreamerUsername,
                        CustomChromiumFlags = local.CustomChromiumFlags,
                        TiktokAccount = new TikTokAccountSyncDto
                        {
                            IsLoggedIn = local.IsLoggedIn,
                            TiktokUid = local.LoggedInUserId,
                            DisplayId = local.LoggedInDisplayId,
                            Nickname = local.LoggedInNickname,
                            AvatarUrl = local.LoggedInAvatarUrl
                        }
                    }).ToList();

                if (missingInCloud.Count > 0)
                {
                    _ = _authService.SyncProfilesBatchAsync(AuthToken, missingInCloud, AuthApiEndpoint);
                }

                if (localChanged)
                {
                    await _storageService.SaveSettingsAsync(Settings);
                    UpdateQuotaState();
                    OnPropertyChanged(nameof(RunningInstancesCount));
                    OnPropertyChanged(nameof(RunningStatusSummary));
                }
            }

            // 2. Sync Proxies
            var proxyResult = await _authService.GetProxiesAsync(AuthToken, AuthApiEndpoint);
            if (proxyResult.Success)
            {
                var cloudProxies = proxyResult.Proxies ?? new();
                bool proxiesChanged = false;

                if (cloudProxies.Count > 0)
                {
                    foreach (var cp in cloudProxies)
                    {
                        var localProxy = Settings.Proxies.FirstOrDefault(p => p.ProxifierId == cp.ProxifierId || (p.Host == cp.Host && p.Port == cp.Port));
                        if (localProxy == null)
                        {
                            var newP = new ProxyItem
                            {
                                Name = cp.Name,
                                Host = cp.Host,
                                Port = cp.Port,
                                Protocol = string.Equals(cp.Protocol, "http", StringComparison.OrdinalIgnoreCase) ? ProxyProtocol.Http : ProxyProtocol.Socks5,
                                Username = cp.Username ?? string.Empty,
                                Password = cp.Password ?? string.Empty,
                                ProxifierId = cp.ProxifierId,
                                IsActive = cp.IsActive
                            };
                            if (Guid.TryParse(cp.ClientProxyId, out var pid)) newP.Id = pid;
                            RunOnUi(() =>
                            {
                                Proxies.Add(newP);
                                Settings.Proxies.Add(newP);
                            });
                            proxiesChanged = true;
                        }
                    }
                }

                var localProxiesToSync = Settings.Proxies.Select(p => new ProxySyncDto
                {
                    ClientProxyId = p.Id.ToString(),
                    Name = p.Name,
                    Host = p.Host,
                    Port = p.Port,
                    Protocol = p.Protocol.ToString().ToLowerInvariant(),
                    Username = p.Username,
                    Password = p.Password,
                    ProxifierId = p.ProxifierId,
                    IsActive = p.IsActive
                }).ToList();

                if (localProxiesToSync.Count > 0)
                {
                    _ = _authService.SyncProxiesBatchAsync(AuthToken, localProxiesToSync, AuthApiEndpoint);
                }

                if (proxiesChanged)
                {
                    await _storageService.SaveSettingsAsync(Settings);
                }
            }

            StatusNotification = $"ซิงค์ข้อมูลกับคลาวด์เรียบร้อย ({Instances.Count} จอ, {Proxies.Count} Proxy)";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CloudSync] Error during sync: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task LogoutAsync()
    {
        _ = _realtimeService.DisconnectAsync();
        AuthToken = string.Empty;
        Settings.AuthToken = string.Empty;
        Settings.RefreshToken = string.Empty;
        CaptchaSolverJwtToken = string.Empty;
        Settings.CaptchaSolverJwtToken = string.Empty;
        LoggedInUsername = string.Empty;
        Settings.LoggedInUsername = string.Empty;
        LicensePlan = "ยังไม่ได้เข้าสู่ระบบ";
        Settings.LicensePlan = LicensePlan;
        LicenseRole = string.Empty;
        LicenseExpiryText = "-";
        MaxTiktokAccounts = 1;
        Settings.MaxTiktokAccounts = 1;

        await _storageService.SaveSettingsAsync(Settings);
        UpdateQuotaState();
        StatusNotification = "ออกจากระบบเรียบร้อยแล้ว";
        RequestLogoutRedirect?.Invoke();
    }

    [RelayCommand]
    public async Task RefreshLicenseAsync()
    {
        if (string.IsNullOrWhiteSpace(AuthToken)) return;
        var res = await _authService.GetProfileAsync(AuthToken, AuthApiEndpoint);
        if (!res.Success && !string.IsNullOrWhiteSpace(Settings.RefreshToken))
        {
            var refreshRes = await _authService.RefreshTokenAsync(Settings.RefreshToken, AuthApiEndpoint);
            if (refreshRes.Success && !string.IsNullOrWhiteSpace(refreshRes.AccessToken))
            {
                AuthToken = refreshRes.AccessToken;
                Settings.AuthToken = refreshRes.AccessToken;
                CaptchaSolverJwtToken = refreshRes.AccessToken;
                Settings.CaptchaSolverJwtToken = refreshRes.AccessToken;
                if (!string.IsNullOrWhiteSpace(refreshRes.RefreshToken))
                {
                    Settings.RefreshToken = refreshRes.RefreshToken;
                }
                await _storageService.SaveSettingsAsync(Settings);
                _ = _realtimeService.ConnectAsync(AuthApiEndpoint, refreshRes.AccessToken);
                res = refreshRes;
            }
        }

        if (res.Success)
        {
            MaxTiktokAccounts = res.MaxTiktokAccounts;
            Settings.MaxTiktokAccounts = res.MaxTiktokAccounts;
            LicensePlan = res.Plan ?? LicensePlan;
            Settings.LicensePlan = LicensePlan;
            LicenseExpiryText = res.ExpiredAt.HasValue ? res.ExpiredAt.Value.ToString("dd/MM/yyyy HH:mm") : "ไม่มีวันหมดอายุ";
            LoginErrorMessage = string.Empty;
            await _storageService.SaveSettingsAsync(Settings);
            UpdateQuotaState();
            StatusNotification = $"อัปเดตสิทธิ์สำเร็จ: ได้รับโควต้า {MaxTiktokAccounts} จอ";
        }
        else
        {
            LoginErrorMessage = res.Message;
        }
    }

    private Timer? _licenseWatchdogTimer;
    private bool _isCheckingWatchdog;
    private readonly object _watchdogLock = new();

    public void StartLicenseWatchdog(TimeSpan? interval = null)
    {
        var period = interval ?? TimeSpan.FromSeconds(15);
        _licenseWatchdogTimer?.Dispose();
        _licenseWatchdogTimer = new Timer(async _ =>
        {
            try
            {
                await CheckLicenseAndQuotaWatchdogAsync();
            }
            catch { }
        }, null, period, period);
    }

    public void StopLicenseWatchdog()
    {
        _licenseWatchdogTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public async Task CheckLicenseAndQuotaWatchdogAsync()
    {
        if (string.IsNullOrWhiteSpace(AuthToken) || !IsUserLoggedIn) return;

        lock (_watchdogLock)
        {
            if (_isCheckingWatchdog) return;
            _isCheckingWatchdog = true;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // 1. Local expiry check
            if (Settings.LicenseExpiresAt.HasValue && DateTime.UtcNow > Settings.LicenseExpiresAt.Value)
            {
                System.Diagnostics.Debug.WriteLine($"[License/TTL] 🚨 Local license expiry detected: {Settings.LicenseExpiresAt.Value:dd/MM/yyyy HH:mm} UTC");
                await HandleLicenseExpiredKickoutAsync("ใบอนุญาตใช้งานของคุณหมดอายุแล้ว ระบบทำการปิดหน้าต่าง TikTok Studio ทั้งหมดและออกจากระบบทันที กรุณาติดต่อแอดมินเพื่อต่ออายุ (หรือขอขยายเวลา 2 ชม.)");
                return;
            }

            // 2. Query remote server for real-time license & quota status
            var res = await _authService.GetProfileAsync(AuthToken, AuthApiEndpoint);
            sw.Stop();
            double profileMs = sw.Elapsed.TotalMilliseconds;

            if (profileMs > 500)
            {
                System.Diagnostics.Debug.WriteLine($"[License/TTL] ⚠️ LAG WARNING: License watchdog profile query took {profileMs:F1}ms (> 500ms threshold)!");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[License/TTL] License profile queried in {profileMs:F1}ms (Success={res.Success})");
            }

            if (!res.Success)
            {
                // If token expired (e.g. 1h access token), try refreshing using refresh token first
                if (!string.IsNullOrWhiteSpace(Settings.RefreshToken))
                {
                    var refreshSw = System.Diagnostics.Stopwatch.StartNew();
                    var refreshRes = await _authService.RefreshTokenAsync(Settings.RefreshToken, AuthApiEndpoint);
                    refreshSw.Stop();
                    System.Diagnostics.Debug.WriteLine($"[License/TTL] Token refresh attempt finished in {refreshSw.Elapsed.TotalMilliseconds:F1}ms (Success={refreshRes.Success})");

                    if (refreshRes.Success && !string.IsNullOrWhiteSpace(refreshRes.AccessToken))
                    {
                        AuthToken = refreshRes.AccessToken;
                        Settings.AuthToken = refreshRes.AccessToken;
                        CaptchaSolverJwtToken = refreshRes.AccessToken;
                        Settings.CaptchaSolverJwtToken = refreshRes.AccessToken;
                        if (!string.IsNullOrWhiteSpace(refreshRes.RefreshToken))
                        {
                            Settings.RefreshToken = refreshRes.RefreshToken;
                        }
                        await _storageService.SaveSettingsAsync(Settings);
                        res = refreshRes;
                    }
                    else if (refreshRes.Message.Contains("LICENSE_EXPIRED") || refreshRes.Message.Contains("User inactive"))
                    {
                        await HandleLicenseExpiredKickoutAsync($"ใบอนุญาตของคุณหมดอายุแล้ว ({refreshRes.Message})\nระบบทำการปิดหน้าต่าง TikTok Studio ทั้งหมดและออกจากระบบทันที");
                        return;
                    }
                }

                if (!res.Success)
                {
                    if (res.Message.Contains("LICENSE_EXPIRED"))
                    {
                        await HandleLicenseExpiredKickoutAsync($"ใบอนุญาตของคุณหมดอายุแล้ว ({res.Message})\nระบบทำการปิดหน้าต่าง TikTok Studio ทั้งหมดและออกจากระบบทันที");
                        return;
                    }
                    // Transient network or server error - do not kick out
                    System.Diagnostics.Debug.WriteLine($"[License/TTL] ⚠️ Transient license watchdog error: {res.Message}");
                    return;
                }
            }
            else
            {
                // Verify remote expiry date
                if (res.ExpiredAt.HasValue && DateTime.UtcNow > res.ExpiredAt.Value)
                {
                    System.Diagnostics.Debug.WriteLine($"[License/TTL] 🚨 Remote server returned license expired at {res.ExpiredAt.Value:dd/MM/yyyy HH:mm} UTC");
                    await HandleLicenseExpiredKickoutAsync("ใบอนุญาตใช้งานของคุณหมดอายุแล้ว ระบบทำการปิดหน้าต่าง TikTok Studio ทั้งหมดและออกจากระบบทันที กรุณาติดต่อแอดมินเพื่อต่ออายุ (หรือขอขยายเวลา 2 ชม.)");
                    return;
                }

                // Update local expiration info
                Settings.LicenseExpiresAt = res.ExpiredAt;
                RunOnUi(() =>
                {
                    LicenseExpiryText = res.ExpiredAt.HasValue
                        ? res.ExpiredAt.Value.ToString("dd/MM/yyyy HH:mm")
                        : "ไม่มีวันหมดอายุ";
                });

                // Check quota reduction
                int serverMax = res.MaxTiktokAccounts;
                if (serverMax != MaxTiktokAccounts)
                {
                    System.Diagnostics.Debug.WriteLine($"[License/TTL] 🔄 Quota change detected: {MaxTiktokAccounts} -> {serverMax} screens");
                    RunOnUi(() =>
                    {
                        MaxTiktokAccounts = serverMax;
                        Settings.MaxTiktokAccounts = serverMax;
                        UpdateQuotaState();
                    });
                }

                // Real Quota Reduction Enforcement: If currently running instances exceed allowed quota, terminate excess screens immediately!
                var running = Instances.Where(i => i.Status == InstanceStatus.Running).ToList();
                if (running.Count > serverMax)
                {
                    int excessCount = running.Count - serverMax;
                    var excessInstances = running.TakeLast(excessCount).ToList();
                    System.Diagnostics.Debug.WriteLine($"[License/TTL] 🚨 Quota reduced ({running.Count} > {serverMax}). Terminating {excessCount} excess screens.");

                    foreach (var inst in excessInstances)
                    {
                        RunOnUi(async () =>
                        {
                            StatusNotification = $"โควต้าจอถูกลดเหลือ {serverMax} จอ สั่งปิดหน้าต่าง '{inst.Name}' ทันที";
                            await inst.StopAsync();
                        });
                    }

                    RunOnUi(() =>
                    {
                        MessageBox.Show(
                            $"โควต้าบัญชีของคุณถูกปรับลดลงเหลือ {serverMax} จอ\nระบบจึงทำการปิดหน้าต่าง TikTok Studio ส่วนเกินจำนวน {excessCount} จอโดยอัตโนมัติ",
                            "ปรับลดโควต้าบัญชี (Account Quota Reduced)",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning
                        );
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[License/TTL] Error in CheckLicenseAndQuotaWatchdogAsync: {ex.Message}");
        }
        finally
        {
            lock (_watchdogLock)
            {
                _isCheckingWatchdog = false;
            }
        }
    }

    private async Task HandleLicenseExpiredKickoutAsync(string message)
    {
        if (!string.IsNullOrWhiteSpace(AuthToken))
        {
            _ = _authService.ReportSecurityEventAsync(
                AuthToken,
                "LICENSE_EXPIRED_KICKOUT",
                message,
                CurrentMachineHwid ?? string.Empty,
                endpoint: AuthApiEndpoint
            );
        }

        RunOnUi(async () =>
        {
            StatusNotification = "ใบอนุญาตหมดอายุ กำลังปิดหน้าต่างและออกจากระบบ...";

            // 1. Stop all instance view models
            foreach (var inst in Instances.ToList())
            {
                if (inst.Status == InstanceStatus.Running)
                {
                    try { await inst.StopAsync(); } catch { }
                }
            }

            // 2. Kill all TikTok LIVE Studio processes just in case
            try
            {
                var procs = System.Diagnostics.Process.GetProcessesByName("TikTok LIVE Studio");
                foreach (var p in procs)
                {
                    try { p.Kill(true); } catch { }
                }
            }
            catch { }

            // 3. Force logout
            await LogoutAsync();

            // 4. Alert user
            MessageBox.Show(
                message,
                "ใบอนุญาตหมดอายุ (License Expired)",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        });
    }

    private void HandleRealtimeQuotaReduced(object? sender, QuotaReducedEventArgs e)
    {
        RunOnUi(async () =>
        {
            int serverMax = e.NewMax;
            if (serverMax != MaxTiktokAccounts)
            {
                MaxTiktokAccounts = serverMax;
                Settings.MaxTiktokAccounts = serverMax;
                await _storageService.SaveSettingsAsync(Settings);
                UpdateQuotaState();
            }

            var running = Instances.Where(i => i.Status == InstanceStatus.Running).ToList();
            if (running.Count > serverMax)
            {
                int excessCount = running.Count - serverMax;
                var excessInstances = running.TakeLast(excessCount).ToList();
                foreach (var inst in excessInstances)
                {
                    StatusNotification = $"โควต้าจอถูกลดเหลือ {serverMax} จอ สั่งปิดหน้าต่าง '{inst.Name}' ทันที";
                    try { await inst.StopAsync(); } catch { }
                }

                MessageBox.Show(
                    $"โควต้าบัญชีของคุณถูกปรับลดลงเหลือ {serverMax} จอ\nระบบทำการปิดหน้าต่าง TikTok Studio ส่วนเกินจำนวน {excessCount} จอเรียบร้อยแล้ว",
                    "ปรับลดโควต้าบัญชี (Quota Reduced)",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
        });
    }

    private void HandleRealtimeLicenseKickout(object? sender, LicenseKickoutEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(AuthToken))
        {
            _ = _authService.ReportSecurityEventAsync(
                AuthToken,
                "REALTIME_LICENSE_KICKOUT",
                e.Reason,
                CurrentMachineHwid ?? string.Empty,
                endpoint: AuthApiEndpoint
            );
        }
        _ = HandleLicenseExpiredKickoutAsync($"บัญชีของคุณถูกระงับหรือสิ้นสุดการใช้งาน ({e.Reason})\nระบบทำการปิดหน้าต่าง TikTok Studio ทั้งหมดและออกจากระบบ");
    }

    private void HandleRealtimeLicenseUpdated(object? sender, LicenseUpdateEventArgs e)
    {
        RunOnUi(async () =>
        {
            if (e.ExpiredAt.HasValue)
            {
                Settings.LicenseExpiresAt = e.ExpiredAt;
                LicenseExpiryText = e.ExpiredAt.Value.ToString("dd/MM/yyyy HH:mm");
            }
            if (e.MaxTiktokAccounts.HasValue && e.MaxTiktokAccounts.Value != MaxTiktokAccounts)
            {
                MaxTiktokAccounts = e.MaxTiktokAccounts.Value;
                Settings.MaxTiktokAccounts = MaxTiktokAccounts;
            }
            if (!string.IsNullOrEmpty(e.Plan))
            {
                LicensePlan = e.Plan;
                Settings.LicensePlan = e.Plan;
            }
            await _storageService.SaveSettingsAsync(Settings);
            UpdateQuotaState();
            StatusNotification = "ได้รับการอัปเดตสิทธิ์ใบอนุญาตเรียบร้อยแล้ว";
        });
    }

    private void HandleRealtimeAppUpdateBroadcast(object? sender, AppUpdateEventArgs e)
    {
        RunOnUi(() =>
        {
            if (string.IsNullOrWhiteSpace(e.Version)) return;

            string currentVerStr = AppConstants.CurrentVersion;
            
            // If already up-to-date and not below min required version, NEVER PROMPT!
            if (!VersionHelper.ShouldPromptUpdate(e.Version, e.MinRequiredVersion, currentVerStr, e.IsMandatory))
            {
                return;
            }

            bool isBelowMin = VersionHelper.IsVersionBelowMin(e.MinRequiredVersion, currentVerStr);
            bool forceUpdate = e.IsMandatory || isBelowMin;

            string targetUrl = e.DownloadUrl;
            if (string.IsNullOrWhiteSpace(targetUrl))
            {
                targetUrl = $"{AppConstants.DefaultServerEndpoint.TrimEnd('/')}/api/v1/app/download";
            }
            else if (targetUrl.StartsWith("/"))
            {
                targetUrl = $"{AppConstants.DefaultServerEndpoint.TrimEnd('/')}{targetUrl}";
            }

            var dialog = new UpdateDialog(
                currentVersion: currentVerStr,
                newVersion: e.Version,
                title: e.Title ?? string.Empty,
                description: e.Description ?? string.Empty,
                isMandatory: forceUpdate,
                downloadUrl: targetUrl
            );

            bool? result = dialog.ShowDialog();

            if (result == true)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = targetUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    StatusNotification = $"เปิดลิงก์ดาวน์โหลดไม่สำเร็จ: {ex.Message}";
                }

                if (forceUpdate)
                {
                    Application.Current?.Shutdown();
                }
            }
            else if (forceUpdate)
            {
                Application.Current?.Shutdown();
            }
        });
    }

    public async Task CheckLatestVersionOnStartupAsync()
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            string url = $"{AppConstants.DefaultServerEndpoint.TrimEnd('/')}/api/v1/app/latest-release";
            var resp = await httpClient.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return;

            string json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            JsonElement data = root;
            if (root.TryGetProperty("data", out var dProp) && dProp.ValueKind == JsonValueKind.Object)
            {
                data = dProp;
            }
            else if (root.TryGetProperty("release", out var rProp) && rProp.ValueKind == JsonValueKind.Object)
            {
                data = rProp;
            }

            string ver = data.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            string minVer = data.TryGetProperty("minRequiredVersion", out var mv) && mv.ValueKind == JsonValueKind.String ? mv.GetString() ?? "" : "";
            string title = data.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
            string desc = data.TryGetProperty("description", out var ds) && ds.ValueKind == JsonValueKind.String ? ds.GetString() ?? "" : "";
            string dlUrl = data.TryGetProperty("downloadUrl", out var du) && du.ValueKind == JsonValueKind.String ? du.GetString() ?? "" : "";
            bool isMandatory = data.TryGetProperty("isMandatory", out var im) && (im.ValueKind == JsonValueKind.True || (im.ValueKind == JsonValueKind.String && im.GetString() == "true"));

            if (!string.IsNullOrWhiteSpace(ver))
            {
                HandleRealtimeAppUpdateBroadcast(this, new AppUpdateEventArgs
                {
                    Version = ver,
                    MinRequiredVersion = minVer,
                    Title = title,
                    Description = desc,
                    DownloadUrl = dlUrl,
                    IsMandatory = isMandatory
                });
            }
        }
        catch { }
    }

    private void RunOnUi(Action action)
    {
        if (Application.Current != null && Application.Current.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }

    [RelayCommand]
    public async Task TestTelegramAsync()
    {
        TelegramTestStatus = "กำลังทดสอบส่งข้อความแจ้งเตือนเข้า Telegram...";
        var (ok, msg) = await _telegramService.TestConnectionAsync(TelegramBotToken, TelegramChatId);
        TelegramTestStatus = msg;
        StatusNotification = ok ? "ทดสอบ Telegram สำเร็จ" : "ทดสอบ Telegram ไม่สำเร็จ";
    }

    [RelayCommand]
    public async Task TestSolverApiAsync()
    {
        SolverApiTestStatus = "กำลังทดสอบเชื่อมต่อ Captcha Solver Server...";
        var (ok, msg) = await _cdpService.TestSolverApiConnectionAsync(AppConstants.DefaultServerEndpoint, CaptchaSolverJwtToken);
        SolverApiTestStatus = msg;
        StatusNotification = ok ? "เชื่อมต่อ Solver API สำเร็จ" : "เชื่อมต่อ Solver API ไม่สำเร็จ";
    }

    [RelayCommand]
    public void TestSound()
    {
        AudioAlertService.PlayAlert(CaptchaSoundVolume, CaptchaSoundFilePath, LoopCaptchaSoundAlert);
        StatusNotification = LoopCaptchaSoundAlert
            ? $"กำลังทดสอบเสียงเตือนแบบ Loop (ความดัง {CaptchaSoundVolume}%)... กดปุ่มหยุดเพื่อหยุดเสียง"
            : $"เล่นเสียงทดสอบความดัง {CaptchaSoundVolume}% เรียบร้อย";
    }

    [RelayCommand]
    public void StopSound()
    {
        AudioAlertService.StopAlert();
        StatusNotification = "ปิดเสียงแจ้งเตือนเรียบร้อย";
    }

    [RelayCommand]
    public void BrowseSoundFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "เลือกไฟล์เสียงแจ้งเตือน Captcha",
            Filter = "Audio Files (*.wav;*.mp3)|*.wav;*.mp3|WAV Files (*.wav)|*.wav|MP3 Files (*.mp3)|*.mp3|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            CaptchaSoundFilePath = dialog.FileName;
            Settings.CaptchaSoundFilePath = dialog.FileName;
            _storageService.SaveSettings(Settings);
            StatusNotification = $"เลือกไฟล์เสียง: {Path.GetFileName(dialog.FileName)}";
        }
    }

    [RelayCommand]
    public void BrowseSaveDirectory()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "เลือกโฟลเดอร์สำหรับบันทึกภาพ Captcha"
        };

        if (dialog.ShowDialog() == true)
        {
            CaptchaSaveDirectory = dialog.FolderName;
            Settings.CaptchaSaveDirectory = dialog.FolderName;
            _storageService.SaveSettings(Settings);
            StatusNotification = $"เปลี่ยนโฟลเดอร์บันทึกภาพ: {dialog.FolderName}";
        }
    }

    [RelayCommand]
    public void OpenCaptchaFolder()
    {
        try
        {
            string dir = !string.IsNullOrWhiteSpace(CaptchaSaveDirectory)
                ? CaptchaSaveDirectory
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPictures), "TikTokStudioCaptures");

            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
            StatusNotification = "เปิดโฟลเดอร์ภาพ Captcha เรียบร้อย";
        }
        catch (Exception ex)
        {
            StatusNotification = $"เปิดโฟลเดอร์ไม่สำเร็จ: {ex.Message}";
        }
    }

    [RelayCommand]
    public void OpenLogsFolder()
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FlowUltraTech",
                "Logs");

            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
            StatusNotification = "เปิดโฟลเดอร์ Logs เรียบร้อย";
        }
        catch (Exception ex)
        {
            StatusNotification = $"เปิดโฟลเดอร์ Log ไม่สำเร็จ: {ex.Message}";
        }
    }
}
