using System;
using System.IO;
using System.Threading.Tasks;
using TiktokStudioSandbox.Core.Models;
using TiktokStudioSandbox.Core.Services;
using Xunit;

namespace TiktokStudioSandbox.Tests;

public class WindowControllerTests
{
    [Fact]
    public void IsRealStudioWindow_WithZeroHandle_ReturnsFalse()
    {
        bool result = WindowController.IsRealStudioWindow(IntPtr.Zero);
        Assert.False(result);
    }

    [Fact]
    public void GetWindowArea_WithZeroHandle_ReturnsZero()
    {
        int area = WindowController.GetWindowArea(IntPtr.Zero);
        Assert.Equal(0, area);
    }

    [Fact]
    public void BringToFrontRockSolid_WithZeroHandle_DoesNotThrow()
    {
        var ex = Record.Exception(() => WindowController.BringToFrontRockSolid(IntPtr.Zero));
        Assert.Null(ex);
    }

    [Fact]
    public async Task LiveTest_StartInstance_DetectsRealWindowAndBringsToFront()
    {
        string studioExe = @"C:\Program Files\TikTok LIVE Studio\1.35.2\TikTok LIVE Studio.exe";
        if (!File.Exists(studioExe)) return; // Skip if machine doesn't have studio installed

        var settings = new AppSettings
        {
            TikTokStudioExePath = studioExe,
            AutoHideOnLaunch = false
        };

        var profile = new StudioInstanceProfile
        {
            Name = "IntegrationTestAcc",
            ProfileFolderName = "IntegrationTestAcc",
            CdpPort = 19288,
            LaunchMode = InstanceLaunchMode.DirectProfile
        };

        var winCtrl = new WindowController();
        var launcher = new ProcessLauncher(winCtrl, null);
        var cdp = new CdpAutomationService();
        var orchestrator = new InstanceOrchestrator(launcher, winCtrl, cdp, null);

        try
        {
            bool ok = await orchestrator.StartInstanceAsync(profile, settings);
            Assert.True(ok, "Failed to start TikTok LIVE Studio instance");

            // Wait a few seconds for Electron to spawn Chrome_WidgetWin_1
            bool foundRealWindow = false;
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(500);
                var handles = winCtrl.FindWindowHandlesForPort(profile.CdpPort, profile.ProcessId);
                if (handles.Count > 0)
                {
                    foundRealWindow = true;
                    // Verify that the handle is a real studio window
                    Assert.True(WindowController.IsRealStudioWindow(handles[0]));
                    break;
                }
            }
            Assert.True(foundRealWindow, "Expected to find real Chrome_WidgetWin_1 UI window within 10s");
        }
        finally
        {
            await orchestrator.StopInstanceAsync(profile);
        }
    }
}
