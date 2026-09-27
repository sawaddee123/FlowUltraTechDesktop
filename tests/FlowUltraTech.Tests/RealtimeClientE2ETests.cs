using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FlowUltraTech.Core.Models;
using FlowUltraTech.Core.Services;
using Xunit;

namespace FlowUltraTech.Tests;

public class RealtimeClientE2ETests
{
    private const string ServerUrl = "http://210.246.215.66:8001";

    [Fact]
    public async Task RealtimeClient_CanConnect_AndEmitInstancesReport()
    {
        // 1. Login to get token
        using var authService = new AuthClientService();
        var loginRes = await authService.LoginAsync("root", "!WFXU#v1aO7338+", "E2E-TEST-HWID", ServerUrl);
        Assert.True(loginRes.Success);
        Assert.NotNull(loginRes.AccessToken);

        // 2. Connect via RealtimeClientService
        using var realtime = new RealtimeClientService();
        await realtime.ConnectAsync(ServerUrl, loginRes.AccessToken);

        // Wait up to 5 seconds for connection
        var start = DateTime.UtcNow;
        while (!realtime.IsConnected && (DateTime.UtcNow - start).TotalSeconds < 5)
        {
            await Task.Delay(200);
        }

        Assert.True(realtime.IsConnected, "RealtimeClientService should connect to server");

        // 3. Emit instances report
        var instances = new List<object>
        {
            new
            {
                name = "Test TikTok 01",
                accountName = "Test TikTok 01",
                profileFolderName = "Acc01",
                cdpPort = 9222,
                status = "Running",
                isTikTokRunning = true,
                processId = 1234,
                startedAt = DateTime.UtcNow.ToString("o"),
                uptimeSeconds = 300,
                proxy = "Direct",
                isLoggedIn = true,
                channelName = "Live Channel",
                channelHandle = "@live_chan",
                activeLeaseId = "lease-test-123"
            }
        };

        var payload = new
        {
            appVersion = "1.0.1",
            hwid = "E2E-REALTIME-HWID-01",
            hostname = "E2E-HOST",
            isProgramOpen = true,
            runningCount = 1,
            totalCount = 1,
            instances = instances
        };

        await realtime.ReportInstancesAsync(payload);
        await Task.Delay(1000);

        // 4. Verify via admin online-users API
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginRes.AccessToken);
        var resp = await http.GetAsync($"{ServerUrl}/api/v1/admin/online-users");
        Assert.True(resp.IsSuccessStatusCode);

        string json = await resp.Content.ReadAsStringAsync();
        Console.WriteLine($"[ONLINE USERS API RESPONSE]: {json}");

        Assert.Contains("E2E-REALTIME-HWID-01", json);
        Assert.Contains("Test TikTok 01", json);
        Assert.Contains("Acc01", json);

        await realtime.DisconnectAsync();
    }
}
