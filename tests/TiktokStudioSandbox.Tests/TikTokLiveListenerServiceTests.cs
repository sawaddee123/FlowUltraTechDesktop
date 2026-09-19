using System;
using System.Threading;
using System.Threading.Tasks;
using TiktokStudioSandbox.Core.Interfaces;
using TiktokStudioSandbox.Core.Models;
using TiktokStudioSandbox.Core.Services;
using Xunit;

namespace TiktokStudioSandbox.Tests;

public class TikTokLiveListenerServiceTests
{
    [Fact]
    public async Task StartAsync_WithEmptyOrInvalidUsername_ReturnsFalseAndReportsStatus()
    {
        var service = new TikTokLiveListenerService();
        string reportedStatus = string.Empty;

        bool result = await service.StartAsync(
            uniqueId: "   ",
            onEventReceived: _ => { },
            onStatusChanged: status => reportedStatus = status);

        Assert.False(result);
        Assert.Equal("ชื่อช่องไม่ถูกต้อง", reportedStatus);
    }

    [Fact]
    public void IsConnected_ReturnsFalseForUnknownOrUnconnectedChannel()
    {
        var service = new TikTokLiveListenerService();

        bool connected = service.IsConnected("non_existent_streamer_999");

        Assert.False(connected);
    }

    [Fact]
    public void GetCurrentRoomId_ReturnsNullWhenNotConnected()
    {
        var service = new TikTokLiveListenerService();

        string? roomId = service.GetCurrentRoomId("non_existent_streamer_999");

        Assert.Null(roomId);
    }

    [Fact]
    public async Task StopAsync_WhenChannelNotActive_DoesNotThrow()
    {
        var service = new TikTokLiveListenerService();

        var exception = await Record.ExceptionAsync(() => service.StopAsync("@random_offline_user"));

        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_WhenCalled_DoesNotThrow()
    {
        var service = new TikTokLiveListenerService();

        var exception = Record.Exception(() => service.Dispose());

        Assert.Null(exception);
    }

    [Fact]
    public void LiveChatMessageItem_FormattedJson_ProducesValidJsonStructure()
    {
        var item = new LiveChatMessageItem
        {
            Type = LiveChatMessageType.Chat,
            Username = "viewer_123",
            Nickname = "Friendly Viewer",
            Message = "Hello streamer!",
            Timestamp = new DateTime(2026, 9, 19, 23, 0, 0)
        };

        string json = item.FormattedJson;

        Assert.Contains("viewer_123", json);
        Assert.Contains("Friendly Viewer", json);
        Assert.Contains("Hello streamer!", json);
        Assert.Contains("Chat", json);
    }

    [Fact]
    public void LiveChatMessageItem_FormattedJson_WithRawJson_FormatsRawJson()
    {
        var item = new LiveChatMessageItem
        {
            RawJson = "{\"field\":\"value\"}"
        };

        string json = item.FormattedJson;

        Assert.Contains("\"field\": \"value\"", json);
    }

    [Fact]
    public void LiveChatMessageItem_FormattedJson_WithThaiUnicodeEscape_UnescapesToReadableThai()
    {
        var item = new LiveChatMessageItem
        {
            RawJson = "{\"type\":\"join\",\"nickname\":\"\\u0E2A\\u0E01\\u0E32\\u0E22\",\"username\":\"user3765151828539\"}"
        };

        string json = item.FormattedJson;

        Assert.Contains("\"nickname\": \"สกาย\"", json);
        Assert.DoesNotContain("\\u0E2A\\u0E01\\u0E32\\u0E22", json);
    }

    [Fact]
    public void LiveChatMessageItem_FormattedJson_WithThaiObjectFields_PreservesReadableThai()
    {
        var item = new LiveChatMessageItem
        {
            Nickname = "สกาย",
            Username = "sky_user",
            Message = "สวัสดีครับ"
        };

        string json = item.FormattedJson;

        Assert.Contains("\"nickname\": \"สกาย\"", json);
        Assert.Contains("\"message\": \"สวัสดีครับ\"", json);
        Assert.DoesNotContain("\\u0E", json);
    }

    [Fact]
    public void InstanceCardViewModel_FilterCheckboxes_DefaultsToTrueAndCanSelectAll()
    {
        var profile = new StudioInstanceProfile { Name = "Test Acc", ProfileFolderName = "test", CdpPort = 9222 };
        var settings = new AppSettings();
        var orchestrator = new InstanceOrchestrator(new ProcessLauncher(), new WindowController(), new CdpAutomationService());
        var vm = new TiktokStudioSandbox.App.ViewModels.InstanceCardViewModel(profile, settings, orchestrator);

        Assert.True(vm.FilterShowChat);
        Assert.True(vm.FilterShowGift);
        Assert.True(vm.FilterShowLikeFollow);
        Assert.True(vm.FilterShowAutoReply);
        Assert.True(vm.FilterShowOther);

        vm.FilterShowOther = false;
        Assert.False(vm.FilterShowOther);

        vm.SelectAllFilters();
        Assert.True(vm.FilterShowOther);
    }
}
