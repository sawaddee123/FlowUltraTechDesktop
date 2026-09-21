using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TiktokStudioSandbox.Core.Interfaces;
using TiktokStudioSandbox.Core.Models;

namespace TiktokStudioSandbox.Core.Services;

public class SlotLeaseClientService : ISlotLeaseClientService
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ConcurrentDictionary<string, ActiveSlotLease> _activeLeases = new();
    private Timer? _heartbeatTimer;
    private readonly SemaphoreSlim _cycleLock = new(1, 1);
    private bool _disposed;

    public string DefaultBaseEndpoint { get; set; } = AppConstants.DefaultServerEndpoint;
    public IRealtimeClientService? RealtimeService { get; set; }
    public TimeSpan OfflineDowntimeGracePeriod { get; set; } = TimeSpan.FromMinutes(10);

    public IReadOnlyDictionary<string, ActiveSlotLease> ActiveLeases => _activeLeases;

    public SlotLeaseClientService(
        HttpClient? httpClient = null,
        string? defaultBaseEndpoint = null,
        IRealtimeClientService? realtimeService = null)
    {
        RealtimeService = realtimeService;

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

        StartHeartbeatTimer(TimeSpan.FromSeconds(25));
    }

    public void StartHeartbeatTimer(TimeSpan? interval = null)
    {
        var period = interval ?? TimeSpan.FromSeconds(25);
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = new Timer(async _ =>
        {
            try
            {
                await RunHeartbeatAndWatchdogCycleAsync();
            }
            catch { }
        }, null, period, period);
    }

    public void StopHeartbeatTimer()
    {
        _heartbeatTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void RegisterActiveLease(
        string leaseId,
        string profileFolderName,
        string hwid,
        string authToken,
        Action onWatchdogTriggered)
    {
        if (string.IsNullOrWhiteSpace(leaseId)) return;

        var lease = new ActiveSlotLease
        {
            LeaseId = leaseId.Trim(),
            ProfileFolderName = profileFolderName,
            Hwid = hwid,
            AuthToken = authToken,
            CreatedAt = DateTime.UtcNow,
            LastSuccessfulHeartbeat = DateTime.UtcNow,
            ConsecutiveFailures = 0,
            OnWatchdogTriggered = onWatchdogTriggered
        };

        _activeLeases[lease.LeaseId] = lease;
    }

    public void UnregisterActiveLease(string leaseId)
    {
        if (string.IsNullOrWhiteSpace(leaseId)) return;
        _activeLeases.TryRemove(leaseId.Trim(), out _);
    }

    public ActiveSlotLease? GetLease(string leaseId)
    {
        if (string.IsNullOrWhiteSpace(leaseId)) return null;
        _activeLeases.TryGetValue(leaseId.Trim(), out var lease);
        return lease;
    }

    public async Task<SlotAcquireResult> AcquireSlotAsync(
        string profileFolderName,
        string hwid,
        string authToken,
        string? baseEndpoint = null)
    {
        var endpoint = (baseEndpoint ?? DefaultBaseEndpoint).TrimEnd('/');
        var url = $"{endpoint}/api/v1/slots/acquire";

        var payload = new
        {
            profileFolderName,
            profileFolder = profileFolderName,
            hwid
        };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken.Trim());
            }

            using var resp = await _httpClient.SendAsync(req);
            string content = await resp.Content.ReadAsStringAsync();

            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                string? leaseId = null;
                string? leaseToken = null;

                if (root.TryGetProperty("leaseId", out var lId))
                {
                    leaseId = lId.GetString();
                }
                else if (root.TryGetProperty("lease_id", out var lIdSnake))
                {
                    leaseId = lIdSnake.GetString();
                }

                if (root.TryGetProperty("leaseToken", out var lToken))
                {
                    leaseToken = lToken.GetString();
                }

                return new SlotAcquireResult
                {
                    Success = !string.IsNullOrEmpty(leaseId),
                    LeaseId = leaseId,
                    LeaseToken = leaseToken,
                    StatusCode = (int)resp.StatusCode,
                    Message = "Acquired slot lease successfully"
                };
            }

            string errorMsg = TryExtractErrorMessage(content) ?? $"Server returned {(int)resp.StatusCode} ({resp.ReasonPhrase})";
            return new SlotAcquireResult
            {
                Success = false,
                StatusCode = (int)resp.StatusCode,
                Message = errorMsg
            };
        }
        catch (Exception ex)
        {
            return new SlotAcquireResult
            {
                Success = false,
                StatusCode = 0,
                Message = $"Failed to connect to Slot API: {ex.Message}"
            };
        }
    }

    public async Task<SlotHeartbeatResult> SendHeartbeatAsync(
        string leaseId,
        string hwid,
        string authToken,
        string? baseEndpoint = null)
    {
        var endpoint = (baseEndpoint ?? DefaultBaseEndpoint).TrimEnd('/');
        var url = $"{endpoint}/api/v1/slots/heartbeat";

        var payload = new
        {
            leaseId,
            hwid
        };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken.Trim());
            }

            req.Headers.TryAddWithoutValidation("X-Instance-Lease", leaseId.Trim());

            using var resp = await _httpClient.SendAsync(req);
            string content = await resp.Content.ReadAsStringAsync();

            if (resp.IsSuccessStatusCode)
            {
                return new SlotHeartbeatResult
                {
                    Success = true,
                    StatusCode = (int)resp.StatusCode,
                    Message = "Heartbeat confirmed"
                };
            }

            string errorMsg = TryExtractErrorMessage(content) ?? $"Heartbeat failed with {(int)resp.StatusCode}";
            return new SlotHeartbeatResult
            {
                Success = false,
                StatusCode = (int)resp.StatusCode,
                Message = errorMsg
            };
        }
        catch (Exception ex)
        {
            return new SlotHeartbeatResult
            {
                Success = false,
                StatusCode = 0,
                Message = $"Heartbeat network error: {ex.Message}"
            };
        }
    }

    public async Task<SlotReleaseResult> ReleaseSlotAsync(
        string leaseId,
        string authToken,
        string? baseEndpoint = null)
    {
        UnregisterActiveLease(leaseId);

        var endpoint = (baseEndpoint ?? DefaultBaseEndpoint).TrimEnd('/');
        var url = $"{endpoint}/api/v1/slots/release";

        var payload = new { leaseId };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken.Trim());
            }

            req.Headers.TryAddWithoutValidation("X-Instance-Lease", leaseId.Trim());

            using var resp = await _httpClient.SendAsync(req);
            string content = await resp.Content.ReadAsStringAsync();

            return new SlotReleaseResult
            {
                Success = resp.IsSuccessStatusCode,
                StatusCode = (int)resp.StatusCode,
                Message = resp.IsSuccessStatusCode ? "Slot released" : (TryExtractErrorMessage(content) ?? $"Status {(int)resp.StatusCode}")
            };
        }
        catch (Exception ex)
        {
            return new SlotReleaseResult
            {
                Success = false,
                StatusCode = 0,
                Message = $"Release slot error: {ex.Message}"
            };
        }
    }

    public async Task RunHeartbeatAndWatchdogCycleAsync()
    {
        if (_activeLeases.IsEmpty) return;
        if (!await _cycleLock.WaitAsync(0)) return;

        try
        {
            var now = DateTime.UtcNow;
            var expiredOrViolated = new List<ActiveSlotLease>();

            foreach (var kvp in _activeLeases)
            {
                var lease = kvp.Value;
                SlotHeartbeatResult res;

                // Priority 1: High performance WebSocket heartbeat if socket is connected (<2ms RTT)
                if (RealtimeService?.IsConnected == true)
                {
                    res = await RealtimeService.SendSlotHeartbeatAsync(lease.LeaseId, lease.Hwid);
                    // Fallback to HTTP POST if socket encountered network/transient error
                    if (!res.Success && res.StatusCode != 401 && res.StatusCode != 403 && res.StatusCode != 404 && !res.IsRevoked && !res.IsExpired)
                    {
                        res = await SendHeartbeatAsync(lease.LeaseId, lease.Hwid, lease.AuthToken);
                    }
                }
                else
                {
                    // Fallback to HTTP POST
                    res = await SendHeartbeatAsync(lease.LeaseId, lease.Hwid, lease.AuthToken);
                }

                if (res.Success)
                {
                    lease.LastSuccessfulHeartbeat = DateTime.UtcNow;
                    lease.ConsecutiveFailures = 0;
                }
                else
                {
                    lease.ConsecutiveFailures++;

                    // 1. Explicit Administrative Revocation, Account Inactive, or License Expired
                    if (res.IsRevoked || res.StatusCode == 401 || res.StatusCode == 403 || res.IsExpired)
                    {
                        expiredOrViolated.Add(lease);
                        continue;
                    }

                    // 2. Lease Not Found (404) - Server may have restarted/reloaded and lost in-memory lease
                    // Perform silent auto-recovery via AcquireSlotAsync before giving up
                    if (res.StatusCode == 404 && !string.IsNullOrWhiteSpace(lease.AuthToken))
                    {
                        try
                        {
                            var reacquire = await AcquireSlotAsync(lease.ProfileFolderName, lease.Hwid, lease.AuthToken);
                            if (reacquire.Success && !string.IsNullOrWhiteSpace(reacquire.LeaseId))
                            {
                                string oldId = lease.LeaseId;
                                _activeLeases.TryRemove(oldId, out _);
                                lease.LeaseId = reacquire.LeaseId;
                                lease.LastSuccessfulHeartbeat = DateTime.UtcNow;
                                lease.ConsecutiveFailures = 0;
                                _activeLeases[lease.LeaseId] = lease;
                                continue;
                            }
                        }
                        catch { }

                        expiredOrViolated.Add(lease);
                        continue;
                    }

                    // 3. Transient Network or Server-Down Errors (StatusCode == 0 or >= 500)
                    // The server is likely redeploying or restarting. DO NOT terminate TikTok Studio!
                    // Live streamers are broadcasting directly to TikTok. Allow OfflineDowntimeGracePeriod.
                    var elapsedSinceSuccess = now - lease.LastSuccessfulHeartbeat;
                    if (elapsedSinceSuccess > OfflineDowntimeGracePeriod)
                    {
                        expiredOrViolated.Add(lease);
                    }
                }
            }

            foreach (var lease in expiredOrViolated)
            {
                _activeLeases.TryRemove(lease.LeaseId, out _);
                try
                {
                    lease.OnWatchdogTriggered?.Invoke();
                }
                catch { }
            }
        }
        finally
        {
            _cycleLock.Release();
        }
    }

    private static string? TryExtractErrorMessage(string jsonContent)
    {
        if (string.IsNullOrWhiteSpace(jsonContent)) return null;
        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
            {
                return err.GetString();
            }
            if (doc.RootElement.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
            {
                return msg.GetString();
            }
        }
        catch { }

        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _heartbeatTimer?.Dispose();
        _cycleLock.Dispose();
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
