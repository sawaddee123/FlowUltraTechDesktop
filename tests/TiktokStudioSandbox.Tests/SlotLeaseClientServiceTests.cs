using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TiktokStudioSandbox.Core.Models;
using TiktokStudioSandbox.Core.Services;
using Xunit;

namespace TiktokStudioSandbox.Tests;

public class SlotLeaseClientServiceTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Handler { get; set; }

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            Handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(Handler(request));
        }
    }


    [Fact]
    public async Task AcquireSlotAsync_WhenServerReturnsSuccess_ReturnsLeaseId()
    {
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            Assert.Contains("/api/v1/slots/acquire", req.RequestUri?.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true,\"leaseId\":\"lease_test_999\"}")
            };
        });

        using var client = new HttpClient(mockHandler);
        using var service = new SlotLeaseClientService(client, "http://127.0.0.1:3006");

        var result = await service.AcquireSlotAsync("Acc01", "hwid-12345", "test-token");

        Assert.True(result.Success);
        Assert.Equal("lease_test_999", result.LeaseId);
    }

    [Fact]
    public async Task AcquireSlotAsync_WhenServerReturnsQuotaExceeded_ReturnsFailure()
    {
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{\"error\":\"เกินโควต้าบัญชีสูงสุดแล้ว (2/2)\"}")
            };
        });

        using var client = new HttpClient(mockHandler);
        using var service = new SlotLeaseClientService(client, "http://127.0.0.1:3006");

        var result = await service.AcquireSlotAsync("Acc01", "hwid-12345", "test-token");

        Assert.False(result.Success);
        Assert.Contains("เกินโควต้าบัญชีสูงสุดแล้ว", result.Message);
    }

    [Fact]
    public async Task SendHeartbeatAsync_AttachesXInstanceLeaseHeader()
    {
        string? capturedLeaseHeader = null;

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.Headers.TryGetValues("X-Instance-Lease", out var values))
            {
                capturedLeaseHeader = string.Join(",", values);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true}")
            };
        });

        using var client = new HttpClient(mockHandler);
        using var service = new SlotLeaseClientService(client, "http://127.0.0.1:3006");

        var result = await service.SendHeartbeatAsync("lease_heartbeat_123", "hwid-123", "token-xyz");

        Assert.True(result.Success);
        Assert.Equal("lease_heartbeat_123", capturedLeaseHeader);
    }

    [Fact]
    public async Task ReleaseSlotAsync_RemovesFromActiveLeases()
    {
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true}")
            };
        });

        using var client = new HttpClient(mockHandler);
        using var service = new SlotLeaseClientService(client, "http://127.0.0.1:3006");

        service.RegisterActiveLease("lease_to_release", "Acc01", "hwid-1", "token-1", () => { });
        Assert.NotNull(service.GetLease("lease_to_release"));

        var result = await service.ReleaseSlotAsync("lease_to_release", "token-1");

        Assert.True(result.Success);
        Assert.Null(service.GetLease("lease_to_release"));
    }

    [Fact]
    public async Task Watchdog_WhenHeartbeatFailsWithUnauthorized_TriggersCallbackAndRemovesLease()
    {
        // Explicit 401 Unauthorized revocation
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"error\":\"Unauthorized or license revoked\"}")
            };
        });

        using var client = new HttpClient(mockHandler);
        using var service = new SlotLeaseClientService(client, "http://127.0.0.1:3006");

        bool watchdogTriggered = false;
        service.RegisterActiveLease("lease_expiring", "Acc01", "hwid-1", "token-1", () =>
        {
            watchdogTriggered = true;
        });

        var lease = service.GetLease("lease_expiring");
        Assert.NotNull(lease);

        await service.RunHeartbeatAndWatchdogCycleAsync();

        Assert.True(watchdogTriggered);
        Assert.Null(service.GetLease("lease_expiring"));
    }

    [Fact]
    public async Task Watchdog_WhenLastSuccessfulHeartbeatExceedsGracePeriod_TriggersCallback()
    {
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        using var client = new HttpClient(mockHandler);
        using var service = new SlotLeaseClientService(client, "http://127.0.0.1:3006")
        {
            OfflineDowntimeGracePeriod = TimeSpan.FromSeconds(60)
        };

        bool watchdogTriggered = false;
        service.RegisterActiveLease("lease_timeout", "Acc02", "hwid-2", "token-2", () =>
        {
            watchdogTriggered = true;
        });

        var lease = service.GetLease("lease_timeout");
        Assert.NotNull(lease);

        // Simulate last heartbeat was 65 seconds ago (exceeding 60s grace period)
        lease.LastSuccessfulHeartbeat = DateTime.UtcNow.AddSeconds(-65);

        await service.RunHeartbeatAndWatchdogCycleAsync();

        Assert.True(watchdogTriggered);
        Assert.Null(service.GetLease("lease_timeout"));
    }

    [Fact]
    public async Task Watchdog_WhenServerRedeploying_WithinGracePeriod_DoesNotKillStream()
    {
        // Server redeploying (503 Service Unavailable / 502 Bad Gateway)
        var mockHandler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("{\"error\":\"Deploying new version\"}")
            };
        });

        using var client = new HttpClient(mockHandler);
        using var service = new SlotLeaseClientService(client, "http://127.0.0.1:3006")
        {
            OfflineDowntimeGracePeriod = TimeSpan.FromMinutes(10)
        };

        bool watchdogTriggered = false;
        service.RegisterActiveLease("lease_redeploy", "Acc03", "hwid-3", "token-3", () =>
        {
            watchdogTriggered = true;
        });

        var lease = service.GetLease("lease_redeploy");
        Assert.NotNull(lease);

        // 3 consecutive failures during server restart (e.g. 75 seconds of restart)
        lease.ConsecutiveFailures = 2;
        lease.LastSuccessfulHeartbeat = DateTime.UtcNow.AddSeconds(-75);

        await service.RunHeartbeatAndWatchdogCycleAsync();

        // Must NOT trigger watchdog or kill live stream!
        Assert.False(watchdogTriggered);
        Assert.NotNull(service.GetLease("lease_redeploy"));
        Assert.Equal(3, lease.ConsecutiveFailures);
    }

    private class MockRealtimeClientService : TiktokStudioSandbox.Core.Interfaces.IRealtimeClientService
    {
        public bool IsConnected { get; set; } = true;
#pragma warning disable CS0067
        public event EventHandler<TiktokStudioSandbox.Core.Interfaces.QuotaReducedEventArgs>? OnQuotaReduced;
        public event EventHandler<TiktokStudioSandbox.Core.Interfaces.LicenseKickoutEventArgs>? OnLicenseKickout;
        public event EventHandler<TiktokStudioSandbox.Core.Interfaces.LicenseUpdateEventArgs>? OnLicenseUpdated;
        public event EventHandler<TiktokStudioSandbox.Core.Interfaces.AppUpdateEventArgs>? OnAppUpdateBroadcast;
#pragma warning restore CS0067

        public Func<string, string, Task<SlotHeartbeatResult>>? OnSendSlotHeartbeat { get; set; }

        public Task ConnectAsync(string serverUrl, string token) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;

        public Task<SlotHeartbeatResult> SendSlotHeartbeatAsync(string leaseId, string hwid, TimeSpan? timeout = null)
        {
            if (OnSendSlotHeartbeat != null) return OnSendSlotHeartbeat(leaseId, hwid);
            return Task.FromResult(new SlotHeartbeatResult { Success = true, StatusCode = 200 });
        }

        public Task ReportInstancesAsync(object payload) => Task.CompletedTask;

        public void Dispose() { }
    }

    [Fact]
    public async Task RunHeartbeatCycle_WhenRealtimeConnected_UsesSocketHeartbeat()
    {
        int socketCalls = 0;
        int httpCalls = 0;

        var mockRealtime = new MockRealtimeClientService
        {
            IsConnected = true,
            OnSendSlotHeartbeat = (l, h) =>
            {
                socketCalls++;
                return Task.FromResult(new SlotHeartbeatResult { Success = true, StatusCode = 200 });
            }
        };

        var mockHttpHandler = new MockHttpMessageHandler(req =>
        {
            httpCalls++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true}")
            };
        });

        using var client = new HttpClient(mockHttpHandler);
        using var service = new SlotLeaseClientService(client, "http://127.0.0.1:3006", mockRealtime);

        service.RegisterActiveLease("lease_fast", "Acc01", "hwid-1", "token-1", () => { });

        await service.RunHeartbeatAndWatchdogCycleAsync();

        Assert.Equal(1, socketCalls);
        Assert.Equal(0, httpCalls); // Did not need HTTP!
    }

    [Fact]
    public async Task RunHeartbeatCycle_WhenRealtimeFailsTransitorily_FallsBackToHttp()
    {
        int socketCalls = 0;
        int httpCalls = 0;

        var mockRealtime = new MockRealtimeClientService
        {
            IsConnected = true,
            OnSendSlotHeartbeat = (l, h) =>
            {
                socketCalls++;
                return Task.FromResult(new SlotHeartbeatResult { Success = false, StatusCode = 408, Message = "Timeout" });
            }
        };

        var mockHttpHandler = new MockHttpMessageHandler(req =>
        {
            httpCalls++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true}")
            };
        });

        using var client = new HttpClient(mockHttpHandler);
        using var service = new SlotLeaseClientService(client, "http://127.0.0.1:3006", mockRealtime);

        service.RegisterActiveLease("lease_fallback", "Acc01", "hwid-1", "token-1", () => { });

        await service.RunHeartbeatAndWatchdogCycleAsync();

        Assert.Equal(1, socketCalls);
        Assert.Equal(1, httpCalls); // Successfully fell back to HTTP!
    }

    [Fact]
    public async Task RunHeartbeatCycle_WhenRealtimeRevoked_ImmediatelyTriggersWatchdogWithoutHttp()
    {
        int socketCalls = 0;
        int httpCalls = 0;
        bool watchdogTriggered = false;

        var mockRealtime = new MockRealtimeClientService
        {
            IsConnected = true,
            OnSendSlotHeartbeat = (l, h) =>
            {
                socketCalls++;
                return Task.FromResult(new SlotHeartbeatResult { Success = false, StatusCode = 404, IsRevoked = true });
            }
        };

        var mockHttpHandler = new MockHttpMessageHandler(req =>
        {
            httpCalls++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = new HttpClient(mockHttpHandler);
        using var service = new SlotLeaseClientService(client, "http://127.0.0.1:3006", mockRealtime);

        service.RegisterActiveLease("lease_revoked", "Acc01", "hwid-1", "token-1", () =>
        {
            watchdogTriggered = true;
        });

        await service.RunHeartbeatAndWatchdogCycleAsync();

        Assert.Equal(1, socketCalls);
        Assert.Equal(0, httpCalls); // Rejection is definitive, no wasted HTTP call
        Assert.True(watchdogTriggered);
        Assert.Null(service.GetLease("lease_revoked"));
    }
}
