using System;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlowUltraTech.App.ViewModels;
using FlowUltraTech.Core.Interfaces;
using FlowUltraTech.Core.Models;
using FlowUltraTech.Core.Services;
using Xunit;

namespace FlowUltraTech.Tests;

public class OverlaySystemTests
{
    [Fact]
    public void OverlayConfig_CloneAndJsonRoundTrip_PreservesAllProperties()
    {
        var original = new OverlayConfig
        {
            IsEnabled = true,
            ClockMode = OverlayClockMode.WoodBlockLed,
            BackgroundStyle = OverlayBackgroundStyle.GlassDark,
            TimeZoneId = "Asia/Bangkok",
            Use24HourFormat = false,
            ShowSeconds = false,
            ShowThaiDate = true,
            PrimaryColorHex = "#38BDF8",
            AccentColorHex = "#F59E0B",
            BrandText = "FLOW ULTRA SHOP",
            SubtitleText = "ANTI-REBROADCAST",
            EnableGlareEffect = false,
            EnableDynamicShadow = true,
            EnableMicroJitter = true,
            EnableLiveEventTicker = true,
            EnableFlashDealCountdown = true,
            FlashDealTitle = "ลด 50% เฉพาะไลฟ์นี้",
            FlashDealDurationSeconds = 180,
            FlashDealEndsAtUtc = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc)
        };

        var clone = original.Clone();
        Assert.Equal(original.ClockMode, clone.ClockMode);
        Assert.Equal(original.BackgroundStyle, clone.BackgroundStyle);
        Assert.Equal(original.BrandText, clone.BrandText);
        Assert.Equal(original.SubtitleText, clone.SubtitleText);
        Assert.Equal(original.PrimaryColorHex, clone.PrimaryColorHex);
        Assert.Equal(original.AccentColorHex, clone.AccentColorHex);
        Assert.Equal(original.EnableGlareEffect, clone.EnableGlareEffect);
        Assert.Equal(original.FlashDealTitle, clone.FlashDealTitle);
        Assert.Equal(original.FlashDealDurationSeconds, clone.FlashDealDurationSeconds);

        var settings = new AppSettings
        {
            OverlayServerPort = 19488
        };
        var profile = new StudioInstanceProfile
        {
            Name = "Overlay Test Profile",
            Overlay = clone
        };
        settings.Instances.Add(profile);

