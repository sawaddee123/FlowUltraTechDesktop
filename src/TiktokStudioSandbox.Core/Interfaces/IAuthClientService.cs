using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TiktokStudioSandbox.Core.Models;

namespace TiktokStudioSandbox.Core.Interfaces;

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

public interface IAuthClientService
{
    Task<LoginResult> LoginAsync(string username, string password, string hwid, string? endpoint = null);
    Task<LoginResult> GetProfileAsync(string accessToken, string? endpoint = null);
    Task<LoginResult> RefreshTokenAsync(string refreshToken, string? endpoint = null);
    Task<CloudSettingsResult> GetCloudSettingsAsync(string accessToken, string? endpoint = null);
    Task<bool> SaveCloudSettingsAsync(string accessToken, CloudSettingsDto settings, string? endpoint = null);
    Task<bool> ReportSecurityEventAsync(string accessToken, string eventType, string details, string hwid, string? profileFolder = null, string? severity = null, string? endpoint = null);
}
