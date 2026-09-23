using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SocketIOClient;
using FlowUltraTech.Core.Interfaces;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Services;

public class RealtimeClientService : IRealtimeClientService
{
    private SocketIOClient.SocketIO? _client;
    private bool _disposed;

    public bool IsConnected => _client?.Connected == true;

    public event EventHandler<QuotaReducedEventArgs>? OnQuotaReduced;
    public event EventHandler<LicenseKickoutEventArgs>? OnLicenseKickout;
    public event EventHandler<LicenseUpdateEventArgs>? OnLicenseUpdated;
    public event EventHandler<AppUpdateEventArgs>? OnAppUpdateBroadcast;

    public async Task ConnectAsync(string serverUrl, string token)
    {
        if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(token))
            return;

        await DisconnectAsync();

        var uri = new Uri(serverUrl);
        var options = new SocketIOOptions
        {
            Auth = new Dictionary<string, object>
            {
                { "token", token }
            },
            Reconnection = true,
            ReconnectionAttempts = int.MaxValue // Reconnect infinitely to survive server redeployments
        };

        _client = new SocketIOClient.SocketIO(uri, options);

        _client.On("connected", async response =>
        {
            await Task.Yield();
        });

        _client.On("app:update", async response =>
        {
            try
            {
                var doc = response.GetValue<JsonElement>(0);
                JsonElement releaseDoc = doc;
                if (doc.TryGetProperty("release", out var rel) && rel.ValueKind == JsonValueKind.Object)
                {
                    releaseDoc = rel;
                }

                string version = releaseDoc.TryGetProperty("version", out var v) ? (v.GetString() ?? string.Empty) : string.Empty;
                string minVer = releaseDoc.TryGetProperty("minRequiredVersion", out var mv) ? (mv.GetString() ?? string.Empty) : string.Empty;
                string? title = releaseDoc.TryGetProperty("title", out var t) ? t.GetString() : null;
                string desc = releaseDoc.TryGetProperty("description", out var d) ? (d.GetString() ?? string.Empty) : string.Empty;
                string downloadUrl = releaseDoc.TryGetProperty("downloadUrl", out var du) ? (du.GetString() ?? string.Empty) : string.Empty;
                bool isMandatory = releaseDoc.TryGetProperty("isMandatory", out var im) && im.GetBoolean();
                long? fileSize = releaseDoc.TryGetProperty("fileSize", out var fs) && fs.ValueKind == JsonValueKind.Number ? fs.GetInt64() : null;
                string? fileHash = releaseDoc.TryGetProperty("fileHash", out var fh) ? fh.GetString() : null;

                DateTime? publishedAt = null;
                if (releaseDoc.TryGetProperty("publishedAt", out var pa) && pa.ValueKind == JsonValueKind.String)
                {
                    if (DateTime.TryParse(pa.GetString(), out var dt)) publishedAt = dt.ToUniversalTime();
                }

                OnAppUpdateBroadcast?.Invoke(this, new AppUpdateEventArgs
                {
                    Version = version,
                    MinRequiredVersion = minVer,
                    Title = title,
                    Description = desc,
                    DownloadUrl = downloadUrl,
                    IsMandatory = isMandatory,
                    FileSize = fileSize,
                    FileHash = fileHash,
                    PublishedAt = publishedAt
                });
            }
            catch { }
            await Task.Yield();
        });

