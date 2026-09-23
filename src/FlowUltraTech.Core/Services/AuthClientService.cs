using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FlowUltraTech.Core.Interfaces;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Services;

public class AuthClientService : IAuthClientService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public string DefaultBaseEndpoint { get; set; } = AppConstants.DefaultServerEndpoint;

    public AuthClientService(HttpClient? httpClient = null, string? defaultBaseEndpoint = null)
    {
        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _ownsHttpClient = true;
        }

        if (!string.IsNullOrWhiteSpace(defaultBaseEndpoint))
        {
            DefaultBaseEndpoint = defaultBaseEndpoint.TrimEnd('/');
        }
    }

    public async Task<LoginResult> LoginAsync(string username, string password, string hwid, string? endpoint = null)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return new LoginResult { Success = false, Message = "กรุณากรอกชื่อผู้ใช้และรหัสผ่าน" };
        }

        var baseUrl = (endpoint ?? DefaultBaseEndpoint).TrimEnd('/');
        var url = $"{baseUrl}/api/v1/auth/login";

        var payload = new
        {
            username = username.Trim(),
            password,
            hwid
        };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            using var resp = await _httpClient.SendAsync(req);
            string content = await resp.Content.ReadAsStringAsync();

            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                string? accessToken = root.TryGetProperty("accessToken", out var at) ? at.GetString() : null;
                string? refreshToken = root.TryGetProperty("refreshToken", out var rt) ? rt.GetString() : null;
                string? user = root.TryGetProperty("username", out var un) ? un.GetString() : username;
                string? role = root.TryGetProperty("role", out var rl) ? rl.GetString() : "user";
                int maxAccounts = root.TryGetProperty("maxTiktokAccounts", out var ma) ? ma.GetInt32() : 1;

                string plan = "standard";
                DateTime? expiredAt = null;

                if (root.TryGetProperty("license", out var lic))
                {
                    if (lic.TryGetProperty("plan", out var p)) plan = p.GetString() ?? "standard";
                    if (lic.TryGetProperty("expiredAt", out var exp) && exp.TryGetDateTime(out var dt)) expiredAt = dt;
                    if (lic.TryGetProperty("maxTiktokAccounts", out var lma)) maxAccounts = lma.GetInt32();
                }

                var appVersion = ParseAppVersion(root);
                var cloudSettings = ParseCloudSettingsFromRoot(root);

                return new LoginResult
                {
                    Success = true,
                    Message = "เข้าสู่ระบบสำเร็จ",
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    Username = user,
                    Role = role,
                    MaxTiktokAccounts = maxAccounts,
                    Plan = plan,
                    ExpiredAt = expiredAt,
                    AppVersion = appVersion,
                    Settings = cloudSettings
                };
            }
            else
            {
                string errMsg = "เข้าสู่ระบบไม่สำเร็จ";
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("error", out var err)) errMsg = err.GetString() ?? errMsg;
                    else if (doc.RootElement.TryGetProperty("message", out var msg)) errMsg = msg.GetString() ?? errMsg;
                }
                catch { }

                return new LoginResult { Success = false, Message = errMsg };
            }
        }
        catch (Exception ex)
        {
            return new LoginResult { Success = false, Message = $"ไม่สามารถเชื่อมต่อเซิร์ฟเวอร์: {ex.Message}" };
        }
    }

    public async Task<LoginResult> GetProfileAsync(string accessToken, string? endpoint = null)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new LoginResult { Success = false, Message = "Token ว่างเปล่า" };
        }

        var baseUrl = (endpoint ?? DefaultBaseEndpoint).TrimEnd('/');
        var url = $"{baseUrl}/api/v1/auth/me";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());

            using var resp = await _httpClient.SendAsync(req);
            string content = await resp.Content.ReadAsStringAsync();

            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                string? user = root.TryGetProperty("username", out var un) ? un.GetString() : string.Empty;
                string? role = root.TryGetProperty("role", out var rl) ? rl.GetString() : "user";
                int maxAccounts = root.TryGetProperty("maxTiktokAccounts", out var ma) ? ma.GetInt32() : 1;

                string plan = "standard";
                DateTime? expiredAt = null;

                if (root.TryGetProperty("license", out var lic))
                {
                    if (lic.TryGetProperty("plan", out var p)) plan = p.GetString() ?? "standard";
                    if (lic.TryGetProperty("expiredAt", out var exp) && exp.TryGetDateTime(out var dt)) expiredAt = dt;
                    if (lic.TryGetProperty("maxTiktokAccounts", out var lma)) maxAccounts = lma.GetInt32();
                }

                var appVersion = ParseAppVersion(root);
                var cloudSettings = ParseCloudSettingsFromRoot(root);

                return new LoginResult
                {
                    Success = true,
                    Message = "ดึงข้อมูลสิทธิ์สำเร็จ",
                    AccessToken = accessToken,
                    Username = user,
                    Role = role,
                    MaxTiktokAccounts = maxAccounts,
                    Plan = plan,
                    ExpiredAt = expiredAt,
                    AppVersion = appVersion,
                    Settings = cloudSettings
                };
            }
            else
            {
                string errMsg = "Token หมดอายุหรือไม่ถูกต้อง";
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                    {
                        errMsg = err.GetString() ?? errMsg;
                    }
                    else if (doc.RootElement.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                    {
                        errMsg = msg.GetString() ?? errMsg;
                    }
                }
                catch { }

                return new LoginResult { Success = false, Message = errMsg };
            }
        }
        catch (Exception ex)
        {
            return new LoginResult { Success = false, Message = $"เกิดข้อผิดพลาด: {ex.Message}" };
        }
    }

    public async Task<LoginResult> RefreshTokenAsync(string refreshToken, string? endpoint = null)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return new LoginResult { Success = false, Message = "Refresh token ว่างเปล่า" };
        }

        var baseUrl = (endpoint ?? DefaultBaseEndpoint).TrimEnd('/');
        var url = $"{baseUrl}/api/v1/auth/refreshToken";

        try
        {
            var payload = new { refreshToken = refreshToken.Trim() };
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            using var resp = await _httpClient.SendAsync(req);
            string content = await resp.Content.ReadAsStringAsync();

            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                string? newAccessToken = root.TryGetProperty("accessToken", out var at) ? at.GetString() : null;
                string? newRefreshToken = root.TryGetProperty("refreshToken", out var rt) ? rt.GetString() : refreshToken;

                if (!string.IsNullOrWhiteSpace(newAccessToken))
                {
                    var profile = await GetProfileAsync(newAccessToken, baseUrl);
                    if (profile.Success)
                    {
                        profile.RefreshToken = newRefreshToken;
                        return profile;
                    }
                }

                return new LoginResult
                {
                    Success = true,
                    Message = "รีเฟรช Token สำเร็จ",
                    AccessToken = newAccessToken,
                    RefreshToken = newRefreshToken
                };
            }
            else
            {
                string errMsg = "Refresh token หมดอายุหรือไม่ถูกต้อง";
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                        errMsg = err.GetString() ?? errMsg;
                    else if (doc.RootElement.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                        errMsg = msg.GetString() ?? errMsg;
                }
                catch { }

                return new LoginResult { Success = false, Message = errMsg };
            }
        }
        catch (Exception ex)
        {
            return new LoginResult { Success = false, Message = $"ไม่สามารถเชื่อมต่อเซิร์ฟเวอร์เพื่อรีเฟรช Token: {ex.Message}" };
        }
    }

    public async Task<CloudSettingsResult> GetCloudSettingsAsync(string accessToken, string? endpoint = null)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new CloudSettingsResult { Success = false, Message = "Token ว่างเปล่า" };
        }

        var baseUrl = (endpoint ?? DefaultBaseEndpoint).TrimEnd('/');
        var url = $"{baseUrl}/api/v1/auth/settings";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());

            using var resp = await _httpClient.SendAsync(req);
            string content = await resp.Content.ReadAsStringAsync();

            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("settings", out var st) && st.ValueKind == JsonValueKind.Object)
                {
                    var settings = ParseCloudSettings(st);
                    return new CloudSettingsResult { Success = true, Settings = settings, Message = "ดึงข้อมูลการตั้งค่าสำเร็จ" };
                }
                return new CloudSettingsResult { Success = true, Settings = new CloudSettingsDto(), Message = "ไม่พบการตั้งค่าบนคลาวด์" };
            }
            return new CloudSettingsResult { Success = false, Message = $"ไม่สามารถดึงการตั้งค่า: HTTP {resp.StatusCode}" };
        }
        catch (Exception ex)
        {
            return new CloudSettingsResult { Success = false, Message = $"เกิดข้อผิดพลาด: {ex.Message}" };
        }
    }

    public async Task<bool> SaveCloudSettingsAsync(string accessToken, CloudSettingsDto settings, string? endpoint = null)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || settings == null)
        {
            return false;
        }

        var baseUrl = (endpoint ?? DefaultBaseEndpoint).TrimEnd('/');
        var url = $"{baseUrl}/api/v1/auth/settings";

        try
        {
            var payload = new
            {
                settings = new
                {
                    telegramBotToken = settings.TelegramBotToken,
                    telegramChatId = settings.TelegramChatId,
                    autoSolveDelaySeconds = settings.AutoSolveDelaySeconds,
                    captchaSoundVolume = settings.CaptchaSoundVolume,
                    autoSaveCaptchaImage = settings.AutoSaveCaptchaImage,
                    rules = settings.Rules
                }
            };

            using var req = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());

            using var resp = await _httpClient.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ReportSecurityEventAsync(
        string accessToken,
        string eventType,
        string details,
        string hwid,
        string? profileFolder = null,
        string? severity = null,
        string? endpoint = null)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(eventType))
        {
            return false;
        }

        var baseUrl = (endpoint ?? DefaultBaseEndpoint).TrimEnd('/');
        var url = $"{baseUrl}/api/v1/security/event";

        try
        {
            var payload = new
            {
                eventType,
                details,
                hwid,
                profileFolder,
                severity = severity ?? "medium"
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());

            using var resp = await _httpClient.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static AppVersionInfo? ParseAppVersion(JsonElement root)
    {
        if (root.TryGetProperty("appVersion", out var ver) && ver.ValueKind == JsonValueKind.Object)
        {
            return new AppVersionInfo
            {
                LatestVersion = ver.TryGetProperty("latestVersion", out var lv) && lv.ValueKind == JsonValueKind.String ? lv.GetString() ?? "0.1.0" : "0.1.0",
                MinRequiredVersion = ver.TryGetProperty("minRequiredVersion", out var mv) && mv.ValueKind == JsonValueKind.String ? mv.GetString() ?? "0.1.0" : "0.1.0",
                DownloadUrl = ver.TryGetProperty("downloadUrl", out var du) && du.ValueKind == JsonValueKind.String ? du.GetString() ?? string.Empty : string.Empty,
            };
        }
        return null;
    }

    private static CloudSettingsDto? ParseCloudSettingsFromRoot(JsonElement root)
    {
        if (root.TryGetProperty("settings", out var st) && st.ValueKind == JsonValueKind.Object)
        {
            return ParseCloudSettings(st);
        }
        if (root.TryGetProperty("setting", out var s) && s.ValueKind == JsonValueKind.Object)
        {
            return ParseCloudSettings(s);
        }
        return null;
    }

    private static CloudSettingsDto ParseCloudSettings(JsonElement element)
    {
        var dto = new CloudSettingsDto();
        if (element.TryGetProperty("telegramBotToken", out var tbt) && tbt.ValueKind == JsonValueKind.String)
            dto.TelegramBotToken = tbt.GetString();
        if (element.TryGetProperty("telegramChatId", out var tci) && tci.ValueKind == JsonValueKind.String)
            dto.TelegramChatId = tci.GetString();
        if (element.TryGetProperty("autoSolveDelaySeconds", out var asd) && asd.TryGetInt32(out var asdVal))
            dto.AutoSolveDelaySeconds = asdVal;
        if (element.TryGetProperty("captchaSoundVolume", out var csv) && csv.TryGetInt32(out var csvVal))
            dto.CaptchaSoundVolume = csvVal;
        if (element.TryGetProperty("autoSaveCaptchaImage", out var asc) && (asc.ValueKind == JsonValueKind.True || asc.ValueKind == JsonValueKind.False))
            dto.AutoSaveCaptchaImage = asc.GetBoolean();
        if (element.TryGetProperty("rules", out var rls) && rls.ValueKind == JsonValueKind.Array)
        {
            try
            {
                dto.Rules = JsonSerializer.Deserialize<List<AutomationRule>>(rls.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { }
        }
        return dto;
    }

    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    public async Task<ProfileSyncResult> GetProfilesAsync(string accessToken, string? endpoint = null)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return new ProfileSyncResult { Success = false, Message = "Token is missing" };
        var baseUrl = (endpoint ?? DefaultBaseEndpoint).TrimEnd('/');
        var url = $"{baseUrl}/api/v1/profiles";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
            using var resp = await _httpClient.SendAsync(req);
            string content = await resp.Content.ReadAsStringAsync();
            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                var list = new List<ProfileSyncDto>();
                if (root.TryGetProperty("profiles", out var profs) && profs.ValueKind == JsonValueKind.Array)
                {
                    list = JsonSerializer.Deserialize<List<ProfileSyncDto>>(profs.GetRawText(), _jsonOpts) ?? new();
                }
                return new ProfileSyncResult { Success = true, Profiles = list };
            }
            return new ProfileSyncResult { Success = false, Message = $"HTTP {resp.StatusCode}" };
        }
        catch (Exception ex)
        {
            return new ProfileSyncResult { Success = false, Message = ex.Message };
        }
    }

    public async Task<bool> UpsertProfileAsync(string accessToken, ProfileSyncDto profile, string? endpoint = null)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || profile == null) return false;
        var baseUrl = (endpoint ?? DefaultBaseEndpoint).TrimEnd('/');
        var url = $"{baseUrl}/api/v1/profiles";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
            string json = JsonSerializer.Serialize(profile, _jsonOpts);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await _httpClient.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeleteProfileAsync(string accessToken, string profileFolderName, string? endpoint = null)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(profileFolderName)) return false;
        var baseUrl = (endpoint ?? DefaultBaseEndpoint).TrimEnd('/');
        var url = $"{baseUrl}/api/v1/profiles/{Uri.EscapeDataString(profileFolderName.Trim())}";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Delete, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
            using var resp = await _httpClient.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ProfileSyncResult> SyncProfilesBatchAsync(string accessToken, List<ProfileSyncDto> profiles, string? endpoint = null)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return new ProfileSyncResult { Success = false, Message = "Token is missing" };
        var baseUrl = (endpoint ?? DefaultBaseEndpoint).TrimEnd('/');
        var url = $"{baseUrl}/api/v1/profiles/sync";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
            string json = JsonSerializer.Serialize(new { profiles }, _jsonOpts);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await _httpClient.SendAsync(req);
            string content = await resp.Content.ReadAsStringAsync();
            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                var list = new List<ProfileSyncDto>();
                if (root.TryGetProperty("profiles", out var profs) && profs.ValueKind == JsonValueKind.Array)
                {
                    list = JsonSerializer.Deserialize<List<ProfileSyncDto>>(profs.GetRawText(), _jsonOpts) ?? new();
                }
                return new ProfileSyncResult { Success = true, Profiles = list };
            }
            return new ProfileSyncResult { Success = false, Message = $"HTTP {resp.StatusCode}" };
        }
        catch (Exception ex)
        {
            return new ProfileSyncResult { Success = false, Message = ex.Message };
        }
    }

    public async Task<bool> UpdateTikTokAccountIdentityAsync(string accessToken, string profileFolderName, TikTokAccountSyncDto account, string? endpoint = null)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(profileFolderName) || account == null) return false;
        var baseUrl = (endpoint ?? DefaultBaseEndpoint).TrimEnd('/');
        var url = $"{baseUrl}/api/v1/profiles/{Uri.EscapeDataString(profileFolderName.Trim())}/tiktok-account";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
            string json = JsonSerializer.Serialize(account, _jsonOpts);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await _httpClient.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ProxySyncResult> GetProxiesAsync(string accessToken, string? endpoint = null)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return new ProxySyncResult { Success = false, Message = "Token is missing" };
        var baseUrl = (endpoint ?? DefaultBaseEndpoint).TrimEnd('/');
        var url = $"{baseUrl}/api/v1/proxies";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
            using var resp = await _httpClient.SendAsync(req);
            string content = await resp.Content.ReadAsStringAsync();
            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                var list = new List<ProxySyncDto>();
                if (root.TryGetProperty("proxies", out var prxs) && prxs.ValueKind == JsonValueKind.Array)
                {
                    list = JsonSerializer.Deserialize<List<ProxySyncDto>>(prxs.GetRawText(), _jsonOpts) ?? new();
                }
                return new ProxySyncResult { Success = true, Proxies = list };
            }
            return new ProxySyncResult { Success = false, Message = $"HTTP {resp.StatusCode}" };
        }
        catch (Exception ex)
        {
            return new ProxySyncResult { Success = false, Message = ex.Message };
        }
    }

    public async Task<ProxySyncResult> SyncProxiesBatchAsync(string accessToken, List<ProxySyncDto> proxies, string? endpoint = null)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return new ProxySyncResult { Success = false, Message = "Token is missing" };
        var baseUrl = (endpoint ?? DefaultBaseEndpoint).TrimEnd('/');
        var url = $"{baseUrl}/api/v1/proxies/sync";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
            string json = JsonSerializer.Serialize(new { proxies }, _jsonOpts);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await _httpClient.SendAsync(req);
            string content = await resp.Content.ReadAsStringAsync();
            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                var list = new List<ProxySyncDto>();
                if (root.TryGetProperty("proxies", out var prxs) && prxs.ValueKind == JsonValueKind.Array)
                {
                    list = JsonSerializer.Deserialize<List<ProxySyncDto>>(prxs.GetRawText(), _jsonOpts) ?? new();
                }
                return new ProxySyncResult { Success = true, Proxies = list };
            }
            return new ProxySyncResult { Success = false, Message = $"HTTP {resp.StatusCode}" };
        }
        catch (Exception ex)
        {
            return new ProxySyncResult { Success = false, Message = ex.Message };
        }
    }

    public async Task<bool> UpsertProxyAsync(string accessToken, ProxySyncDto proxy, string? endpoint = null)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || proxy == null) return false;
        var baseUrl = (endpoint ?? DefaultBaseEndpoint).TrimEnd('/');
        var url = $"{baseUrl}/api/v1/proxies";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
            string json = JsonSerializer.Serialize(proxy, _jsonOpts);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await _httpClient.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeleteProxyAsync(string accessToken, string identifier, string? endpoint = null)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(identifier)) return false;
        var baseUrl = (endpoint ?? DefaultBaseEndpoint).TrimEnd('/');
        var url = $"{baseUrl}/api/v1/proxies/{Uri.EscapeDataString(identifier.Trim())}";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Delete, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
            using var resp = await _httpClient.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
