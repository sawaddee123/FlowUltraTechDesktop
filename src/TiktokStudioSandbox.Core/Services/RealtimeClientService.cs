using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SocketIOClient;
using TiktokStudioSandbox.Core.Interfaces;
using TiktokStudioSandbox.Core.Models;

namespace TiktokStudioSandbox.Core.Services;

public class RealtimeClientService : IRealtimeClientService
{
    private SocketIOClient.SocketIO? _client;
    private bool _disposed;

    public bool IsConnected => _client?.Connected == true;

    public event EventHandler<QuotaReducedEventArgs>? OnQuotaReduced;
    public event EventHandler<LicenseKickoutEventArgs>? OnLicenseKickout;
    public event EventHandler<LicenseUpdateEventArgs>? OnLicenseUpdated;

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
            ReconnectionAttempts = 10
        };

        _client = new SocketIOClient.SocketIO(uri, options);

        _client.On("connected", async response =>
        {
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
                Message = "Socket not connected"
            };
        }

        var tcs = new TaskCompletionSource<SlotHeartbeatResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5);
        using var cts = new CancellationTokenSource(effectiveTimeout);
        cts.Token.Register(() => tcs.TrySetResult(new SlotHeartbeatResult
        {
            Success = false,
            StatusCode = 408,
            Message = "Socket heartbeat timed out"
        }));

        try
        {
            var payload = new { leaseId, hwid };
            await _client.EmitAsync("slot:heartbeat", new object[] { payload }, async msg =>
            {
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

                    tcs.TrySetResult(new SlotHeartbeatResult
                    {
                        Success = success,
                        StatusCode = statusCode,
                        Message = msgText ?? (success ? "Heartbeat confirmed (Socket)" : "Heartbeat failed"),
                        IsRevoked = isRevoked,
                        IsExpired = isExpired
                    });
                }
                catch (Exception ex)
                {
                    tcs.TrySetResult(new SlotHeartbeatResult
                    {
                        Success = false,
                        StatusCode = 500,
                        Message = $"Failed to parse heartbeat ack: {ex.Message}"
                    });
                }
                await Task.CompletedTask;
            }, cts.Token);

            return await tcs.Task;
        }
        catch (Exception ex)
        {
            return new SlotHeartbeatResult
            {
                Success = false,
                StatusCode = 0,
                Message = $"Socket emit error: {ex.Message}"
            };
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ = DisconnectAsync();
        GC.SuppressFinalize(this);
    }
}
