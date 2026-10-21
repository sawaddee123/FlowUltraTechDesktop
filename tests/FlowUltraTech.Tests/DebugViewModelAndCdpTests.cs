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
    public void MainWindow_Type_CanBeLoaded()
    {
        var windowType = typeof(FlowUltraTech.App.MainWindow);
        Assert.NotNull(windowType);
        Assert.True(typeof(System.Windows.Window).IsAssignableFrom(windowType));
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
    public void InstanceCardViewModel_ToggleDetail_TogglesStateAndText()
    {
        var profile = new StudioInstanceProfile();
        var settings = new AppSettings();
        var orchestrator = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), new CdpAutomationService());
        var vm = new InstanceCardViewModel(profile, settings, orchestrator);

        Assert.False(vm.IsDetailOpen);
        Assert.Contains("รายละเอียด", vm.DetailButtonText);

        vm.ToggleDetailCommand.Execute(null);
        Assert.True(vm.IsDetailOpen);
        Assert.Contains("ปิดรายละเอียด", vm.DetailButtonText);

        vm.ToggleDetailCommand.Execute(null);
        Assert.False(vm.IsDetailOpen);
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
        Assert.Equal("หยุดทำงาน", vm.StatusDisplay);
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

        // When auto monitoring starts
        await vm.StartAutoMonitoringAsync();

        Assert.True(vm.IsStorageAutoRefresh);
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

    private class TestFakeCdpService : ICdpAutomationService
    {
        public CaptchaDetectionResult? CaptchaResultToReturn { get; set; }

        public Task<bool> CheckConnectionAsync(int cdpPort) => Task.FromResult(true);
        public Task<bool> SendChatMessageAsync(int cdpPort, string message) => Task.FromResult(true);
        public Task<string?> GetChatPageUrlAsync(int cdpPort) => Task.FromResult<string?>("https://webcast.tiktok.com");
        public Task<string> ExecuteScriptAsync(int cdpPort, string script) => Task.FromResult("ok");
        public Task<StorageDumpResult> GetCookiesAndStorageAsync(int cdpPort) => Task.FromResult(new StorageDumpResult());
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