        _client.On("quota:reduced", async response =>
        {
            try
            {
                var doc = response.GetValue<JsonElement>(0);
                int newMax = doc.TryGetProperty("newMax", out var nm) ? nm.GetInt32() : 2;
                var revokedList = new List<string>();
                if (doc.TryGetProperty("revokedLeaseIds", out var rIds) && rIds.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in rIds.EnumerateArray())
                    {
                        var s = item.GetString();
                        if (!string.IsNullOrEmpty(s)) revokedList.Add(s);
                    }
                }

                OnQuotaReduced?.Invoke(this, new QuotaReducedEventArgs
                {
                    NewMax = newMax,
                    RevokedLeaseIds = revokedList.ToArray()
                });
            }
            catch { }
            await Task.Yield();
        });

        _client.On("license:kickout", async response =>
        {
            try
            {
                var doc = response.GetValue<JsonElement>(0);
                string reason = doc.TryGetProperty("reason", out var r) ? (r.GetString() ?? "License expired") : "License expired";

                OnLicenseKickout?.Invoke(this, new LicenseKickoutEventArgs
                {
                    Reason = reason
                });
            }
            catch { }
            await Task.Yield();
        });

        _client.On("license:updated", async response =>
        {
            try
            {
                var doc = response.GetValue<JsonElement>(0);
                DateTime? expiredAt = null;
                if (doc.TryGetProperty("expiredAt", out var exp) && exp.ValueKind == JsonValueKind.String)
                {
                    if (DateTime.TryParse(exp.GetString(), out var parsedExp))
                        expiredAt = parsedExp.ToUniversalTime();
                }

                int? maxAccounts = null;
                if (doc.TryGetProperty("maxTiktokAccounts", out var max) && max.ValueKind == JsonValueKind.Number)
                {
                    maxAccounts = max.GetInt32();
                }

                string? status = doc.TryGetProperty("status", out var st) ? st.GetString() : null;
                string? plan = doc.TryGetProperty("plan", out var pl) ? pl.GetString() : null;

                OnLicenseUpdated?.Invoke(this, new LicenseUpdateEventArgs
                {
                    ExpiredAt = expiredAt,
                    MaxTiktokAccounts = maxAccounts,
                    Status = status,
                    Plan = plan
                });
            }
            catch { }
            await Task.Yield();
        });

        _client.On("cmd:get_instance_logs", async response =>
        {
            try
            {
                var doc = response.GetValue<JsonElement>(0);
                string requestId = doc.TryGetProperty("requestId", out var rId) ? (rId.GetString() ?? string.Empty) : string.Empty;
                string profileFolder = doc.TryGetProperty("profileFolderName", out var pf) ? (pf.GetString() ?? string.Empty) : string.Empty;
                string accountName = doc.TryGetProperty("accountName", out var an) ? (an.GetString() ?? string.Empty) : string.Empty;
                int cdpPort = doc.TryGetProperty("cdpPort", out var cp) && cp.ValueKind == JsonValueKind.Number ? cp.GetInt32() : 0;
                int maxLines = doc.TryGetProperty("maxLines", out var ml) && ml.ValueKind == JsonValueKind.Number ? ml.GetInt32() : 2000;

                var (success, logs, fileName) = InstanceLogManager.Shared.ReadLogs(profileFolder, cdpPort, maxLines, accountName);

                var replyPayload = new
                {
                    requestId,
                    success,
                    profileFolderName = profileFolder,
                    accountName,
                    cdpPort,
                    fileName,
                    logs,
                    timestamp = DateTime.UtcNow.ToString("o")
                };

                if (_client != null && _client.Connected)
                {
                    await _client.EmitAsync("client:instance_logs_response", new object[] { replyPayload });
                }
            }
            catch (Exception ex)
            {
                if (_client != null && _client.Connected)
                {
                    try
                    {
                        await _client.EmitAsync("client:instance_logs_response", new object[] { new { success = false, error = ex.Message } });
                    }
                    catch { }
                }
            }
            await Task.Yield();
        });

        try
        {
            await _client.ConnectAsync();
        }
        catch { }
    }

    public async Task DisconnectAsync()
    {
        if (_client != null)
        {
            try
            {
                await _client.DisconnectAsync();
                _client.Dispose();
            }
            catch { }
            finally
            {
                _client = null;
            }
        }
    }

    public async Task<SlotHeartbeatResult> SendSlotHeartbeatAsync(string leaseId, string hwid, TimeSpan? timeout = null)
    {
        if (_client == null || !_client.Connected)
        {
            return new SlotHeartbeatResult
            {
                Success = false,
                StatusCode = 0,
                Message = "Socket not connected",
                Transport = "WebSocket"
            };
        }

        var sw = Stopwatch.StartNew();
        var tcs = new TaskCompletionSource<SlotHeartbeatResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5);
        using var cts = new CancellationTokenSource(effectiveTimeout);
        cts.Token.Register(() =>
        {
            sw.Stop();
            tcs.TrySetResult(new SlotHeartbeatResult
            {
                Success = false,
                StatusCode = 408,
                Message = "Socket heartbeat timed out",
                LatencyMs = sw.Elapsed.TotalMilliseconds,
                Transport = "WebSocket"
            });
        });

        try
        {
            var payload = new { leaseId, hwid };
            await _client.EmitAsync("slot:heartbeat", new object[] { payload }, async msg =>
            {
                sw.Stop();
                double latency = sw.Elapsed.TotalMilliseconds;
                try
                {
                    var doc = msg.GetValue<JsonElement>(0);
                    bool success = doc.TryGetProperty("success", out var s) && s.GetBoolean();
                    int statusCode = doc.TryGetProperty("statusCode", out var sc) ? sc.GetInt32() : (success ? 200 : 400);
                    string? msgText = doc.TryGetProperty("message", out var m) ? m.GetString() : null;
                    if (string.IsNullOrEmpty(msgText) && doc.TryGetProperty("error", out var err))
                    {
                        msgText = err.GetString();
                    }
                    bool isRevoked = doc.TryGetProperty("isRevoked", out var ir) && ir.GetBoolean();
                    bool isExpired = doc.TryGetProperty("isExpired", out var ie) && ie.GetBoolean();

                    DateTime? expiresAt = null;
                    if (doc.TryGetProperty("expiresAt", out var exp) && exp.ValueKind == JsonValueKind.String)
                    {
                        if (DateTime.TryParse(exp.GetString(), out var dt)) expiresAt = dt.ToUniversalTime();
                    }

                    int remainingTtl = expiresAt.HasValue
                        ? Math.Max(0, (int)(expiresAt.Value - DateTime.UtcNow).TotalSeconds)
                        : 600;

                    tcs.TrySetResult(new SlotHeartbeatResult
                    {
                        Success = success,
                        StatusCode = statusCode,
                        Message = msgText ?? (success ? "Heartbeat confirmed (Socket)" : "Heartbeat failed"),
                        IsRevoked = isRevoked,
                        IsExpired = isExpired,
                        ExpiresAt = expiresAt,
                        LatencyMs = latency,
                        Transport = "WebSocket",
                        RemainingTtlSeconds = remainingTtl
                    });
                }
                catch (Exception ex)
                {
                    tcs.TrySetResult(new SlotHeartbeatResult
                    {
                        Success = false,
                        StatusCode = 500,
                        Message = $"Failed to parse heartbeat ack: {ex.Message}",
                        LatencyMs = latency,
                        Transport = "WebSocket"
                    });
                }
                await Task.CompletedTask;
            }, cts.Token);

            return await tcs.Task;
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new SlotHeartbeatResult
            {
                Success = false,
                StatusCode = 0,
                Message = $"Socket emit error: {ex.Message}",
                LatencyMs = sw.Elapsed.TotalMilliseconds,
                Transport = "WebSocket"
            };
        }
    }

    public async Task ReportInstancesAsync(object payload)
    {
        if (_client == null || !_client.Connected)
            return;

        try
        {
            await _client.EmitAsync("client:instances_report", new object[] { payload });
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ = DisconnectAsync();
        GC.SuppressFinalize(this);
    }
}
