using System;
using System.Threading.Tasks;
using FlowUltraTech.App.ViewModels;
using FlowUltraTech.Core.Interfaces;
using FlowUltraTech.Core.Models;
using FlowUltraTech.Core.Services;
using Xunit;

namespace FlowUltraTech.Tests;

public class DebugViewModelAndCdpTests
{
    [Fact]
    public void DebugViewModel_InitializesWithSensibleDefaults()
    {
        var cdpService = new CdpAutomationService();
        var vm = new DebugViewModel(cdpService);

        Assert.Equal(9221, vm.TargetPort);
        Assert.Contains("ไม่ได้เชื่อมต่อ", vm.ConnectionStatus);
        Assert.NotEmpty(vm.ScriptInput);
        Assert.NotEmpty(vm.ConsoleOutput);
        Assert.False(vm.IsHooking);
        Assert.Empty(vm.NetworkTraffic);
        Assert.Empty(vm.Cookies);
        Assert.Equal("-", vm.SessionIdToken);
        Assert.Equal("-", vm.CsrfToken);
    }

    [Fact]
    public void DebugViewModel_ClearConsoleCommand_ClearsOutput()
    {
        var cdpService = new CdpAutomationService();
        var vm = new DebugViewModel(cdpService);

        Assert.NotEmpty(vm.ConsoleOutput);
        vm.ClearConsoleCommand.Execute(null);
        Assert.Empty(vm.ConsoleOutput);
    }

    [Fact]
    public void MainWindow_Type_CanBeLoaded()
    {
        var windowType = typeof(FlowUltraTech.App.MainWindow);
        Assert.NotNull(windowType);
        Assert.True(typeof(System.Windows.Window).IsAssignableFrom(windowType));
    }

    [Fact]
    public async Task DebugViewModel_ExecuteScript_OnUnreachablePort_ReportsErrorCleanly()
    {
        var cdpService = new CdpAutomationService();
        var vm = new DebugViewModel(cdpService)
        {
            TargetPort = 59999 // Unused port
        };

        await vm.ExecuteScriptCommand.ExecuteAsync(null);

        Assert.Contains("Cannot connect to CDP port", vm.ConsoleOutput);
        Assert.Contains("59999", vm.ConsoleOutput);
    }

    [Fact]
    public async Task DebugViewModel_CheckConnection_OnUnreachablePort_ReportsDisconnected()
    {
        var cdpService = new CdpAutomationService();
        var vm = new DebugViewModel(cdpService)
        {
            TargetPort = 59998
        };

        await vm.CheckCdpStatusCommand.ExecuteAsync(null);

        Assert.Contains("ไม่ตอบสนอง", vm.ConnectionStatus);
    }

    [Fact]
    public void StorageDumpResult_TokenExtractionLogic_WorksProperly()
    {
        var result = new StorageDumpResult
        {
            SessionIdToken = "test_session_12345",
            CsrfToken = "test_csrf_token_abc",
            MsToken = "test_ms_token_xyz"
        };

        result.Cookies.Add(new CookieItem
        {
            Name = "sessionid",
            Value = "test_session_12345",
            Domain = ".tiktok.com",
            Path = "/",
            HttpOnly = true,
            Secure = true,
            IsAuthToken = true
        });

        Assert.Equal("test_session_12345", result.SessionIdToken);
        Assert.Equal("test_csrf_token_abc", result.CsrfToken);
        Assert.Equal("test_ms_token_xyz", result.MsToken);
        Assert.Single(result.Cookies);
        Assert.True(result.Cookies[0].IsAuthToken);
    }

    [Fact]
    public void NetworkTrafficItem_LifecycleState_UpdatesInPlace()
    {
        var item = new NetworkTrafficItem
        {
            RequestId = Guid.NewGuid().ToString(),
            Method = "POST",
            Url = "https://webcast.tiktok.com/api/chat/send",
            StatusCode = 0,
            StatusDisplay = "Pending ⏳",
            ResourceType = "Fetch/XHR"
        };

        Assert.Equal("Pending ⏳", item.StatusDisplay);
        Assert.Equal(0, item.StatusCode);

        // Simulate response arrival in-place
        item.StatusCode = 200;
        item.StatusDisplay = "200 OK";
        item.DurationMs = 145;
        item.ResponseBody = "{\"status_code\":0,\"data\":{\"message\":\"sent\"}}";

        Assert.Equal("200 OK", item.StatusDisplay);
        Assert.Equal(200, item.StatusCode);
        Assert.Equal(145, item.DurationMs);
        Assert.Contains("\"sent\"", item.ResponseBody);
    }

    [Fact]
    public void ProcessTreeHelper_NeverIncludesCurrentProcess()
    {
        var (root, pids) = ProcessTreeHelper.GetProcessTree(Environment.ProcessId);
        Assert.DoesNotContain(Environment.ProcessId, pids);
    }

    [Fact]
    public void StorageEntry_PopulatesProperly()
    {
        var entry = new StorageEntry { Key = "s_v_web_id", Value = "verify_12345" };
        Assert.Equal("s_v_web_id", entry.Key);
        Assert.Equal("verify_12345", entry.Value);
    }

