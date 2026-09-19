using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TiktokStudioSandbox.Core.Interfaces;

namespace TiktokStudioSandbox.Core.Services;

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
                    ExpiredAt = expiredAt
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

                return new LoginResult
                {
                    Success = true,
                    Message = "ดึงข้อมูลสิทธิ์สำเร็จ",
                    AccessToken = accessToken,
                    Username = user,
                    Role = role,
                    MaxTiktokAccounts = maxAccounts,
                    Plan = plan,
                    ExpiredAt = expiredAt
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
