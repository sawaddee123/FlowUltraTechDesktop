using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Interfaces;

public class AppVersionInfo
{
    public string LatestVersion { get; set; } = "0.1.0";
    public string MinRequiredVersion { get; set; } = "0.1.0";
    public string DownloadUrl { get; set; } = string.Empty;
}

public class CloudSettingsDto
{
    public string? TelegramBotToken { get; set; }
    public string? TelegramChatId { get; set; }
    public int? AutoSolveDelaySeconds { get; set; }
    public int? CaptchaSoundVolume { get; set; }
    public bool? AutoSaveCaptchaImage { get; set; }
    public List<AutomationRule>? Rules { get; set; }
}

public class CloudSettingsResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public CloudSettingsDto? Settings { get; set; }
}

public class LoginResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public string? Username { get; set; }
    public string? Role { get; set; }
    public int MaxTiktokAccounts { get; set; } = 1;
    public string? Plan { get; set; }
    public DateTime? ExpiredAt { get; set; }
    public AppVersionInfo? AppVersion { get; set; }
    public CloudSettingsDto? Settings { get; set; }
}

public class TikTokAccountSyncDto
{
    public bool IsLoggedIn { get; set; }
    public string? TiktokUid { get; set; }
    public string? DisplayId { get; set; }
    public string? Nickname { get; set; }
    public string? AvatarUrl { get; set; }
}

public class ProfileSyncDto
{
    public string? ClientProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProfileFolderName { get; set; } = string.Empty;
    public int CdpPort { get; set; } = 9221;
    public string LaunchMode { get; set; } = "DirectProfile";
    public string? ProxyId { get; set; }
    public string? StreamerUsername { get; set; }
    public string? CustomChromiumFlags { get; set; }
    public TikTokAccountSyncDto? TiktokAccount { get; set; }
    public List<AutomationRule>? Rules { get; set; }
}

public class ProxySyncDto
{
    public string? ClientProxyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 1080;
    public string Protocol { get; set; } = "socks5";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public int ProxifierId { get; set; } = 100;
    public bool IsActive { get; set; } = true;
}

public class ProfileSyncResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<ProfileSyncDto> Profiles { get; set; } = new();
}

public class ProxySyncResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<ProxySyncDto> Proxies { get; set; } = new();
}

public interface IAuthClientService
{
    Task<LoginResult> LoginAsync(string username, string password, string hwid, string? endpoint = null);
    Task<LoginResult> GetProfileAsync(string accessToken, string? endpoint = null);
    Task<LoginResult> RefreshTokenAsync(string refreshToken, string? endpoint = null);
    Task<CloudSettingsResult> GetCloudSettingsAsync(string accessToken, string? endpoint = null);
    Task<bool> SaveCloudSettingsAsync(string accessToken, CloudSettingsDto settings, string? endpoint = null);
    Task<bool> ReportSecurityEventAsync(string accessToken, string eventType, string details, string hwid, string? profileFolder = null, string? severity = null, string? endpoint = null);

    // Profile Cloud Sync
    Task<ProfileSyncResult> GetProfilesAsync(string accessToken, string? endpoint = null);
    Task<bool> UpsertProfileAsync(string accessToken, ProfileSyncDto profile, string? endpoint = null);
    Task<bool> DeleteProfileAsync(string accessToken, string profileFolderName, string? endpoint = null);
    Task<ProfileSyncResult> SyncProfilesBatchAsync(string accessToken, List<ProfileSyncDto> profiles, string? endpoint = null);
    Task<bool> UpdateTikTokAccountIdentityAsync(string accessToken, string profileFolderName, TikTokAccountSyncDto account, string? endpoint = null);

    // Proxy Cloud Sync
    Task<ProxySyncResult> GetProxiesAsync(string accessToken, string? endpoint = null);
    Task<ProxySyncResult> SyncProxiesBatchAsync(string accessToken, List<ProxySyncDto> proxies, string? endpoint = null);
    Task<bool> UpsertProxyAsync(string accessToken, ProxySyncDto proxy, string? endpoint = null);
    Task<bool> DeleteProxyAsync(string accessToken, string identifier, string? endpoint = null);
}