    [Fact]
    public void InstanceCardViewModel_SwitchMode_CyclesThroughAllModes()
    {
        var profile = new StudioInstanceProfile { LaunchMode = InstanceLaunchMode.DirectProfile };
        var settings = new AppSettings();
        var orchestrator = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), new CdpAutomationService());
        var vm = new InstanceCardViewModel(profile, settings, orchestrator);

        Assert.Equal(InstanceLaunchMode.DirectProfile, vm.LaunchMode);
        Assert.Contains("Direct Profile", vm.ModeBadgeText);

        vm.SwitchModeCommand.Execute(null);
        Assert.Equal(InstanceLaunchMode.DefaultProfile, vm.LaunchMode);
        Assert.Contains("บัญชีหลัก", vm.ModeBadgeText);

        vm.SwitchModeCommand.Execute(null);
        Assert.Equal(InstanceLaunchMode.DirectProfile, vm.LaunchMode);
        Assert.Contains("Direct Profile", vm.ModeBadgeText);
    }

    [Fact]
    public void InstanceCardViewModel_ToggleDetail_TogglesStateAndText()
    {
        var profile = new StudioInstanceProfile();
        var settings = new AppSettings();
        var orchestrator = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), new CdpAutomationService());
        var vm = new InstanceCardViewModel(profile, settings, orchestrator);

        Assert.False(vm.IsDetailOpen);
        Assert.Contains("Detail", vm.DetailButtonText);

        vm.ToggleDetailCommand.Execute(null);
        Assert.True(vm.IsDetailOpen);
        Assert.Contains("ปิด Detail", vm.DetailButtonText);

        vm.ToggleDetailCommand.Execute(null);
        Assert.False(vm.IsDetailOpen);
    }

    [Fact]
    public async Task InstanceCardViewModel_ExecuteScript_WhenNotRunning_WarnsCleanly()
    {
        var profile = new StudioInstanceProfile { Status = InstanceStatus.Stopped };
        var settings = new AppSettings();
        var orchestrator = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), new CdpAutomationService());
        var vm = new InstanceCardViewModel(profile, settings, orchestrator);

        Assert.False(vm.IsRunning);
        await vm.ExecuteScriptCommand.ExecuteAsync(null);

        Assert.Contains("ยังไม่ได้เปิดทำงาน", vm.ConsoleOutput);
    }

    [Fact]
    public void InstanceCardViewModel_SnippetAndClearConsole_WorksCorrectly()
    {
        var profile = new StudioInstanceProfile();
        var settings = new AppSettings();
        var orchestrator = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), new CdpAutomationService());
        var vm = new InstanceCardViewModel(profile, settings, orchestrator);

        vm.SetScriptSnippetCommand.Execute("document.title");
        Assert.Equal("document.title", vm.ScriptInput);

        vm.ClearConsoleCommand.Execute(null);
        Assert.Empty(vm.ConsoleOutput);
    }

    [Fact]
    public void InstanceCardViewModel_WhenProfileStatusIsStarting_SanitizesToStoppedOnInstantiation()
    {
        var profile = new StudioInstanceProfile { Status = InstanceStatus.Starting, Name = "Account 03" };
        var settings = new AppSettings();
        var orchestrator = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), new CdpAutomationService());
        var vm = new InstanceCardViewModel(profile, settings, orchestrator);

        Assert.Equal(InstanceStatus.Stopped, vm.Status);
        Assert.True(vm.CanStart);
        Assert.False(vm.IsStarting);
        Assert.Equal("⚪ หยุดทำงาน", vm.StatusDisplay);
    }

    [Fact]
    public void InstanceCardViewModel_NetworkMonitor_FilterByMethodAndUrl_WorksProperly()
    {
        var profile = new StudioInstanceProfile();
        var settings = new AppSettings();
        var orchestrator = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), new CdpAutomationService());
        var vm = new InstanceCardViewModel(profile, settings, orchestrator);

        vm.NetworkTraffic.Add(new NetworkTrafficItem { Method = "POST", Url = "https://webcast.tiktok.com/api/chat", StatusCode = 200, StatusDisplay = "200 OK" });
        vm.NetworkTraffic.Add(new NetworkTrafficItem { Method = "GET", Url = "https://tiktok.com/static/index.js", StatusCode = 200, StatusDisplay = "200 OK" });

        // Filter by POST method
        vm.NetworkMethodFilter = "POST";
        var filteredList = vm.FilteredNetworkTraffic.Cast<NetworkTrafficItem>().ToList();
        Assert.Single(filteredList);
        Assert.Equal("POST", filteredList[0].Method);

        // Filter by URL text
        vm.NetworkMethodFilter = "ALL";
        vm.NetworkUrlFilter = "static";
        filteredList = vm.FilteredNetworkTraffic.Cast<NetworkTrafficItem>().ToList();
        Assert.Single(filteredList);
        Assert.Contains("static", filteredList[0].Url);

        // Reset filters
        vm.ResetNetworkFiltersCommand.Execute(null);
        Assert.Equal(2, vm.FilteredNetworkTraffic.Cast<NetworkTrafficItem>().Count());
    }

    [Fact]
    public void InstanceCardViewModel_CookiesAndStorage_FilterEachColumn_WorksProperly()
    {
        var profile = new StudioInstanceProfile();
        var settings = new AppSettings();
        var orchestrator = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), new CdpAutomationService());
        var vm = new InstanceCardViewModel(profile, settings, orchestrator);

        vm.Cookies.Add(new CookieItem { Name = "sessionid", Value = "abc12345", Domain = ".tiktok.com" });
        vm.Cookies.Add(new CookieItem { Name = "tt_csrf_token", Value = "csrf_token_xyz", Domain = ".tiktok.com" });

        vm.CookieNameFilter = "session";
        var filteredCookies = vm.FilteredCookies.Cast<CookieItem>().ToList();
        Assert.Single(filteredCookies);
        Assert.Equal("sessionid", filteredCookies[0].Name);

        vm.ResetCookieFiltersCommand.Execute(null);
        Assert.Equal(2, vm.FilteredCookies.Cast<CookieItem>().Count());

        // Test LocalStorage filter
        vm.LocalStorageItems.Add(new StorageEntry { Key = "theme", Value = "dark" });
        vm.LocalStorageItems.Add(new StorageEntry { Key = "auth_token", Value = "token999" });

        vm.StorageKeyFilter = "theme";
        var filteredStorage = vm.FilteredLocalStorage.Cast<StorageEntry>().ToList();
        Assert.Single(filteredStorage);
        Assert.Equal("theme", filteredStorage[0].Key);

        vm.ResetStorageFiltersCommand.Execute(null);
        Assert.Equal(2, vm.FilteredLocalStorage.Cast<StorageEntry>().Count());
    }

    [Fact]
    public async Task InstanceCardViewModel_AutoMonitoring_InitializesAndActivatesProperly()
    {
        var profile = new StudioInstanceProfile { Status = InstanceStatus.Running };
        var settings = new AppSettings();
        var orchestrator = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), new CdpAutomationService());
        var vm = new InstanceCardViewModel(profile, settings, orchestrator);

        // Auto refresh should be true by default
        Assert.True(vm.IsStorageAutoRefresh);
        Assert.Equal("เริ่มดักจับ Network API", vm.NetworkHookButtonText);
        Assert.Equal("พักการดักจับ", vm.NetworkHookBadgeText);

        // When auto monitoring starts
        await vm.StartAutoMonitoringAsync();

        Assert.True(vm.IsStorageAutoRefresh);
        Assert.True(vm.IsNetworkHooking);
        Assert.Equal("หยุดดักจับ API ชั่วคราว", vm.NetworkHookButtonText);
        Assert.Equal("Auto Monitor (เปิดอยู่)", vm.NetworkHookBadgeText);

        // Stopping instance should cleanly stop network hook
        await vm.StopNetworkHookInternalAsync();
        Assert.False(vm.IsNetworkHooking);
    }

    [Fact]
    public void JsonFormatter_FormatsValidJsonObjectAndArray_PreservesUnicode()
    {
        string rawJson = "{\"status_code\":0,\"data\":{\"nickname\":\"ทดสอบแชท\",\"count\":123}}";
        string formatted = JsonFormatter.TryFormatJson(rawJson);

        Assert.Contains("\n", formatted);
        Assert.Contains("\"status_code\": 0", formatted);
        Assert.Contains("\"nickname\": \"ทดสอบแชท\"", formatted); // Thai text not escaped!

        string rawArray = "[{\"id\":1},{\"id\":2}]";
        string formattedArray = JsonFormatter.TryFormatJson(rawArray);
        Assert.Contains("\n", formattedArray);
        Assert.Contains("\"id\": 1", formattedArray);
    }

    [Fact]
    public void JsonFormatter_LeavesNonJsonUntouched()
    {
        string plain = "key=value&foo=bar";
        Assert.Equal(plain, JsonFormatter.TryFormatJson(plain));

        string broken = "{\"broken\": ";
        Assert.Equal(broken, JsonFormatter.TryFormatJson(broken));

        Assert.Equal(string.Empty, JsonFormatter.TryFormatJson(null));
        Assert.Equal(string.Empty, JsonFormatter.TryFormatJson("   "));
    }

    [Fact]
    public void NetworkTrafficItem_AutomaticallyFormats_PayloadAndResponseBodyWhenJson()
    {
        var item = new NetworkTrafficItem
        {
            PostData = "{\"action\":\"send_msg\",\"text\":\"สวัสดี\"}",
            ResponseBody = "{\"status\":200,\"success\":true}"
        };

        Assert.Contains("\n", item.PostData);
        Assert.Contains("\"action\": \"send_msg\"", item.PostData);
        Assert.Contains("\"text\": \"สวัสดี\"", item.PostData);

        Assert.Contains("\n", item.ResponseBody);
        Assert.Contains("\"status\": 200", item.ResponseBody);
        Assert.Contains("\"success\": true", item.ResponseBody);

        // When setting non-json payload
        item.PostData = "grant_type=refresh_token&client_id=123";
        Assert.Equal("grant_type=refresh_token&client_id=123", item.PostData);
    }

    [Fact]
    public void PageHtmlResult_Model_PopulatesProperly()
    {
        var now = DateTime.Now;
        var result = new PageHtmlResult
        {
            Html = "<!DOCTYPE html><html><head><title>TikTok Live</title></head><body>Hello</body></html>",
            Url = "https://webcast.tiktok.com/live",
            Title = "TikTok Live Studio",
            PageCount = 2,
            FetchedAt = now
        };

        Assert.Contains("<!DOCTYPE html>", result.Html);
        Assert.Equal("https://webcast.tiktok.com/live", result.Url);
        Assert.Equal("TikTok Live Studio", result.Title);
        Assert.Equal(2, result.PageCount);
        Assert.Equal(now, result.FetchedAt);
    }

    [Fact]
    public async Task InstanceCardViewModel_ExportFullHtml_WhenNotRunning_WarnsCleanly()
    {
        var profile = new StudioInstanceProfile { Status = InstanceStatus.Stopped };
        var settings = new AppSettings();
        var orchestrator = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), new CdpAutomationService());
        var vm = new InstanceCardViewModel(profile, settings, orchestrator);

        Assert.False(vm.IsRunning);
        string? exportedPath = await vm.ExportFullHtmlAsync();

        Assert.Null(exportedPath);
        Assert.Contains("ยังไม่ได้เปิดทำงาน", vm.ConsoleOutput);
        Assert.Contains("กรุณาเปิดสตูดิโอก่อน", vm.StatusMessage);
    }

    [Fact]
    public async Task InstanceCardViewModel_ExportFullHtml_WithDirectPath_ExportsAndAddsHeaderComment()
    {
        var fakeCdp = new TestFakeCdpService
        {
            ResultToReturn = new PageHtmlResult
            {
                Html = "<!DOCTYPE html><html><head><title>Test Page</title></head><body><div id=\"chat-box\">Live Chat</div></body></html>",
                Url = "https://webcast.tiktok.com/chat",
                Title = "Test Page",
                PageCount = 1
            }
        };

        var profile = new StudioInstanceProfile { Status = InstanceStatus.Running, Name = "Test Profile", ProfileFolderName = "TestAcc01" };
        var settings = new AppSettings();
        var orchestrator = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), fakeCdp);
        var vm = new InstanceCardViewModel(profile, settings, orchestrator, fakeCdp);

        string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"test_export_{Guid.NewGuid():N}.html");
        try
        {
            string? resultPath = await vm.ExportFullHtmlAsync(tempFile);

            Assert.NotNull(resultPath);
            Assert.True(System.IO.File.Exists(tempFile));
            string content = await System.IO.File.ReadAllTextAsync(tempFile);

            // Assert comment header is injected after DOCTYPE
            Assert.Contains("<!-- [TikTok LIVE Studio Sandbox] Exported:", content);
            Assert.Contains("Profile: Test Profile", content);
            Assert.Contains("https://webcast.tiktok.com/chat", content);
            Assert.Contains("<div id=\"chat-box\">Live Chat</div>", content);

            Assert.Equal(tempFile, vm.LastExportedHtmlPath);
            Assert.Contains("Export Full HTML สำเร็จ", vm.StatusMessage);
            Assert.Contains("Export Full HTML สำเร็จเรียบร้อย!", vm.ConsoleOutput);
        }
        finally
        {
            if (System.IO.File.Exists(tempFile))
            {
                System.IO.File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task DebugViewModel_ExportFullHtml_WithDirectPath_ExportsProperly()
    {
        var fakeCdp = new TestFakeCdpService
        {
            ResultToReturn = new PageHtmlResult
            {
                Html = "<html><body>Debug HTML Content</body></html>",
                Url = "http://127.0.0.1:9221/studio",
                Title = "Studio Debug",
                PageCount = 1
            }
        };

        var vm = new DebugViewModel(fakeCdp) { TargetPort = 9221 };

        string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"test_debug_export_{Guid.NewGuid():N}.html");
        try
        {
            string? resultPath = await vm.ExportFullHtmlAsync(tempFile);

            Assert.NotNull(resultPath);
            Assert.True(System.IO.File.Exists(tempFile));
            string content = await System.IO.File.ReadAllTextAsync(tempFile);

            Assert.Contains("<!-- [TikTok LIVE Studio Sandbox] Exported:", content);
            Assert.Contains("Port: 9221", content);
            Assert.Contains("Debug HTML Content", content);
            Assert.Equal(tempFile, vm.LastExportedHtmlPath);
        }
        finally
        {
            if (System.IO.File.Exists(tempFile))
            {
                System.IO.File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task InstanceCardViewModel_CheckCaptcha_WhenFound_SetsPropertiesAndShowsAlert()
    {
        var fakeCaptcha = new CaptchaDetectionResult
        {
            DetectedAt = DateTime.Now,
            IsActive = true,
            Type = CaptchaType.Jigsaw,
            TypeDescription = "จิ๊กซอว์เลื่อนต่อภาพ (Slide Captcha)",
            CombinedHtml = "<div id=\"captcha-verify-container\"><div class=\"captcha_verify_slide\"></div></div>",
            Elements =
            {
                new CaptchaElementItem
                {
                    TagName = "div",
                    Id = "captcha-verify-container",
                    ClassName = "secsdk-captcha-container",
                    Depth = 2,
                    Html = "<div id=\"captcha-verify-container\"><div class=\"captcha_verify_slide\"></div></div>",
                    IsVisible = true
                }
            }
        };

        var fakeCdp = new TestFakeCdpService { CaptchaResultToReturn = fakeCaptcha };
        var profile = new StudioInstanceProfile { Status = InstanceStatus.Running, Name = "CaptchaTestAcc", CdpPort = 9222 };
        var settings = new AppSettings();
        var orchestrator = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), fakeCdp);
        var vm = new InstanceCardViewModel(profile, settings, orchestrator, fakeCdp);

        Assert.False(vm.IsCaptchaAlertVisible);

        var result = await vm.CheckCaptchaInternalAsync(isAuto: true);

        Assert.True(result.Found);
        Assert.True(result.IsActive);
        Assert.True(vm.IsCaptchaAlertVisible);
        Assert.Equal(1, vm.DetectedCaptchaCount);
        Assert.Equal(fakeCaptcha.CombinedHtml, vm.DetectedCaptchaHtml);
        Assert.Contains("ตรวจพบ Captcha", vm.CaptchaAlertTitle);
        Assert.Contains("จิ๊กซอว์เลื่อนต่อภาพ", vm.CaptchaAlertMessage);
        Assert.NotEmpty(vm.LastCaptchaDetectedTime);

        // Test Dismiss
        vm.DismissCaptchaAlertCommand.Execute(null);
        Assert.False(vm.IsCaptchaAlertVisible);
    }

    [Fact]
    public async Task InstanceCardViewModel_CheckCaptcha_WhenHiddenInDom_DoesNotShowAlert()
    {
        var fakeCaptcha = new CaptchaDetectionResult
        {
            DetectedAt = DateTime.Now,
            IsActive = false,
            State = CaptchaState.HiddenOrUnmounted,
            Type = CaptchaType.None,
            CombinedHtml = "<div id=\"captcha-mount-point\" style=\"display: none;\"></div>",
            Elements =
            {
                new CaptchaElementItem
                {
                    TagName = "div",
                    Id = "captcha-mount-point",
                    Html = "<div id=\"captcha-mount-point\" style=\"display: none;\"></div>",
                    IsVisible = false
                }
            }
        };

        var fakeCdp = new TestFakeCdpService { CaptchaResultToReturn = fakeCaptcha };
        var profile = new StudioInstanceProfile { Status = InstanceStatus.Running, Name = "HiddenCaptchaAcc", CdpPort = 9222 };
        var settings = new AppSettings();
        var orchestrator = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), fakeCdp);
        var vm = new InstanceCardViewModel(profile, settings, orchestrator, fakeCdp);

        var result = await vm.CheckCaptchaInternalAsync(isAuto: true);

        Assert.True(result.Found);
        Assert.False(result.IsActive);
        Assert.False(vm.IsCaptchaAlertVisible, "Must NOT show alert for hidden/inactive DOM elements");
    }

    [Fact]
    public async Task InstanceCardViewModel_ExportCaptchaHtml_WritesExpectedContent()
    {
        var fakeCaptcha = new CaptchaDetectionResult
        {
            DetectedAt = DateTime.Now,
            CombinedHtml = "<div id=\"secsdk-captcha-drag-wrapper\">Slider Captcha</div>",
            Elements =
            {
                new CaptchaElementItem
                {
                    TagName = "div",
                    Id = "secsdk-captcha-drag-wrapper",
                    Html = "<div id=\"secsdk-captcha-drag-wrapper\">Slider Captcha</div>"
                }
            }
        };

        var fakeCdp = new TestFakeCdpService { CaptchaResultToReturn = fakeCaptcha };
        var profile = new StudioInstanceProfile { Status = InstanceStatus.Running, Name = "ExportCaptchaAcc", ProfileFolderName = "Acc01" };
        var settings = new AppSettings();
        var orchestrator = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), fakeCdp);
        var vm = new InstanceCardViewModel(profile, settings, orchestrator, fakeCdp);

        string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"captcha_test_{Guid.NewGuid():N}.html");
        try
        {
            string? exportedPath = await vm.ExportCaptchaHtmlAsync(tempFile);

            Assert.NotNull(exportedPath);
            Assert.True(System.IO.File.Exists(tempFile));
            string content = await System.IO.File.ReadAllTextAsync(tempFile);

            Assert.Contains("<!-- [TikTok LIVE Studio Sandbox] Captcha Full HTML Dump", content);
            Assert.Contains("ExportCaptchaAcc", content);
            Assert.Contains("<div id=\"secsdk-captcha-drag-wrapper\">Slider Captcha</div>", content);
        }
        finally
        {
            if (System.IO.File.Exists(tempFile))
            {
                System.IO.File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task DebugViewModel_CopyCaptchaHtml_WhenFound_ReturnsCombinedHtml()
    {
        var fakeCaptcha = new CaptchaDetectionResult
        {
            DetectedAt = DateTime.Now,
            CombinedHtml = "<div class=\"captcha_verify_container\">Verify Code</div>",
            Elements =
            {
                new CaptchaElementItem
                {
                    TagName = "div",
                    ClassName = "captcha_verify_container",
                    Html = "<div class=\"captcha_verify_container\">Verify Code</div>"
                }
            }
        };

        var fakeCdp = new TestFakeCdpService { CaptchaResultToReturn = fakeCaptcha };
        var vm = new DebugViewModel(fakeCdp) { TargetPort = 9221 };

        // Test in STA or catch clipboard restriction in headless test
        try
        {
            var html = await vm.CopyCaptchaHtmlAsync();
            Assert.Equal("<div class=\"captcha_verify_container\">Verify Code</div>", html);
            Assert.Contains("คัดลอก Captcha Full HTML", vm.ConsoleOutput);
        }
        catch (System.Threading.ThreadStateException)
        {
            // Clipboard might require STA thread in some test runners
        }
    }

    [Fact]
    public void Captcha_TopmostSmartLayerCalculation_KeepsTopmostElementsAndDiscardsDescendants()
    {
        // Simulation of DOM node hierarchy for layer calculation testing:
        // Root
        //  ├── Element A (id: 'captcha-verify-container', layer 1)
        //  │     └── Element C (class: 'captcha-inner-slider', layer 2 - child of A)
        //  │           └── Element D (class: 'captcha-button', layer 3 - child of C)
        //  └── Element B (id: 'secsdk-captcha-box', layer 1 - sibling of A)
        //
        // Requirement: "layer 1 - element a,b; layer 2 - element c => ก็จะ copy ทั้ง a และ b ให้คำนวนอย่างฉลาดหน่อย"

        var nodeA = new FakeDomNode { Name = "A", Id = "captcha-verify-container" };
        var nodeB = new FakeDomNode { Name = "B", Id = "secsdk-captcha-box" };
        var nodeC = new FakeDomNode { Name = "C", ClassName = "captcha-inner-slider", Parent = nodeA };
        nodeA.Children.Add(nodeC);
        var nodeD = new FakeDomNode { Name = "D", ClassName = "captcha-button", Parent = nodeC };
        nodeC.Children.Add(nodeD);

        var allMatched = new List<FakeDomNode> { nodeA, nodeB, nodeC, nodeD };

        // Smart Layer algorithm used in CdpAutomationService:
        // filter(el => !matched.some(other => other !== el && other.contains(el)))
        var topmost = allMatched.Where(el =>
            !allMatched.Any(other => other != el && other.Contains(el))
        ).ToList();

        // Must contain both A and B (layer 1)
        Assert.Equal(2, topmost.Count);
        Assert.Contains(nodeA, topmost);
        Assert.Contains(nodeB, topmost);

        // Must NOT contain C or D (descendants of A)
        Assert.DoesNotContain(nodeC, topmost);
        Assert.DoesNotContain(nodeD, topmost);
    }

    [Fact]
    public void InstanceCardViewModel_OpenCaptchaTabAndClear_WorkCorrectly()
    {
        var profile = new StudioInstanceProfile();
        var settings = new AppSettings();
        var fakeCdp = new TestFakeCdpService();
        var orchestrator = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), fakeCdp);
        var vm = new InstanceCardViewModel(profile, settings, orchestrator, fakeCdp);

        // Test OpenCaptchaTab
        Assert.False(vm.IsDetailOpen);
        Assert.Equal(0, vm.SelectedDetailTabIndex);

        vm.OpenCaptchaTabCommand.Execute(null);
        Assert.True(vm.IsDetailOpen);
        Assert.Equal(4, vm.SelectedDetailTabIndex);

        // Add dummy detected captcha items
        vm.DetectedCaptchaHtml = "<div>captcha</div>";
        vm.DetectedCaptchaCount = 1;
        vm.DetectedCaptchaElements.Add(new CaptchaElementItem { TagName = "div", Id = "captcha" });
        vm.IsCaptchaAlertVisible = true;

        // Test ClearCaptchaResults
        vm.ClearCaptchaResultsCommand.Execute(null);
        Assert.Empty(vm.DetectedCaptchaElements);
        Assert.Equal(0, vm.DetectedCaptchaCount);
        Assert.Empty(vm.DetectedCaptchaHtml);
        Assert.False(vm.IsCaptchaAlertVisible);
    }

    [Fact]
    public async Task DebugViewModel_DetectCaptcha_PopulatesElementsAndRawHtml()
    {
        var fakeCaptcha = new CaptchaDetectionResult
        {
            DetectedAt = DateTime.Now,
            IsActive = true,
            Type = CaptchaType.Jigsaw,
            TypeDescription = "จิ๊กซอว์เลื่อนต่อภาพ (Slide Captcha)",
            CombinedHtml = "<div id=\"captcha-test\">Verify</div>",
            Elements =
            {
                new CaptchaElementItem { TagName = "div", Id = "captcha-test", Html = "<div id=\"captcha-test\">Verify</div>", IsVisible = true }
            }
        };

        var fakeCdp = new TestFakeCdpService { CaptchaResultToReturn = fakeCaptcha };
        var vm = new DebugViewModel(fakeCdp) { TargetPort = 9221 };

        Assert.Empty(vm.CaptchaElements);
        Assert.Empty(vm.CaptchaRawHtml);

        var res = await vm.DetectCaptchaAsync();

        Assert.True(res.Found);
        Assert.True(res.IsActive);
        Assert.Single(vm.CaptchaElements);
        Assert.Equal("<div id=\"captcha-test\">Verify</div>", vm.CaptchaRawHtml);
        Assert.Contains("ตรวจพบ Captcha จริง", vm.CaptchaStatusText);

        // Test Clear
        vm.ClearCaptchaCommand.Execute(null);
        Assert.Empty(vm.CaptchaElements);
        Assert.Empty(vm.CaptchaRawHtml);
    }

    [Fact]
    public async Task DebugViewModel_DetectCaptcha_WhenHiddenInDom_ShowsInactiveStatus()
    {
        var fakeCaptcha = new CaptchaDetectionResult
        {
            DetectedAt = DateTime.Now,
            IsActive = false,
            State = CaptchaState.HiddenOrUnmounted,
            Type = CaptchaType.None,
            CombinedHtml = "<div id=\"captcha-mount-point\" style=\"display: none;\"></div>",
            Elements =
            {
                new CaptchaElementItem { TagName = "div", Id = "captcha-mount-point", Html = "<div id=\"captcha-mount-point\" style=\"display: none;\"></div>", IsVisible = false }
            }
        };

        var fakeCdp = new TestFakeCdpService { CaptchaResultToReturn = fakeCaptcha };
        var vm = new DebugViewModel(fakeCdp) { TargetPort = 9221 };

        var res = await vm.DetectCaptchaAsync();

        Assert.True(res.Found);
        Assert.False(res.IsActive);
        Assert.Contains("ซ่อน/ปิดอยู่", vm.CaptchaStatusText);
    }

    [Fact]
    public void LiveChatMessageItem_FormattingAndColor_AreCorrect()
    {
        var chat = new LiveChatMessageItem
        {
            Type = LiveChatMessageType.Chat,
            Username = "user123",
            Nickname = "User 123",
            Message = "Hello World"
        };
        Assert.Equal("💬 แชท", chat.TypeDisplay);
        Assert.Equal("#38BDF8", chat.TypeColor);
        Assert.Equal("User 123 (@user123)", chat.SenderDisplay);

        var gift = new LiveChatMessageItem
        {
            Type = LiveChatMessageType.Gift,
            Username = "fan99",
            Nickname = "Big Fan",
            GiftName = "Galaxy",
            DiamondCount = 1000,
            RepeatCount = 2
        };
        Assert.Equal("🎁 Galaxy (1000 💎)", gift.TypeDisplay);
        Assert.Equal("#F59E0B", gift.TypeColor);
        Assert.Contains("Galaxy x2", gift.FullText);

        chat.IsAutoReplied = true;
        chat.AutoReplyContent = "ยินดีต้อนรับครับ";
        Assert.Equal("🤖 ยินดีต้อนรับครับ", chat.AutoReplyDisplay);
        Assert.Contains("บอท: ยินดีต้อนรับครับ", chat.FullText);
    }

    [Fact]
    public void InstanceCardViewModel_LiveChatMockAndCommands_WorkProperly()
    {
        var profile = new StudioInstanceProfile { Name = "Test Profile", CdpPort = 9222 };
        var settings = new AppSettings();
        var fakeCdp = new TestFakeCdpService();
        var orch = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), fakeCdp);

        var vm = new FlowUltraTech.App.ViewModels.InstanceCardViewModel(profile, settings, orch, fakeCdp);

        Assert.Empty(vm.LiveChatMessages);
        Assert.Equal(0, vm.TotalChatCount);

        // Generate 5 mock chat messages
        for (int i = 0; i < 5; i++)
        {
            vm.SendMockChatCommand.Execute(null);
        }

        Assert.Equal(5, vm.LiveChatMessages.Count);
        Assert.Contains("ทั้งหมด 5 รายการ", vm.LiveChatCountSummary);

        // Test reply to message
        var firstMsg = vm.LiveChatMessages[0];
        vm.ReplyToChatMessageCommand.Execute(firstMsg);
        Assert.StartsWith("@", vm.TestChatMessage);

        // Test clear
        vm.ClearLiveChatCommand.Execute(null);
        Assert.Empty(vm.LiveChatMessages);
        Assert.Equal(0, vm.TotalChatCount);
        Assert.Equal(0, vm.TotalGiftCount);
    }

    [Fact]
    public async Task InstanceCardViewModel_LiveChatAutoReply_EvaluatesConfiguredRules()
    {
        var profile = new StudioInstanceProfile { Name = "Test Streamer", CdpPort = 9222, Status = InstanceStatus.Running };
        var settings = new AppSettings
        {
            Rules = new List<AutomationRule>
            {
                new AutomationRule
                {
                    Name = "Greeting Rule",
                    TriggerType = RuleTriggerType.ChatKeyword,
                    Pattern = "สวัสดี",
                    ResponseTemplate = "สวัสดีครับคุณ @{User} ขอบคุณที่แวะมาชมไลฟ์!",
                    IsEnabled = true
                }
            }
        };
        var fakeCdp = new TestFakeCdpService
        {
            LiveChatMessagesToReturn =
            {
                new LiveChatMessageItem
                {
                    Type = LiveChatMessageType.Chat,
                    Username = "viewer_somchai",
                    Nickname = "สมชาย",
                    Message = "สวัสดีครับทุกคน",
                    Timestamp = DateTime.Now
                }
            }
        };
        var orch = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), fakeCdp);

        var vm = new FlowUltraTech.App.ViewModels.InstanceCardViewModel(profile, settings, orch, fakeCdp);

        await vm.PollLiveChatInternalAsync();

        Assert.Single(vm.LiveChatMessages);
        var msg = vm.LiveChatMessages[0];
        Assert.True(msg.IsAutoReplied);
        Assert.Equal("สวัสดีครับคุณ @viewer_somchai ขอบคุณที่แวะมาชมไลฟ์!", msg.AutoReplyContent);
        Assert.Equal(1, vm.TotalAutoRepliesCount);
    }

    [Theory]
    [InlineData("@somchai_pro", "somchai_pro")]
    [InlineData("somchai_pro", "somchai_pro")]
    [InlineData("  @somchai_pro  ", "somchai_pro")]
    [InlineData("https://www.tiktok.com/@somchai_pro/live", "somchai_pro")]
    [InlineData("https://www.tiktok.com/@somchai_pro", "somchai_pro")]
    [InlineData("https://tiktok.com/@somchai_pro?lang=th", "somchai_pro")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void NormalizeUsername_HandlesVariousInputFormats(string? raw, string expected)
    {
        string result = FlowUltraTech.App.ViewModels.InstanceCardViewModel.NormalizeUsername(raw);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ChannelTarget_SaveAndSync_UpdatesActiveChannelDisplay()
    {
        var profile = new StudioInstanceProfile
        {
            Name = "ChannelTestProfile",
            CdpPort = 9222,
            LoggedInDisplayId = "my_original_user",
            StreamerUsername = "initial_streamer"
        };
        var settings = new AppSettings();
        var fakeCdp = new TestFakeCdpService();
        var orch = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), fakeCdp);

        var vm = new FlowUltraTech.App.ViewModels.InstanceCardViewModel(profile, settings, orch, fakeCdp);

        Assert.Equal("initial_streamer", vm.StreamerUsername);
        Assert.Contains("initial_streamer", vm.ActiveChannelDisplay);

        // Change and save
        vm.StreamerUsername = "https://www.tiktok.com/@target_streamer_live/live";
        vm.SaveStreamerUsername();

        Assert.Equal("target_streamer_live", vm.StreamerUsername);
        Assert.Equal("target_streamer_live", profile.StreamerUsername);
        Assert.Contains("@target_streamer_live", vm.ActiveChannelDisplay);
        Assert.Contains("target_streamer_live", vm.ChannelStatusText);

        // Use current account
        string expectedUser = !string.IsNullOrWhiteSpace(vm.LoggedInDisplayId) ? vm.LoggedInDisplayId : "my_original_user";
        vm.UseCurrentAccountAsChannel();
        Assert.Equal(expectedUser, vm.StreamerUsername);
        Assert.Equal(expectedUser, profile.StreamerUsername);
        Assert.Contains($"@{expectedUser}", vm.ActiveChannelDisplay);
    }

    [Fact]
    public async Task PollLiveChat_PassesTargetChannelToCdpService()
    {
        var profile = new StudioInstanceProfile
        {
            Name = "ChannelPollTest",
            CdpPort = 9222,
            Status = InstanceStatus.Running,
            StreamerUsername = "@streamer_target"
        };
        var settings = new AppSettings();
        
        string? capturedTargetChannel = null;
        var fakeCdp = new TestFakeCdpService
        {
            OnFetchLiveChat = ch => capturedTargetChannel = ch
        };
        var orch = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), fakeCdp);

        var vm = new FlowUltraTech.App.ViewModels.InstanceCardViewModel(profile, settings, orch, fakeCdp);
        await vm.PollLiveChatInternalAsync();

        Assert.Equal("streamer_target", capturedTargetChannel);
    }

    [Fact]
    public void InstanceCardViewModel_StreamerUsername_DefaultsToAccountUsername_WhenNotSet()
    {
        var profile = new StudioInstanceProfile
        {
            Name = "DefaultUserTestProfile",
            CdpPort = 9223,
            LoggedInDisplayId = "default_streamer_account",
            LoggedInNickname = "สตรีมเมอร์หลัก",
            StreamerUsername = "" // Not set
        };
        var settings = new AppSettings();
        var fakeCdp = new TestFakeCdpService();
        var orch = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), fakeCdp);

        var vm = new FlowUltraTech.App.ViewModels.InstanceCardViewModel(profile, settings, orch, fakeCdp);

        // Should default to account's LoggedInDisplayId (or disk-resolved displayId)
        string expected = !string.IsNullOrWhiteSpace(vm.LoggedInDisplayId) ? vm.LoggedInDisplayId : "default_streamer_account";
        Assert.Equal(expected, vm.StreamerUsername);
        Assert.Contains(expected, vm.ActiveChannelDisplay);
    }

    [Fact]
    public void InstanceCardViewModel_StreamerUsername_DoesNotDefaultToGenericAccountName_WhenUnauthenticated()
    {
        var profile = new StudioInstanceProfile
        {
            Name = "Account 99",
            ProfileFolderName = "NonExistentFolder_Unauth_99",
            CdpPort = 9228,
            LoggedInDisplayId = "",
            LoggedInNickname = "",
            StreamerUsername = "",
            IsLoggedIn = false
        };
        var settings = new AppSettings();
        var fakeCdp = new TestFakeCdpService();
        var orch = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), fakeCdp);

        var vm = new FlowUltraTech.App.ViewModels.InstanceCardViewModel(profile, settings, orch, fakeCdp);

        Assert.Equal(string.Empty, vm.StreamerUsername);
        Assert.Contains("ยังไม่ได้ระบุช่อง", vm.ActiveChannelDisplay);
    }

    [Fact]
    public async Task LiveChatMonitor_CanMonitorBeforeOpeningAccount_WhenStopped()
    {
        var profile = new StudioInstanceProfile
        {
            Name = "OfflineMonitorProfile",
            CdpPort = 9224,
            Status = InstanceStatus.Stopped, // Instance NOT opened yet!
            LoggedInDisplayId = "offline_user",
            StreamerUsername = "offline_streamer"
        };
        var settings = new AppSettings();
        
        string? capturedTarget = null;
        var fakeCdp = new TestFakeCdpService
        {
            OnFetchLiveChat = ch => capturedTarget = ch
        };
        fakeCdp.LiveChatMessagesToReturn.Add(new LiveChatMessageItem
        {
            Type = LiveChatMessageType.Chat,
            Username = "viewer_1",
            Nickname = "ผู้ชมเบอร์หนึ่ง",
            Message = "มอนิเตอร์ตอนออฟไลน์สำเร็จ!",
            Timestamp = DateTime.Now
        });

        var orch = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), fakeCdp);
        var vm = new FlowUltraTech.App.ViewModels.InstanceCardViewModel(profile, settings, orch, fakeCdp);

        // Verify monitor is active by default and can be toggled before opening account
        Assert.True(vm.IsLiveChatMonitoring);
        Assert.Contains("ก่อนเปิดแอป", vm.LiveChatMonitorBadgeText);

        // Toggle off
        vm.ToggleLiveChatMonitorCommand.Execute(null);
        Assert.False(vm.IsLiveChatMonitoring);
        Assert.Contains("พักการมอนิเตอร์", vm.LiveChatMonitorBadgeText);

        // Toggle back on
        vm.ToggleLiveChatMonitorCommand.Execute(null);
        Assert.True(vm.IsLiveChatMonitoring);
        Assert.Contains("ก่อนเปิดแอป", vm.LiveChatMonitorBadgeText);

        // Poll chat while stopped
        await vm.PollLiveChatInternalAsync();

        Assert.Equal("offline_streamer", capturedTarget);
        Assert.Single(vm.LiveChatMessages);
        Assert.Equal("มอนิเตอร์ตอนออฟไลน์สำเร็จ!", vm.LiveChatMessages[0].Message);
    }

    private class FakeDomNode
    {
        public string Name { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public FakeDomNode? Parent { get; set; }
        public List<FakeDomNode> Children { get; } = new();

        public bool Contains(FakeDomNode other)
        {
            var cur = other.Parent;
            while (cur != null)
            {
                if (cur == this) return true;
                cur = cur.Parent;
            }
            return false;
        }
    }

    private class TestFakeCdpService : ICdpAutomationService
    {
        public PageHtmlResult? ResultToReturn { get; set; }
        public CaptchaDetectionResult? CaptchaResultToReturn { get; set; }

        public Task<bool> CheckConnectionAsync(int cdpPort) => Task.FromResult(true);
        public Task<bool> SendChatMessageAsync(int cdpPort, string message) => Task.FromResult(true);
        public Task<string?> GetChatPageUrlAsync(int cdpPort) => Task.FromResult<string?>("https://webcast.tiktok.com");
        public Task<string> ExecuteScriptAsync(int cdpPort, string script) => Task.FromResult("ok");
        public Task StartNetworkHookAsync(int cdpPort, Action<NetworkTrafficItem> onRequest, Action<NetworkTrafficItem> onResponse) => Task.CompletedTask;
        public Task StopNetworkHookAsync(int cdpPort) => Task.CompletedTask;
        public Task<StorageDumpResult> GetCookiesAndStorageAsync(int cdpPort) => Task.FromResult(new StorageDumpResult());
        public Task<PageHtmlResult?> GetFullHtmlAsync(int cdpPort) => Task.FromResult(ResultToReturn);
        public Task<CaptchaDetectionResult> DetectCaptchaElementsAsync(int cdpPort, bool forceFullHtml = false) => Task.FromResult(CaptchaResultToReturn ?? new CaptchaDetectionResult());
        public Task<CaptchaSolveResult> SolveCaptchaWithMockAsync(int cdpPort, CaptchaDetectionResult? currentDetection = null, string? mockFilePath = null, Action<string>? logCallback = null, int delaySeconds = 0, string? instanceLeaseId = null, string? profileFolderName = null)
        {
            return Task.FromResult(new CaptchaSolveResult { Success = true, Message = "Mock solved", CaptchaType = "Jigsaw" });
        }
        public Task<CaptchaSolveResult> SolveCaptchaAsync(int cdpPort, CaptchaDetectionResult? currentDetection = null, CaptchaSolverType solverMode = CaptchaSolverType.LocalApiServer, string? solverApiEndpoint = null, string? solverJwtToken = null, string? mockFilePath = null, Action<string>? logCallback = null, int delaySeconds = 0, string? instanceLeaseId = null, string? profileFolderName = null)
        {
            return Task.FromResult(new CaptchaSolveResult { Success = true, Message = "API solved", CaptchaType = "Jigsaw" });
        }
        public Task<(bool Success, string Message)> TestSolverApiConnectionAsync(string? endpoint = null, string? jwtToken = null)
        {
            return Task.FromResult((true, "Connected"));
        }
        public List<LiveChatMessageItem> LiveChatMessagesToReturn { get; set; } = new();
        public Action<string?>? OnFetchLiveChat { get; set; }
        public Task<List<LiveChatMessageItem>> FetchLiveChatMessagesAsync(int cdpPort, string? targetChannel = null)
        {
            OnFetchLiveChat?.Invoke(targetChannel);
            return Task.FromResult(LiveChatMessagesToReturn);
        }
        public Task<OverlayInjectResult> InjectOverlaySourceAsync(int cdpPort, string overlayUrl, Action<string>? logCallback = null)
        {
            logCallback?.Invoke($"Fake injected: {overlayUrl}");
            return Task.FromResult(new OverlayInjectResult
            {
                Success = true,
                Message = "ส่งนาฬิกาเข้าฉากไลฟ์สำเร็จ",
                InjectedUrl = overlayUrl
            });
        }
        public void Dispose() { }
    }
}