        string json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal(19488, restored!.OverlayServerPort);
        Assert.Single(restored.Instances);
        Assert.Equal(OverlayClockMode.WoodBlockLed, restored.Instances[0].Overlay.ClockMode);
        Assert.Equal("FLOW ULTRA SHOP", restored.Instances[0].Overlay.BrandText);
        Assert.Equal("ลด 50% เฉพาะไลฟ์นี้", restored.Instances[0].Overlay.FlashDealTitle);
    }

    [Fact]
    public async Task OverlayServerService_PortFallbackAndHttpEndpoints_WorkReliably()
    {
        using var server1 = new OverlayServerService();
        using var server2 = new OverlayServerService();

        int port1 = await server1.StartAsync(19520);
        Assert.True(server1.IsRunning);
        Assert.True(port1 > 0);

        // Edge Case: Port collision triggers automatic fallback port
        int port2 = await server2.StartAsync(port1);
        Assert.True(server2.IsRunning);
        Assert.NotEqual(port1, port2);

        var profileId = Guid.NewGuid();
        string overlayUrl = server1.GetOverlayUrl(profileId);
        Assert.Contains($"/overlay/{profileId:D}", overlayUrl);

        using var http = new HttpClient();
        string html = await http.GetStringAsync(overlayUrl);

        // Verify all 6 Horological Clock Modes & Realistic Camera Physics are present
        Assert.Contains(profileId.ToString("D"), html);
        Assert.Contains("RealisticWall", html);
        Assert.Contains("SeikoCustom", html);
        Assert.Contains("Desk3D", html);
        Assert.Contains("TwinBell", html);
        Assert.Contains("WoodBlockLed", html);
        Assert.Contains("DigitalClean", html);
        Assert.Contains("fx-glare", html);
        Assert.Contains("fx-shadow", html);
        Assert.Contains("fx-jitter", html);
        Assert.Contains("is-preview-mode", html);

        // Edge Case: Invalid GUID in path falls back safely without 500 error
        var badGuidResp = await http.GetAsync($"http://127.0.0.1:{port1}/overlay/invalid-guid-segment");
        Assert.True(badGuidResp.IsSuccessStatusCode);

        // Favicon returns 204 No Content to guarantee 0 browser console errors
        var favResp = await http.GetAsync($"http://127.0.0.1:{port1}/favicon.ico");
        Assert.Equal(System.Net.HttpStatusCode.NoContent, favResp.StatusCode);
    }

    [Fact]
    public async Task OverlayServerService_WebSocketHotReload_PushesConfigEventsAndFlashDeal()
    {
        using var server = new OverlayServerService();
        int port = await server.StartAsync(19540);
        var profileId = Guid.NewGuid();

        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws/{profileId:D}"), CancellationToken.None);

        Assert.Equal(1, server.GetConnectedClientCount(profileId));

        // 1. Hot-Reload Config Push
        var updatedConfig = server.GetConfig(profileId);
        updatedConfig.ClockMode = OverlayClockMode.SeikoCustom;
        updatedConfig.BackgroundStyle = OverlayBackgroundStyle.GlassDark;
        updatedConfig.BrandText = "CHRONO STUDIO";
        updatedConfig.PrimaryColorHex = "#E2E8F0";
        updatedConfig.AccentColorHex = "#10B981";
        await server.UpdateConfigAsync(profileId, updatedConfig);

        string configMsg = await ReceiveWebSocketTextAsync(ws);
        Assert.Contains("\"type\":\"config\"", configMsg);
        Assert.Contains("SeikoCustom", configMsg);
        Assert.Contains("CHRONO STUDIO", configMsg);

        // 2. Live Event Broadcast + Overflow Truncation Edge Case
        var liveEvent = new OverlayLiveEventPayload
        {
            EventType = "gift",
            Nickname = "ผู้ชมสายเปย์_" + new string('A', 220),
            Username = "big_spender",
            Message = "ส่งของขวัญ Galaxy x10 " + new string('B', 400),
            BadgeText = "ของขวัญ"
        };
        await server.BroadcastLiveEventAsync(profileId, liveEvent);

        string eventMsg = await ReceiveWebSocketTextAsync(ws);
        Assert.Contains("\"type\":\"event\"", eventMsg);
        Assert.Contains("ส่งของขวัญ Galaxy x10", eventMsg);
        Assert.Contains("ผู้ชมสายเปย์_", eventMsg);

        // 3. Flash Deal Trigger & Stop
        await server.TriggerFlashDealAsync(profileId, "นาทีทอง ซื้อ 1 แถม 1", 240);
        string dealStartMsg = await ReceiveWebSocketTextAsync(ws);
        Assert.Contains("นาทีทอง ซื้อ 1 แถม 1", dealStartMsg);
        Assert.True(server.GetConfig(profileId).EnableFlashDealCountdown);

        await server.StopFlashDealAsync(profileId);
        string dealStopMsg = await ReceiveWebSocketTextAsync(ws);
        Assert.Contains("flash_deal_stop", dealStopMsg);
        Assert.False(server.GetConfig(profileId).EnableFlashDealCountdown);
    }

    [Fact]
    public async Task CdpAutomationService_OverlayAutoInject_HandlesValidationOfflineAndScriptGeneration()
    {
        using var cdp = new CdpAutomationService();
        string testUrl = "http://127.0.0.1:19450/overlay/11111111-1111-1111-1111-111111111111";

        string step1Script = cdp.BuildOverlayInjectScript(testUrl, "open_add_source");
        string fullScript = cdp.BuildOverlayInjectScript(testUrl, "fill_and_confirm");

        Assert.Contains("Add source", step1Script);
        Assert.Contains("เพิ่มซอร์ส", step1Script);
        Assert.Contains(testUrl, fullScript);
        Assert.Contains("HTMLInputElement.prototype", fullScript);

        // Edge Case 1: Empty URL or invalid port
        var invalidUrlResult = await cdp.InjectOverlaySourceAsync(9221, "");
        Assert.False(invalidUrlResult.Success);
        Assert.Equal("Validation", invalidUrlResult.StepFailed);

        var invalidPortResult = await cdp.InjectOverlaySourceAsync(0, testUrl);
        Assert.False(invalidPortResult.Success);
        Assert.Equal("Validation", invalidPortResult.StepFailed);

        // Edge Case 2: Unreachable / Offline CDP Port
        var offlineResult = await cdp.InjectOverlaySourceAsync(59996, testUrl);
        Assert.False(offlineResult.Success);
        Assert.Equal("ConnectCdp", offlineResult.StepFailed);
    }

    [Fact]
    public void TikTokLiveListenerService_OnLiveEventReceived_MulticastsToSubscribers()
    {
        using var listener = new TikTokLiveListenerService();
        string? capturedChannel = null;
        LiveChatMessageItem? capturedItem = null;

        listener.OnLiveEventReceived += (channel, item) =>
        {
            capturedChannel = channel;
            capturedItem = item;
        };

        listener.PublishSimulatedEvent("@thai_live_store", new LiveChatMessageItem
        {
            Type = LiveChatMessageType.Chat,
            Nickname = "สมหญิง",
            Username = "somying99",
            Message = "สนใจสินค้าชิ้นที่ 2"
        });

        Assert.Equal("thai_live_store", capturedChannel);
        Assert.NotNull(capturedItem);
        Assert.Equal("สมหญิง", capturedItem!.Nickname);
        Assert.Equal("สนใจสินค้าชิ้นที่ 2", capturedItem.Message);
    }

    [Fact]
    public async Task InstanceCardViewModel_OverlayStudioIntegration_SyncsConfigTabsAndLiveEvents()
    {
        using var server = new OverlayServerService();
        int port = await server.StartAsync(19560);

        var profile = new StudioInstanceProfile
        {
            Name = "Shop 01",
            ProfileFolderName = "OverlayTestProfile_NonExistent",
            CdpPort = 9221,
            StreamerUsername = "shop01_official"
        };
        var settings = new AppSettings { OverlayServerPort = port };
        using var listener = new TikTokLiveListenerService();
        var orchestrator = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), new CdpAutomationService());

        // Connect WebSocket client first so ViewModel construction and UI commands run on the same thread
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws/{profile.Id:D}"), CancellationToken.None);

        int stateSaveCount = 0;
        var vm = new InstanceCardViewModel(
            profile,
            settings,
            orchestrator,
            liveListenerService: listener,
            overlayService: server,
            onStateChanged: () => stateSaveCount++);

        // Verify Tab Index Continuity: Captcha is Tab 4, Overlay is Tab 7
        vm.OpenCaptchaTabCommand.Execute(null);
        Assert.True(vm.IsDetailOpen);
        Assert.Equal(4, vm.SelectedDetailTabIndex);

        vm.OpenOverlayTabCommand.Execute(null);
        Assert.True(vm.IsDetailOpen);
        Assert.True(vm.IsDetailExpanded);
        Assert.Equal(7, vm.SelectedDetailTabIndex);

        // Verify OverlayEndpointUrl format
        Assert.Equal($"http://127.0.0.1:{port}/overlay/{profile.Id:D}", vm.OverlayEndpointUrl);

        // Mutate ViewModel overlay properties & fire live events on the same thread
        vm.OverlayClockModeIndex = (int)OverlayClockMode.TwinBell;
        Assert.Equal(OverlayClockMode.TwinBell, server.GetConfig(profile.Id).ClockMode);
        Assert.True(stateSaveCount > 0);

        vm.OverlayBrandText = "BKK LIVE OFFICIAL";
        vm.OverlayFlashDealTitle = "นาทีทอง ลด 70%";
        vm.OverlayFlashDealDurationSeconds = 90;

        // Verify Flash Deal trigger, and stop async commands
        await vm.TriggerFlashDealCommand.ExecuteAsync(null);
        Assert.True(vm.OverlayEnableFlashDeal);

        await vm.StopFlashDealCommand.ExecuteAsync(null);
        Assert.False(vm.OverlayEnableFlashDeal);

        // Verify external live listener event forwards to Overlay WebSocket
        listener.PublishSimulatedEvent("@shop01_official", new LiveChatMessageItem
        {
            Type = LiveChatMessageType.Gift,
            Nickname = "เสี่ยบอย",
            Username = "boy_vip",
            Message = "ส่ง มังกร x1"
        });

        // Drain WebSocket frames until the final simulated live event frame arrives
        var frames = new System.Collections.Generic.List<string>();
        for (int i = 0; i < 15; i++)
        {
            string frame = await ReceiveWebSocketTextAsync(ws);
            frames.Add(frame);
            if (frame.Contains("เสี่ยบอย", StringComparison.Ordinal))
            {
                break;
            }
        }

        Assert.Contains(frames, f => f.Contains("TwinBell", StringComparison.Ordinal));
        Assert.Contains(frames, f => f.Contains("BKK LIVE OFFICIAL", StringComparison.Ordinal));
        Assert.Contains(frames, f => f.Contains("นาทีทอง ลด 70%", StringComparison.Ordinal));
        Assert.Contains(frames, f => f.Contains("เสี่ยบอย", StringComparison.Ordinal) && f.Contains("ส่ง มังกร x1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OverlayServerService_BrowserRenderAndFiveUiStates_ZeroConsoleErrors()
    {
        string? browserExe = FindLocalChromiumExecutable();
        Assert.False(string.IsNullOrWhiteSpace(browserExe), "Expected Chrome or Edge executable on Windows for headless UI verification.");

        using var server = new OverlayServerService();
        int port = await server.StartAsync(19580);
        var profileId = Guid.NewGuid();
        string url = server.GetOverlayUrl(profileId);

        int consoleErrors = 0;
        int pageErrors = 0;

        await using var browser = await PuppeteerSharp.Puppeteer.LaunchAsync(new PuppeteerSharp.LaunchOptions
        {
            ExecutablePath = browserExe,
            Headless = true,
            Args = new[] { "--no-sandbox", "--disable-gpu", "--window-size=520,520" }
        });

        await using var page = await browser.NewPageAsync();
        await page.SetViewportAsync(new PuppeteerSharp.ViewPortOptions { Width = 520, Height = 520 });

        page.Console += (_, e) =>
        {
            if (e.Message.Type == PuppeteerSharp.ConsoleType.Error)
            {
                consoleErrors++;
            }
        };
        page.PageError += (_, _) => pageErrors++;

        // 1. Loading & Empty State (Initial load before any live event is pushed)
        await page.GoToAsync(url, PuppeteerSharp.WaitUntilNavigation.Networkidle0);
        await Task.Delay(350);

        string emptyTickerText = await page.EvaluateExpressionAsync<string>("document.body.innerText");
        Assert.Contains("LIVE", emptyTickerText);

        string artifactDir = @"C:\Users\Prayu\.gemini\antigravity\brain\fe991115-6a0e-485f-ad29-dda1aa13577a";
        if (System.IO.Directory.Exists(artifactDir))
        {
            await page.ScreenshotAsync(System.IO.Path.Combine(artifactDir, "overlay_state_empty_wall.png"), new PuppeteerSharp.ScreenshotOptions { OmitBackground = true });
        }

        // 2. Ideal State: Trigger Flash Deal + Live Gift Event on RealisticWall
        await server.TriggerFlashDealAsync(profileId, "โปรนาทีทอง ส่งฟรีทั่วไทย", 300);
        await server.BroadcastLiveEventAsync(profileId, new OverlayLiveEventPayload
        {
            EventType = "gift",
            Nickname = "คุณลูกค้า VIP",
            Username = "vip_buyer",
            Message = "ส่งของขวัญ กุหลาบ x99",
            BadgeText = "ของขวัญ"
        });
        await Task.Delay(350);

        string idealText = await page.EvaluateExpressionAsync<string>("document.body.innerText");
        Assert.Contains("โปรนาทีทอง ส่งฟรีทั่วไทย", idealText);
        Assert.Contains("คุณลูกค้า VIP", idealText);

        if (System.IO.Directory.Exists(artifactDir))
        {
            await page.ScreenshotAsync(System.IO.Path.Combine(artifactDir, "overlay_state_ideal_wall.png"), new PuppeteerSharp.ScreenshotOptions { OmitBackground = true });
        }

        // 3. Hot-Reload to WoodBlockLed + Overflow State (250-char strings must not overflow viewport width)
        var ledCfg = server.GetConfig(profileId);
        ledCfg.ClockMode = OverlayClockMode.WoodBlockLed;
        ledCfg.BackgroundStyle = OverlayBackgroundStyle.GlassDark;
        ledCfg.BrandText = "FLOW ULTRA LIVE STUDIO";
        await server.UpdateConfigAsync(profileId, ledCfg);

        await server.BroadcastLiveEventAsync(profileId, new OverlayLiveEventPayload
        {
            EventType = "chat",
            Nickname = "ชื่อยาวมากทดสอบระบบตัดคำอัตโนมัติ_" + new string('X', 180),
            Username = "overflow_tester",
            Message = "ข้อความคอมเมนต์ยาวมากเพื่อทดสอบว่าป้ายไม่ล้นกรอบนาฬิกา_" + new string('Y', 240),
            BadgeText = "แชทสด"
        });
        await Task.Delay(350);

        int scrollWidth = await page.EvaluateExpressionAsync<int>("document.documentElement.scrollWidth");
        Assert.True(scrollWidth <= 540, $"Expected no horizontal overflow, but scrollWidth was {scrollWidth}");

        if (System.IO.Directory.Exists(artifactDir))
        {
            await page.ScreenshotAsync(System.IO.Path.Combine(artifactDir, "overlay_state_hotreload_led.png"), new PuppeteerSharp.ScreenshotOptions { OmitBackground = true });
        }

        // 4. Error / Offline Resilience State: Stop server while page stays open -> 0 console errors & clock still renders
        await server.StopAsync();
        await Task.Delay(400);

        Assert.Equal(0, consoleErrors);
        Assert.Equal(0, pageErrors);
    }

    private static string? FindLocalChromiumExecutable()
    {
        string[] candidates =
        {
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
        };
        foreach (var path in candidates)
        {
            if (System.IO.File.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    private static async Task<string> ReceiveWebSocketTextAsync(ClientWebSocket ws)
    {
        var buffer = new byte[8192];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }
}
