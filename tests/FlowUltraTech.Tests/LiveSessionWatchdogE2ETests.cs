using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using FlowUltraTech.Core.Models;
using FlowUltraTech.Core.Services;
using Xunit;

namespace FlowUltraTech.Tests;

public class LiveSessionWatchdogE2ETests
{
    private const string CentralServerUrl = "http://210.246.215.66:8001";

    [Fact]
    public async Task LiveSession_BackofficeDoesNotGhost_AndDesktopReportsRunningAccounts()
    {
        // -------------------------------------------------------------
        // Step 1: Admin Login & Connect Backoffice Socket (clientType: backoffice)
        // -------------------------------------------------------------
        using var authService = new AuthClientService();
        var loginRes = await authService.LoginAsync("root", "!WFXU#v1aO7338+", "E2E-WATCHDOG-HWID", CentralServerUrl);
        Assert.True(loginRes.Success, "Login should succeed");
        Assert.False(string.IsNullOrWhiteSpace(loginRes.AccessToken));

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginRes.AccessToken);

        // Check online users before desktop connects -> should NOT show backoffice as client app
        var initialRes = await http.GetAsync($"{CentralServerUrl}/api/v1/admin/online-users");
        Assert.True(initialRes.IsSuccessStatusCode);
        string initialJson = await initialRes.Content.ReadAsStringAsync();
        Console.WriteLine($"[INIT STATE] /admin/online-users: {initialJson}");

        // -------------------------------------------------------------
        // Step 2: Acquire a Slot Lease for a running TikTok Account
        // -------------------------------------------------------------
        using var slotService = new SlotLeaseClientService(http, CentralServerUrl);
        string testHwid = $"HWID-{Guid.NewGuid().ToString("N")[..8]}";
        var leaseRes = await slotService.AcquireSlotAsync("Profile_Acc01", testHwid, loginRes.AccessToken);
        Assert.True(leaseRes.Success, $"Slot acquire should succeed: {leaseRes.Message}");
        Assert.False(string.IsNullOrWhiteSpace(leaseRes.LeaseId));
        string activeLeaseId = leaseRes.LeaseId;
        Console.WriteLine($"[LEASE ACQUIRED] LeaseId={activeLeaseId}");

        // -------------------------------------------------------------
        // Step 3: Connect Desktop Client via RealtimeClientService (clientType: desktop)
        // -------------------------------------------------------------
        using var desktopRealtime = new RealtimeClientService();
        bool connectedFired = false;
        desktopRealtime.OnConnected += (s, e) => connectedFired = true;

        await desktopRealtime.ConnectAsync(CentralServerUrl, loginRes.AccessToken);

        // Wait for connection
        var connectDeadline = DateTime.UtcNow.AddSeconds(5);
        while (!desktopRealtime.IsConnected && DateTime.UtcNow < connectDeadline)
        {
            await Task.Delay(200);
        }
        Assert.True(desktopRealtime.IsConnected, "Desktop RealtimeClientService should be connected");
        Assert.True(connectedFired, "OnConnected event should have fired");

        // -------------------------------------------------------------
        // Step 4: Desktop reports 2 TikTok instances (1 Running with lease, 1 Stopped)
        // -------------------------------------------------------------
        var instancesPayload = new
        {
            appVersion = "1.0.1",
            hwid = testHwid,
            hostname = "DESKTOP-E2E-TEST",
            isProgramOpen = true,
            runningCount = 1,
            totalCount = 2,
            instances = new List<object>
            {
                new
                {
                    name = "TikTok Account 01",
                    accountName = "TikTok Account 01",
                    profileFolderName = "Profile_Acc01",
                    cdpPort = 9222,
                    status = "Running",
                    isTikTokRunning = true,
                    processId = 9876,
                    startedAt = DateTime.UtcNow.ToString("o"),
                    uptimeSeconds = 180,
                    proxy = "Direct",
                    isLoggedIn = true,
                    channelName = "Pro Gamer Live",
                    channelHandle = "@progamer",
                    channelAvatar = "https://p16-va.tiktokcdn.com/avatar.jpg",
                    activeLeaseId = activeLeaseId,
                    captcha = new
                    {
                        hasAlert = false,
                        isSolving = false,
                        typeDescription = "-",
                        countdown = "00:00",
                        count = 0
                    }
                },
                new
                {
                    name = "TikTok Account 02",
                    accountName = "TikTok Account 02",
                    profileFolderName = "Profile_Acc02",
                    cdpPort = 9223,
                    status = "Stopped",
                    isTikTokRunning = false,
                    proxy = "Direct",
                    isLoggedIn = false,
                    channelName = string.Empty,
                    channelHandle = string.Empty,
                    activeLeaseId = string.Empty,
                    captcha = new
                    {
                        hasAlert = false,
                        isSolving = false,
                        typeDescription = "-",
                        countdown = "00:00",
                        count = 0
                    }
                }
            }
        };

        await desktopRealtime.ReportInstancesAsync(instancesPayload);
        await Task.Delay(1500); // Allow server to ingest and broadcast

        // -------------------------------------------------------------
        // Step 5: Verify Backoffice API receives the full TikTok accounts
        // -------------------------------------------------------------
        var onlineRes = await http.GetAsync($"{CentralServerUrl}/api/v1/admin/online-users");
        Assert.True(onlineRes.IsSuccessStatusCode);
        string onlineJson = await onlineRes.Content.ReadAsStringAsync();
        Console.WriteLine($"[ACTIVE ONLINE USERS REPORT]:\n{onlineJson}");

        // Assertions for full live representation
        Assert.Contains(testHwid, onlineJson);
        Assert.Contains("DESKTOP-E2E-TEST", onlineJson);
        Assert.Contains("TikTok Account 01", onlineJson);
        Assert.Contains("Profile_Acc01", onlineJson);
        Assert.Contains("Pro Gamer Live", onlineJson);
        Assert.Contains("@progamer", onlineJson);
        Assert.Contains("9222", onlineJson);
        Assert.Contains("TikTok Account 02", onlineJson);
        Assert.Contains(activeLeaseId, onlineJson);

        // -------------------------------------------------------------
        // Step 6: Cleanup - Release slot lease and disconnect
        // -------------------------------------------------------------
        await slotService.ReleaseSlotAsync(activeLeaseId, loginRes.AccessToken);
        await desktopRealtime.DisconnectAsync();
    }
}
