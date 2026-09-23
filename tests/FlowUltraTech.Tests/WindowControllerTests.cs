using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FlowUltraTech.Core.Models;
using FlowUltraTech.Core.Native;
using FlowUltraTech.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace FlowUltraTech.Tests;

public class WindowControllerTests
{
    private readonly ITestOutputHelper _output;

    public WindowControllerTests(ITestOutputHelper output)
    {
        _output = output;
    }

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
        string studioExe = TikTokStudioPathResolver.ResolveValidStudioPath(null);
        if (!File.Exists(studioExe))
        {
            _output.WriteLine($"[SKIP] Studio executable not found at: {studioExe}");
            return;
        }
        _output.WriteLine($"[INFO] Using TikTok LIVE Studio executable: {studioExe}");

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
            _output.WriteLine($"[1] Starting instance on port {profile.CdpPort}...");
            bool ok = await orchestrator.StartInstanceAsync(profile, settings);
            Assert.True(ok, "Failed to start TikTok LIVE Studio instance");
            _output.WriteLine($"[1] Started instance successfully. PID: {profile.ProcessId}");

            // Monitor window appearance and lifecycle over 18 seconds (orchestrator reinforcement completes by 15s)
            bool foundRealWindow = false;
            for (int i = 0; i < 18; i++)
            {
                await Task.Delay(1000);
                var handles = winCtrl.FindWindowHandlesForPort(profile.CdpPort, profile.ProcessId);
                _output.WriteLine($"--- Second {i + 1}: Found {handles.Count} handle(s) ---");

                for (int hIdx = 0; hIdx < handles.Count; hIdx++)
                {
                    var hWnd = handles[hIdx];
                    var sb = new StringBuilder(256);
                    Win32.GetWindowText(hWnd, sb, 256);
                    string title = sb.ToString();

                    var sbClass = new StringBuilder(256);
                    Win32.GetClassName(hWnd, sbClass, 256);
                    string className = sbClass.ToString();

                    Win32.GetWindowRect(hWnd, out var rect);
                    int score = WindowController.GetWindowScore(hWnd);
                    bool isVisible = Win32.IsWindowVisible(hWnd);
                    bool isReal = WindowController.IsRealStudioWindow(hWnd);

                    _output.WriteLine($"  [Handle #{hIdx}] HWND: {hWnd} | Class: {className} | Title: '{title}' | Size: {rect.Width}x{rect.Height} | Visible: {isVisible} | Score: {score} | IsReal: {isReal}");
                }

                if (handles.Count > 0 && WindowController.IsRealStudioWindow(handles[0]))
                {
                    foundRealWindow = true;
                }
            }

            Assert.True(foundRealWindow, "Expected to find real TikTok Studio UI window within 15s");

            // Test Hide
            _output.WriteLine("[2] Testing Window Hide...");
            bool hideOk = winCtrl.SetWindowVisibilityForInstance(profile.ProcessId, profile.CdpPort, false);
            _output.WriteLine($"[2] SetWindowVisibility(false) result: {hideOk}");
            await Task.Delay(500);

            // Verify visibility after hide
            var handlesAfterHide = winCtrl.FindWindowHandlesForPort(profile.CdpPort, profile.ProcessId);
            Assert.NotEmpty(handlesAfterHide);
            var primaryAfterHide = handlesAfterHide[0];
            _output.WriteLine($"  Primary After Hide HWND: {primaryAfterHide}, Visible: {Win32.IsWindowVisible(primaryAfterHide)}, Iconic: {Win32.IsIconic(primaryAfterHide)}");

            // Test Show
            _output.WriteLine("[3] Testing Window Show (Primary only)...");
            bool showOk = winCtrl.SetWindowVisibilityForInstance(profile.ProcessId, profile.CdpPort, true);
            _output.WriteLine($"[3] SetWindowVisibility(true) result: {showOk}");
            await Task.Delay(1000);

            var handlesAfterShow = winCtrl.FindWindowHandlesForPort(profile.CdpPort, profile.ProcessId);
            Assert.NotEmpty(handlesAfterShow);
            var primaryAfterShow = handlesAfterShow[0];
            _output.WriteLine($"[3] Primary window HWND: {primaryAfterShow}, Visible: {Win32.IsWindowVisible(primaryAfterShow)}, Iconic: {Win32.IsIconic(primaryAfterShow)}");
            Assert.True(Win32.IsWindowVisible(primaryAfterShow) && !Win32.IsIconic(primaryAfterShow), "Primary window should be restored and visible after Show");
            Assert.Equal(primaryAfterHide, primaryAfterShow);
        }
        finally
        {
            _output.WriteLine("[4] Stopping instance and terminating processes...");
            await orchestrator.StopInstanceAsync(profile);
            _output.WriteLine("[4] Instance stopped.");
        }
    }

    [Fact]
    public void StudioWindowHandleInfo_FormattedProperties_AreCorrect()
    {
        var info = new StudioWindowHandleInfo
        {
            Handle = (IntPtr)0x1234,
            Title = "TikTok LIVE Studio",
            ClassName = "Chrome_WidgetWin_1",
            ProcessId = 9999,
            Width = 1280,
            Height = 720,
            IsVisible = true,
            IsIconic = false,
            IsPrimary = true,
            WindowRole = "หน้าต่างหลัก (Main)"
        };

        Assert.Equal("0x00001234", info.HandleHex);
        Assert.Equal("1280 x 720", info.SizeDisplay);
        Assert.True(info.IsPrimary);
        Assert.True(info.IsVisible);
    }

    [Fact]
    public void WindowController_GetWindowHandlesInfoForInstance_NonExistent_ReturnsEmpty()
    {
        var winCtrl = new WindowController();
        var result = winCtrl.GetWindowHandlesInfoForInstance(null, 59999);
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void WindowController_SetSingleWindowVisibility_ZeroHandle_ReturnsFalse()
    {
        var winCtrl = new WindowController();
        bool result = winCtrl.SetSingleWindowVisibility(IntPtr.Zero, true);
        Assert.False(result);
    }
}
