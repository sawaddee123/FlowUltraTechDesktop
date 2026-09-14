using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TiktokStudioSandbox.Core.Interfaces;
using TiktokStudioSandbox.Core.Models;

namespace TiktokStudioSandbox.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IConfigStorageService _storageService;
    private readonly IInstanceOrchestrator _orchestrator;
    private readonly IProxifierService _proxifierService;
    private readonly ICdpAutomationService _cdpService;
    private readonly IProfileManagerService _profileManager;

    public AppSettings Settings { get; }

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

    public MainViewModel(
        IConfigStorageService storageService,
        IInstanceOrchestrator orchestrator,
        IProxifierService proxifierService,
        ICdpAutomationService cdpService,
        IProfileManagerService? profileManager = null)
    {
        _storageService = storageService;
        _orchestrator = orchestrator;
        _proxifierService = proxifierService;
        _cdpService = cdpService;
        _profileManager = profileManager ?? new Core.Services.ProfileManagerService();
        _debug = new DebugViewModel(cdpService);

        Settings = _storageService.LoadSettings();

        StudioExePath = Settings.TikTokStudioExePath;
        ProxifierExePath = Settings.ProxifierExePath;
        ProxifierProfilePath = Settings.ProxifierProfilePath;
        AutoHideOnLaunch = Settings.AutoHideOnLaunch;

        foreach (var inst in Settings.Instances)
        {
            Instances.Add(new InstanceCardViewModel(inst, Settings, _orchestrator, _cdpService, _profileManager));
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
    }

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
            StatusNotification = "⚠️ ไม่มีบัญชีใดเปิดทำงานอยู่ (กรุณากด Start บัญชีก่อนสแกน)";
        }
        else
        {
            StatusNotification = foundCount > 0
                ? $"🚨 สแกนเสร็จสิ้น: ตรวจพบ Captcha ใน {foundCount} บัญชี!"
                : $"🟢 สแกนเสร็จสิ้น ({runningCount} บัญชี): ไม่พบ Captcha ในบัญชีใดๆ";
        }
    }

    [RelayCommand]
    public async Task StartAllAsync()
    {
        StatusNotification = "กำลังเริ่มทุก Instance...";
        foreach (var card in Instances)
        {
            await card.StartAsync();
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
        foreach (var card in Instances)
        {
            if (!card.IsWindowHidden && card.Status == InstanceStatus.Running)
            {
                card.ToggleVisibility();
            }
        }
        StatusNotification = "ซ่อนหน้าต่างทั้งหมด (Pseudo-Headless Active)";
    }

    [RelayCommand]
    public void ShowAllWindows()
    {
        foreach (var card in Instances)
        {
            if (card.IsWindowHidden && card.Status == InstanceStatus.Running)
            {
                card.ToggleVisibility();
            }
        }
        StatusNotification = "แสดงหน้าต่างทั้งหมดเรียบร้อย";
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
        Instances.Add(new InstanceCardViewModel(newProfile, Settings, _orchestrator, _cdpService, _profileManager));
        _storageService.SaveSettings(Settings);
        StatusNotification = $"เพิ่ม {newProfile.Name} (พอร์ต {newProfile.CdpPort})";
    }

    [RelayCommand]
    public async Task RemoveInstanceAsync(InstanceCardViewModel? card)
    {
        if (card == null) return;
        await card.StopAsync();
        Instances.Remove(card);
        Settings.Instances.Remove(card.Profile);
        _storageService.SaveSettings(Settings);
        StatusNotification = $"ลบ {card.Name} เรียบร้อยแล้ว";
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
    }

    [RelayCommand]
    public void RemoveProxy(ProxyItem? proxy)
    {
        if (proxy == null) return;
        Proxies.Remove(proxy);
        Settings.Proxies.Remove(proxy);
        _storageService.SaveSettings(Settings);
        StatusNotification = $"ลบ {proxy.Name} เรียบร้อยแล้ว";
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

        await _storageService.SaveSettingsAsync(Settings);
        StatusNotification = "บันทึกการตั้งค่าเรียบร้อยแล้ว";
    }
}
