using System.Collections.Generic;

namespace TiktokStudioSandbox.Core.Models;

public class AppSettings
{
    public string TikTokStudioExePath { get; set; } = @"C:\Program Files\TikTok LIVE Studio\1.31.0\TikTok LIVE Studio.exe";
    public string ProxifierExePath { get; set; } = @"C:\Program Files (x86)\Proxifier\Proxifier.exe";
    public string ProxifierProfilePath { get; set; } = string.Empty;

    public string DefaultChromiumFlags { get; set; } = string.Empty;

    public bool AutoHideOnLaunch { get; set; } = false;
    public bool AutoConnectCdp { get; set; } = true;

    // Captcha & Sound Notification Settings
    public bool EnableCaptchaSoundAlert { get; set; } = true;
    public bool LoopCaptchaSoundAlert { get; set; } = true;
    public int CaptchaSoundVolume { get; set; } = 80;
    public string CaptchaSoundFilePath { get; set; } = string.Empty;
    public string CaptchaSaveDirectory { get; set; } = string.Empty;
    public bool AutoSaveCaptchaImage { get; set; } = true;
    public bool AutoSolveCaptcha { get; set; } = true;
    public int AutoSolveDelaySeconds { get; set; } = 5;

    // Captcha Solver API Settings (SimpleSolveCaptchaProject)
    public CaptchaSolverType CaptchaSolverMode { get; set; } = CaptchaSolverType.LocalApiServer;
    public string CaptchaSolverEndpoint { get; set; } = AppConstants.DefaultServerEndpoint;
    public string CaptchaSolverJwtToken { get; set; } = string.Empty;

    // Slot Lease, License Quota & Central Auth Settings
    public string AuthApiEndpoint { get; set; } = AppConstants.DefaultServerEndpoint;
    public string AuthToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int MaxTiktokAccounts { get; set; } = 1;

    // Account & License Details
    public string LoggedInUsername { get; set; } = string.Empty;
    public string LicenseRole { get; set; } = string.Empty;
    public string LicensePlan { get; set; } = string.Empty;
    public System.DateTime? LicenseExpiresAt { get; set; }

    // Telegram Bot Notifications
    public bool EnableTelegramAlert { get; set; } = false;
    public string TelegramBotToken { get; set; } = string.Empty;
    public string TelegramChatId { get; set; } = string.Empty;
    public bool TelegramAlertOnDetected { get; set; } = true;
    public bool TelegramAlertOnSolved { get; set; } = true;
    public bool TelegramAlertOnFailed { get; set; } = true;
    public bool TelegramSendPhoto { get; set; } = true;

    public List<StudioInstanceProfile> Instances { get; set; } = new();
    public List<ProxyItem> Proxies { get; set; } = new();
    public List<AutomationRule> Rules { get; set; } = new();
}
