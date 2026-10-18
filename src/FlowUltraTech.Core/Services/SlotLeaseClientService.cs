using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlowUltraTech.Core.Interfaces;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Services;

public class SlotLeaseClientService : ISlotLeaseClientService
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ConcurrentDictionary<string, ActiveSlotLease> _activeLeases = new();
    private Timer? _heartbeatTimer;
    private readonly SemaphoreSlim _cycleLock = new(1, 1);
    private bool _disposed;
    private long _cycleCount = 0;
    private TimeSpan _currentInterval = TimeSpan.FromSeconds(25);

    public event Action<string, string, string>? OnHeartbeatLog;

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
        _currentInterval = period;
        _heartbeatTimer?.Dispose();
        LogSystem(InstanceLogLevel.Info, $"Heartbeat interval timer started (Interval: {period.TotalSeconds}s).");
        _heartbeatTimer = new Timer(async _ =>
        {
            try
            {
                await RunHeartbeatAndWatchdogCycleAsync();
            }
            catch (Exception ex)
            {
                LogSystem(InstanceLogLevel.Error, $"Unhandled error in heartbeat cycle: {ex.Message}", ex.ToString());
            }
        }, null, period, period);
    }

    public void StopHeartbeatTimer()
    {
        LogSystem(InstanceLogLevel.Info, "Heartbeat interval timer stopped.");
        _heartbeatTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void RegisterActiveLease(
        string leaseId,
        string profileFolderName,
        string hwid,
        string authToken,
        Action onWatchdogTriggered,
        IInstanceLogger? logger = null)
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
            OnWatchdogTriggered = onWatchdogTriggered,
            Logger = logger
        };

        _activeLeases[lease.LeaseId] = lease;
        LogLease(lease, InstanceLogLevel.Info, $"Registered active lease {lease.LeaseId} for profile '{profileFolderName}' (HWID: {hwid}). TTL watchdog armed.");
    }

    public void UnregisterActiveLease(string leaseId)
    {
        if (string.IsNullOrWhiteSpace(leaseId)) return;
        if (_activeLeases.TryRemove(leaseId.Trim(), out var lease))
        {
            LogLease(lease, InstanceLogLevel.Info, $"Unregistered active lease {leaseId} for profile '{lease.ProfileFolderName}'.");
        }
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
        var sw = Stopwatch.StartNew();

        _activeLeases.TryGetValue(leaseId.Trim(), out var trackedLease);
        var payload = new
        {
            leaseId,
            hwid,
            profileFolderName = trackedLease?.ProfileFolderName
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
            sw.Stop();
            double latencyMs = sw.Elapsed.TotalMilliseconds;
            string content = await resp.Content.ReadAsStringAsync();

            if (resp.IsSuccessStatusCode)
            {
                DateTime? expiresAt = TryExtractExpiresAt(content);
                int remainingTtl = expiresAt.HasValue
                    ? Math.Max(0, (int)(expiresAt.Value - DateTime.UtcNow).TotalSeconds)
                    : 600;

                return new SlotHeartbeatResult
                {
                    Success = true,
                    StatusCode = (int)resp.StatusCode,
                    Message = "Heartbeat confirmed (HTTP)",
                    ExpiresAt = expiresAt,
                    LatencyMs = latencyMs,
                    Transport = "HTTP",
                    RemainingTtlSeconds = remainingTtl
                };
            }

            string errorMsg = TryExtractErrorMessage(content) ?? $"Heartbeat failed with {(int)resp.StatusCode}";
            bool isRevoked = content.Contains("SLOT_REVOKED", StringComparison.OrdinalIgnoreCase) || content.Contains("เพิกถอน", StringComparison.OrdinalIgnoreCase);
            bool isExpired = content.Contains("LEASE_EXPIRED", StringComparison.OrdinalIgnoreCase) || content.Contains("LICENSE_EXPIRED", StringComparison.OrdinalIgnoreCase);

            return new SlotHeartbeatResult
            {
                Success = false,
                StatusCode = (int)resp.StatusCode,
                Message = errorMsg,
                LatencyMs = latencyMs,
                Transport = "HTTP",
                IsRevoked = isRevoked,
                IsExpired = isExpired
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new SlotHeartbeatResult
            {
                Success = false,
                StatusCode = 0,
                Message = $"Heartbeat network error: {ex.Message}",
                LatencyMs = sw.Elapsed.TotalMilliseconds,
                Transport = "HTTP"
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
        if (!await _cycleLock.WaitAsync(0))
        {
            LogSystem(InstanceLogLevel.Warn, "[Lease/TTL] ⚠️ Previous heartbeat cycle is still running. Skipping this cycle to prevent overlapping calls.");
            return;
        }

        var totalSw = Stopwatch.StartNew();
        _cycleCount++;
        long currentCycle = _cycleCount;
        int initialCount = _activeLeases.Count;

        LogSystem(InstanceLogLevel.Info, $"[Lease/TTL] ─── Starting Heartbeat & TTL Watchdog Cycle #{currentCycle} ({initialCount} active lease(s)) ───");

        int successCount = 0;
        int failCount = 0;
        int evictedCount = 0;
        double slowestLeaseMs = 0;
        string slowestProfile = string.Empty;
        string slowestTransport = string.Empty;

        try
        {
            var now = DateTime.UtcNow;
            var expiredOrViolated = new List<(ActiveSlotLease Lease, string Reason)>();

            foreach (var kvp in _activeLeases)
            {
                var lease = kvp.Value;
                var leaseSw = Stopwatch.StartNew();
                SlotHeartbeatResult res;

                string preferredTransport = (RealtimeService?.IsConnected == true) ? "WebSocket" : "HTTP";
                LogLease(lease, InstanceLogLevel.Debug, $"[Lease/TTL] [{lease.ProfileFolderName}] Sending heartbeat for lease '{lease.LeaseId}' via {preferredTransport} (HWID: {lease.Hwid})...");

                // Priority 1: High performance WebSocket heartbeat if socket is connected (<2ms RTT)
                if (RealtimeService?.IsConnected == true)
                {
                    res = await RealtimeService.SendSlotHeartbeatAsync(lease.LeaseId, lease.Hwid);
                    // Fallback to HTTP POST if socket encountered network/transient error
                    if (!res.Success && res.StatusCode != 401 && res.StatusCode != 403 && res.StatusCode != 404 && !res.IsRevoked && !res.IsExpired)
                    {
                        LogLease(lease, InstanceLogLevel.Warn, $"[Lease/TTL] [{lease.ProfileFolderName}] ⚠️ WebSocket heartbeat failed ({res.Message}, Status={res.StatusCode}). Falling back to HTTP POST /api/v1/slots/heartbeat...");
                        var httpSw = Stopwatch.StartNew();
                        res = await SendHeartbeatAsync(lease.LeaseId, lease.Hwid, lease.AuthToken);
                        httpSw.Stop();
                        res.LatencyMs += httpSw.Elapsed.TotalMilliseconds;
                    }
                }
                else
                {
                    // Fallback to HTTP POST
                    res = await SendHeartbeatAsync(lease.LeaseId, lease.Hwid, lease.AuthToken);
                }

                leaseSw.Stop();
                double elapsedMs = leaseSw.Elapsed.TotalMilliseconds;
                lease.LastLatencyMs = elapsedMs;
                lease.LastTransport = res.Transport;
                if (res.ExpiresAt.HasValue)
                {
                    lease.ExpiresAt = res.ExpiresAt.Value;
                }

                if (elapsedMs > slowestLeaseMs)
                {
                    slowestLeaseMs = elapsedMs;
                    slowestProfile = lease.ProfileFolderName;
                    slowestTransport = res.Transport;
                }

                if (res.Success)
                {
                    successCount++;
                    lease.LastSuccessfulHeartbeat = DateTime.UtcNow;
                    lease.ConsecutiveFailures = 0;

                    string expiryInfo = res.ExpiresAt.HasValue
                        ? $"TTL extended to {res.ExpiresAt.Value.ToLocalTime():HH:mm:ss} ({res.RemainingTtlSeconds}s remaining)"
                        : "TTL extended (10m)";

                    LogLease(lease, InstanceLogLevel.Info,
                        $"[Lease/TTL] [{lease.ProfileFolderName}] ✔ Heartbeat SUCCESS in {elapsedMs:F1}ms via {res.Transport}. Lease='{lease.LeaseId}'. {expiryInfo}.");
                }
                else
                {
                    failCount++;
                    lease.ConsecutiveFailures++;

                    LogLease(lease, InstanceLogLevel.Warn,
                        $"[Lease/TTL] [{lease.ProfileFolderName}] ✖ Heartbeat FAILED in {elapsedMs:F1}ms via {res.Transport}. Status={res.StatusCode}, Error='{res.Message}', ConsecutiveFailures={lease.ConsecutiveFailures}.");

                    // 1. Explicit Administrative Revocation, Account Inactive, or License Expired
                    if (res.IsRevoked || res.StatusCode == 401 || res.StatusCode == 403 || res.IsExpired)
                    {
                        string reason = res.IsRevoked ? "Quota reduced or revoked by admin" : (res.IsExpired ? "License expired" : $"Rejected with status {res.StatusCode}");
                        expiredOrViolated.Add((lease, reason));
                        continue;
                    }

                    // 2. Lease Not Found (404) - Server may have restarted/reloaded and lost in-memory lease
                    // Perform silent auto-recovery via AcquireSlotAsync before giving up
                    if (res.StatusCode == 404 && !string.IsNullOrWhiteSpace(lease.AuthToken))
                    {
                        LogLease(lease, InstanceLogLevel.Warn,
                            $"[Lease/TTL] [{lease.ProfileFolderName}] 🔄 Lease '{lease.LeaseId}' returned 404 Not Found (server eviction). Attempting silent auto-recovery via AcquireSlotAsync...");

                        var reacquireSw = Stopwatch.StartNew();
                        try
                        {
                            var reacquire = await AcquireSlotAsync(lease.ProfileFolderName, lease.Hwid, lease.AuthToken);
                            reacquireSw.Stop();

                            if (reacquire.Success && !string.IsNullOrWhiteSpace(reacquire.LeaseId))
                            {
                                string oldId = lease.LeaseId;
                                _activeLeases.TryRemove(oldId, out _);
                                lease.LeaseId = reacquire.LeaseId;
                                lease.LastSuccessfulHeartbeat = DateTime.UtcNow;
                                lease.ConsecutiveFailures = 0;
                                _activeLeases[lease.LeaseId] = lease;

                                LogLease(lease, InstanceLogLevel.Info,
                                    $"[Lease/TTL] [{lease.ProfileFolderName}] ✔ Silent auto-recovery SUCCESS in {reacquireSw.Elapsed.TotalMilliseconds:F1}ms. Replaced lease '{oldId}' -> '{lease.LeaseId}'.");
                                successCount++;
                                failCount--;
                                continue;
                            }
                            else
                            {
                                LogLease(lease, InstanceLogLevel.Error,
                                    $"[Lease/TTL] [{lease.ProfileFolderName}] ✖ Silent auto-recovery failed in {reacquireSw.Elapsed.TotalMilliseconds:F1}ms: {reacquire.Message}");
                            }
                        }
                        catch (Exception ex)
                        {
                            reacquireSw.Stop();
                            LogLease(lease, InstanceLogLevel.Error,
                                $"[Lease/TTL] [{lease.ProfileFolderName}] ✖ Silent auto-recovery exception in {reacquireSw.Elapsed.TotalMilliseconds:F1}ms: {ex.Message}");
                        }

                        expiredOrViolated.Add((lease, "Lease 404 Not Found and auto-recovery failed"));
                        continue;
                    }

                    // 3. Transient Network or Server-Down Errors (StatusCode == 0 or >= 500)
                    // The server is likely redeploying or restarting. DO NOT terminate TikTok Studio!
                    // Live streamers are broadcasting directly to TikTok. Allow OfflineDowntimeGracePeriod.
                    var elapsedSinceSuccess = now - lease.LastSuccessfulHeartbeat;
                    double remainingGraceSec = Math.Max(0, (OfflineDowntimeGracePeriod - elapsedSinceSuccess).TotalSeconds);

                    LogLease(lease, InstanceLogLevel.Warn,
                        $"[Lease/TTL] [{lease.ProfileFolderName}] ⚠️ Network/Server unavailable (StatusCode: {res.StatusCode}). Streamer safety guard active: offline for {elapsedSinceSuccess.TotalSeconds:F0}s (Grace period remaining: {remainingGraceSec:F0}s).");

                    if (elapsedSinceSuccess > OfflineDowntimeGracePeriod)
                    {
                        expiredOrViolated.Add((lease, $"Offline downtime exceeded grace period ({OfflineDowntimeGracePeriod.TotalMinutes:F0} min)"));
                    }
                }
            }

            foreach (var (lease, reason) in expiredOrViolated)
            {
                evictedCount++;
                _activeLeases.TryRemove(lease.LeaseId, out _);
                LogLease(lease, InstanceLogLevel.Error,
                    $"[Lease/TTL] [{lease.ProfileFolderName}] 🚨 WATCHDOG TRIGGERED: Terminating instance '{lease.ProfileFolderName}'. Reason: {reason}.");

                try
                {
                    lease.OnWatchdogTriggered?.Invoke();
                }
                catch (Exception ex)
                {
                    LogLease(lease, InstanceLogLevel.Error,
                        $"[Lease/TTL] [{lease.ProfileFolderName}] Error executing watchdog callback: {ex.Message}");
                }
            }
        }
        finally
        {
            totalSw.Stop();
            double totalMs = totalSw.Elapsed.TotalMilliseconds;

            LogSystem(InstanceLogLevel.Info,
                $"[Lease/TTL] ─── Cycle #{currentCycle} completed in {totalMs:F1}ms. Renewed: {successCount}, Failed: {failCount}, Evicted: {evictedCount}. Next cycle in {_currentInterval.TotalSeconds:F0}s. ───");

            // Lag Detection Threshold: If entire cycle took > 500ms or any lease took > 300ms, emit explicit WARNING
            if (totalMs > 500 || slowestLeaseMs > 300)
            {
                LogSystem(InstanceLogLevel.Warn,
                    $"[Lease/TTL] ⚠️ LAG WARNING: Heartbeat cycle #{currentCycle} took {totalMs:F1}ms (> 500ms threshold)! Slowest lease: '{slowestProfile}' ({slowestLeaseMs:F1}ms via {slowestTransport}). Check network or server performance.");
            }

            _cycleLock.Release();
        }
    }

    private void LogLease(ActiveSlotLease? lease, InstanceLogLevel level, string message, string? details = null)
    {
        lease?.Logger?.Log(level, "Lease", message, details);
        OnHeartbeatLog?.Invoke(level.ToString(), message, details ?? string.Empty);
        try
        {
            var sharedLogger = InstanceLogManager.Shared.TryGetLoggerByPort(0);
            sharedLogger?.Log(level, "Lease", message, details);
        }
        catch { }
    }

    private void LogSystem(InstanceLogLevel level, string message, string? details = null)
    {
        OnHeartbeatLog?.Invoke(level.ToString(), message, details ?? string.Empty);
        try
        {
            var sharedLogger = InstanceLogManager.Shared.TryGetLoggerByPort(0);
            sharedLogger?.Log(level, "Lease", message, details);
        }
        catch { }
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

    private static DateTime? TryExtractExpiresAt(string jsonContent)
    {
        if (string.IsNullOrWhiteSpace(jsonContent)) return null;
        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            if (doc.RootElement.TryGetProperty("expiresAt", out var exp))
            {
                if (exp.ValueKind == JsonValueKind.String && DateTime.TryParse(exp.GetString(), out var dt))
                {
                    return dt.ToUniversalTime();
                }
            }
        }
        catch { }

        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopHeartbeatTimer();
        _heartbeatTimer?.Dispose();
        _cycleLock.Dispose();

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
