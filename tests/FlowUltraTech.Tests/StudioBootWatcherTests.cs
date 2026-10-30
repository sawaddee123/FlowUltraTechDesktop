using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FlowUltraTech.App.ViewModels;
using FlowUltraTech.Core.Interfaces;
using FlowUltraTech.Core.Models;
using FlowUltraTech.Core.Native;
using FlowUltraTech.Core.Services;
using Xunit;

namespace FlowUltraTech.Tests;

public class StudioBootWatcherTests
{
    private const uint WS_POPUP = 0x80000000;
    private static readonly string TempProfileDir = Path.Combine(Path.GetTempPath(), "BootWatcherTest");

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(int exStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [Theory]
    [InlineData(Win32.WS_EX_TOPMOST, false, true)]  // Studio still booting: the black window if forced
    [InlineData(0, false, false)]                   // hidden by the user, or an internal Studio window
    [InlineData(Win32.WS_EX_TOPMOST, true, false)]  // already on screen
    public void IsAwaitingStudioShow_OnlyForHiddenTopmostWindow(int exStyle, bool show, bool expected)
    {
        IntPtr hWnd = CreateWindowExW(exStyle, "STATIC", "boot", WS_POPUP, -32000, -32000, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, hWnd);
        try
        {
            if (show) Win32.ShowWindow(hWnd, Win32.SW_SHOWNOACTIVATE);
            Assert.Equal(expected, WindowController.IsAwaitingStudioShow(hWnd));
        }
        finally
        {
            DestroyWindow(hWnd);
        }
    }

    [Fact]
    public void BringToFrontRockSolid_LeavesBootingWindowHidden()
    {
        IntPtr hWnd = CreateWindowExW(Win32.WS_EX_TOPMOST, "STATIC", "boot", WS_POPUP, -32000, -32000, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        try
        {
            WindowController.BringToFrontRockSolid(hWnd);
            Assert.False(Win32.IsWindowVisible(hWnd));
        }
        finally
        {
            DestroyWindow(hWnd);
        }
    }

    [Fact]
    public async Task UiShown_BringsToFrontWithoutStalling()
    {
        var windows = new FakeWindowController { UiShown = true };
        var (orchestrator, profile) = Create(windows, TimeSpan.FromSeconds(10));
        int stalls = 0;
        orchestrator.InstanceBootStalled += _ => stalls++;

        await orchestrator.StartInstanceAsync(profile, new AppSettings());
        await WaitUntilAsync(() => windows.BroughtToFront > 0);

        Assert.Equal(1, windows.BroughtToFront);
        Assert.Equal(0, stalls);
    }

    [Fact]
    public async Task UiNeverShown_ReportsStallOnce()
    {
        var windows = new FakeWindowController { UiShown = false };
        var (orchestrator, profile) = Create(windows, TimeSpan.FromSeconds(1));
        int stalls = 0;
        orchestrator.InstanceBootStalled += _ => stalls++;

        await orchestrator.StartInstanceAsync(profile, new AppSettings());
        await WaitUntilAsync(() => stalls > 0);
        await Task.Delay(700);

        Assert.Equal(1, stalls);
        Assert.Equal(0, windows.BroughtToFront);
    }

    [Fact]
    public async Task StoppedDuringBoot_DoesNotReportStall()
    {
        var windows = new FakeWindowController { UiShown = false };
        var (orchestrator, profile) = Create(windows, TimeSpan.FromSeconds(1));
        int stalls = 0;
        orchestrator.InstanceBootStalled += _ => stalls++;

        await orchestrator.StartInstanceAsync(profile, new AppSettings());
        profile.Status = InstanceStatus.Stopped;
        await Task.Delay(1500);

        Assert.Equal(0, stalls);
    }

    [Fact]
    public void SafeRenderingFlag_IsNotSavedWithTheProfile()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new StudioInstanceProfile { UseSafeRendering = true });
        Assert.DoesNotContain(nameof(StudioInstanceProfile.UseSafeRendering), json);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiveTest_StudioShowsItsOwnUi_AndShowAllRestoresOnlyWhatWasHidden(bool safeRendering)
    {
        string studioExe = TikTokStudioPathResolver.ResolveValidStudioPath(null);
        if (!File.Exists(studioExe)) return;

        var profile = new StudioInstanceProfile
        {
            Name = "BootWatcherLive",
            ProfileFolderName = "BootWatcherLive",
            CdpPort = 19289,
            UserDataDir = Path.Combine(Path.GetTempPath(), $"BootWatcherLive_{Guid.NewGuid():N}"),
            UseSafeRendering = safeRendering
        };
        var windows = new WindowController();
        var orchestrator = new InstanceOrchestrator(new ProcessLauncher(windows), windows, new CdpAutomationService());

        int CountVisible() => windows.FindWindowHandlesForPort(profile.CdpPort, profile.ProcessId).Count(Win32.IsWindowVisible);

        try
        {
            Assert.True(await orchestrator.StartInstanceAsync(profile, new AppSettings { TikTokStudioExePath = studioExe }));

            await WaitUntilAsync(() => windows.IsStudioUiShown(profile.ProcessId, profile.CdpPort), attempts: 600);
            Assert.True(windows.IsStudioUiShown(profile.ProcessId, profile.CdpPort), "Studio did not show its UI within 30s");

            // Studio runs elevated like the app does; Windows blocks hide/show from a non-elevated test run
            if (!Environment.IsPrivilegedProcess) return;

            // Studio keeps finishing its startup right after the UI appears
            await Task.Delay(2000);
            int visibleBefore = CountVisible();
            Assert.True(windows.SetAllWindowsVisibilityForInstance(profile.ProcessId, profile.CdpPort, false));
            await Task.Delay(500);
            Assert.Equal(0, CountVisible());

            Assert.True(windows.SetAllWindowsVisibilityForInstance(profile.ProcessId, profile.CdpPort, true));
            await Task.Delay(1000);
            Assert.Equal(visibleBefore, CountVisible());
        }
        finally
        {
            await orchestrator.StopInstanceAsync(profile);
        }
    }

    [Fact]
    public async Task Card_RestartsOnceWithGpuOff_ThenStopsWithError()
    {
        var orchestrator = new FakeOrchestrator();
        var profile = new StudioInstanceProfile { Name = "BootStallCard", ProfileFolderName = "BootStallCard", CdpPort = 59872, UserDataDir = TempProfileDir };
        var card = new InstanceCardViewModel(profile, new AppSettings(), orchestrator);

        orchestrator.RaiseStall(profile);
        await WaitUntilAsync(() => orchestrator.Starts == 1);
        Assert.True(profile.UseSafeRendering);
        Assert.Equal(1, orchestrator.Stops);

        orchestrator.RaiseStall(profile);
        await WaitUntilAsync(() => card.Status == InstanceStatus.Error);
        Assert.Equal(InstanceStatus.Error, card.Status);
        Assert.False(profile.UseSafeRendering);
        Assert.Equal(1, orchestrator.Starts);
        Assert.Equal(2, orchestrator.Stops);
    }

    [Theory]
    [InlineData(true, 1, 0)]   // Studio updated itself: bring the account back
    [InlineData(false, 0, 1)]  // Studio was simply closed
    public async Task ExitWatcher_TellsUpdateRelaunchFromNormalExit(bool relaunchFound, int expectedUpdated, int expectedExited)
    {
        var launcher = new FakeLauncher { RelaunchFound = relaunchFound };
        var (orchestrator, profile) = Create(new FakeWindowController { UiShown = true }, TimeSpan.FromSeconds(10), launcher);
        int updated = 0, exited = 0;
        orchestrator.InstanceUpdated += _ => updated++;
        orchestrator.InstanceExited += _ => exited++;

        await orchestrator.StartInstanceAsync(profile, new AppSettings());
        launcher.Running = false;
        await WaitUntilAsync(() => updated + exited > 0, attempts: 200);

        Assert.Equal(expectedUpdated, updated);
        Assert.Equal(expectedExited, exited);
    }

    [Fact]
    public async Task AccountsUpdatingTogether_ShareOneRelaunch()
    {
        var first = new FakeLauncher { RelaunchFound = true };
        var (orchestrator, profile) = Create(new FakeWindowController { UiShown = true }, TimeSpan.FromSeconds(10), first);
        int updated = 0;
        orchestrator.InstanceUpdated += _ => updated++;

        await orchestrator.StartInstanceAsync(profile, new AppSettings());
        var second = new StudioInstanceProfile { Name = "BootWatcherTest2", ProfileFolderName = "BootWatcherTest2", CdpPort = 59873, UserDataDir = TempProfileDir };
        await orchestrator.StartInstanceAsync(second, new AppSettings());

        first.Running = false;
        await WaitUntilAsync(() => updated > 0, attempts: 200);
        first.RelaunchFound = false; // already closed by the first watcher
        await WaitUntilAsync(() => updated > 1, attempts: 100);

        Assert.Equal(2, updated);
    }

    [Fact]
    public async Task Card_ReturnsToItsProfileAfterUpdate_WithoutRestartLoop()
    {
        var orchestrator = new FakeOrchestrator();
        var profile = new StudioInstanceProfile { Name = "UpdateCard", ProfileFolderName = "UpdateCard", CdpPort = 59874, UserDataDir = TempProfileDir };
        _ = new InstanceCardViewModel(profile, new AppSettings(), orchestrator);

        orchestrator.RaiseUpdated(profile);
        await WaitUntilAsync(() => orchestrator.Starts == 1);
        Assert.Equal(1, orchestrator.Stops);

        orchestrator.RaiseUpdated(profile);
        await Task.Delay(300);
        Assert.Equal(1, orchestrator.Starts);
    }

    [Fact]
    public void ResolveValidStudioPath_MovesPinnedPathToCurrentVersion()
    {
        string root = Path.Combine(Path.GetTempPath(), $"BootWatcherResolver_{Guid.NewGuid():N}");
        string oldExe = Path.Combine(root, "1.36.6", "TikTok LIVE Studio.exe");
        string newExe = Path.Combine(root, "1.38.0", "TikTok LIVE Studio.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(oldExe)!);
        Directory.CreateDirectory(Path.GetDirectoryName(newExe)!);
        File.WriteAllText(oldExe, string.Empty);
        File.WriteAllText(newExe, string.Empty);
        try
        {
            Assert.Equal(oldExe, TikTokStudioPathResolver.ResolveValidStudioPath(oldExe));

            File.WriteAllText(Path.Combine(root, "launcher_config.json"), "{\"cur_path\":\"1.38.0\"}");
            Assert.Equal(newExe, TikTokStudioPathResolver.ResolveValidStudioPath(oldExe));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LiveTest_UpdateRelaunch_ClosesStrayStudioAndReportsUpdate()
    {
        string studioExe = TikTokStudioPathResolver.ResolveValidStudioPath(null);
        // Studio runs elevated like the app does; a non-elevated run can neither read its command line nor close it
        if (!File.Exists(studioExe) || !Environment.IsPrivilegedProcess) return;

        string installDir = Path.GetDirectoryName(Path.GetDirectoryName(studioExe))!;
        var windows = new WindowController();
        var launcher = new ProcessLauncher(windows);
        var orchestrator = new InstanceOrchestrator(launcher, windows, new CdpAutomationService());
        var profile = new StudioInstanceProfile
        {
            Name = "UpdateRelaunchLive",
            ProfileFolderName = "UpdateRelaunchLive",
            CdpPort = 19290,
            UserDataDir = Path.Combine(Path.GetTempPath(), $"BootWatcherLive_{Guid.NewGuid():N}")
        };
        int updated = 0;
        orchestrator.InstanceUpdated += _ => updated++;

        try
        {
            Assert.True(await orchestrator.StartInstanceAsync(profile, new AppSettings { TikTokStudioExePath = studioExe }));
            await WaitUntilAsync(() => windows.IsStudioUiShown(profile.ProcessId, profile.CdpPort), attempts: 600);

            // What Studio's forced update does: relaunch through the Launcher, then exit
            Process.Start(Path.Combine(installDir, "TikTok LIVE Studio Launcher.exe"), "--update_relaunch");
            ProcessLauncher.ForceKillProcessTree(profile.ProcessId!.Value);

            await WaitUntilAsync(() => updated > 0, attempts: 400);
            Assert.Equal(1, updated);
            await Task.Delay(2000);
            Assert.Empty(Process.GetProcessesByName("TikTok LIVE Studio"));
        }
        finally
        {
            await orchestrator.StopInstanceAsync(profile);
        }
    }

    [Fact]
    public async Task UntrackedStudio_ShowsAsRunning_ThenStoppedWhenItCloses()
    {
        var launcher = new FakeLauncher { UntrackedPid = 4242 };
        var (orchestrator, profile) = Create(new FakeWindowController(), TimeSpan.FromSeconds(10), launcher);
        int exited = 0;
        orchestrator.InstanceExited += _ => exited++;

        Assert.True(orchestrator.TryAdoptRunningInstance(profile));
        Assert.Equal(InstanceStatus.Running, profile.Status);
        Assert.Equal(4242, profile.ProcessId);

        launcher.Running = false;
        await WaitUntilAsync(() => exited > 0, attempts: 200);
        Assert.Equal(InstanceStatus.Stopped, profile.Status);
    }

    [Theory]
    [InlineData(InstanceStatus.Running)]   // already tracked
    [InlineData(InstanceStatus.Starting)]  // our own launch just opened the port
    public void TrackedStudio_IsNotAdoptedAgain(InstanceStatus status)
    {
        var (orchestrator, profile) = Create(new FakeWindowController(), TimeSpan.FromSeconds(10), new FakeLauncher { UntrackedPid = 4242 });
        profile.Status = status;

        Assert.False(orchestrator.TryAdoptRunningInstance(profile));
    }

    [Fact]
    public async Task Card_FollowsUntrackedStudio()
    {
        var orchestrator = new FakeOrchestrator();
        var profile = new StudioInstanceProfile { Name = "AdoptCard", ProfileFolderName = "AdoptCard", CdpPort = 59875, UserDataDir = TempProfileDir };
        var card = new InstanceCardViewModel(profile, new AppSettings(), orchestrator);

        await card.AdoptUntrackedStudioAsync();
        Assert.Equal(InstanceStatus.Stopped, card.Status);

        orchestrator.UntrackedStudio = true;
        await card.AdoptUntrackedStudioAsync();
        Assert.Equal(InstanceStatus.Running, card.Status);
    }

    [Fact]
    public async Task LiveTest_LoginCallbackStudio_IsAdoptedOnlyByItsOwnProfile()
    {
        string studioExe = TikTokStudioPathResolver.ResolveValidStudioPath(null);
        // Studio runs elevated like the app does; only an elevated run can close it again
        if (!File.Exists(studioExe) || !Environment.IsPrivilegedProcess) return;

        var windows = new WindowController();
        var launcher = new ProcessLauncher(windows);
        var orchestrator = new InstanceOrchestrator(launcher, windows, new CdpAutomationService());
        var owner = new StudioInstanceProfile
        {
            Name = "AdoptLive",
            ProfileFolderName = "AdoptLive",
            CdpPort = 19291,
            UserDataDir = Path.Combine(Path.GetTempPath(), $"BootWatcherLive_{Guid.NewGuid():N}")
        };
        var otherAccount = new StudioInstanceProfile
        {
            Name = "AdoptOther",
            ProfileFolderName = "AdoptOther",
            CdpPort = owner.CdpPort,
            UserDataDir = Path.Combine(Path.GetTempPath(), $"BootWatcherLive_{Guid.NewGuid():N}")
        };

        try
        {
            // What the live-studio-app:// handler does when no Studio is running on the profile
            Assert.True(ProcessLauncher.ForwardLoginCallback(owner, "live-studio-app://login/prepare", new AppSettings { TikTokStudioExePath = studioExe }));
            await WaitUntilAsync(() => launcher.FindUntrackedStudio(owner).HasValue, attempts: 600);

            Assert.False(orchestrator.TryAdoptRunningInstance(otherAccount));
            Assert.True(orchestrator.TryAdoptRunningInstance(owner));
            Assert.Equal(InstanceStatus.Running, owner.Status);
        }
        finally
        {
            await orchestrator.StopInstanceAsync(owner);
        }
    }

    private static (InstanceOrchestrator, StudioInstanceProfile) Create(IWindowController windows, TimeSpan bootTimeout, IProcessLauncher? launcher = null)
    {
        var orchestrator = new InstanceOrchestrator(launcher ?? new FakeLauncher(), windows, new CdpAutomationService())
        {
            StudioBootTimeout = bootTimeout
        };
        return (orchestrator, new StudioInstanceProfile { Name = "BootWatcherTest", ProfileFolderName = "BootWatcherTest", CdpPort = 59871, UserDataDir = TempProfileDir });
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int attempts = 100)
    {
        for (int i = 0; i < attempts && !condition(); i++)
        {
            await Task.Delay(50);
        }
    }

    private sealed class FakeOrchestrator : IInstanceOrchestrator
    {
        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public bool UntrackedStudio { get; set; }
        public event Action<StudioInstanceProfile>? InstanceExited { add { } remove { } }
        public event Action<StudioInstanceProfile>? InstanceBootStalled;
        public event Action<StudioInstanceProfile>? InstanceUpdated;

        public void RaiseStall(StudioInstanceProfile profile) => InstanceBootStalled?.Invoke(profile);
        public void RaiseUpdated(StudioInstanceProfile profile) => InstanceUpdated?.Invoke(profile);

        public Task<bool> StartInstanceAsync(StudioInstanceProfile profile, AppSettings settings)
        {
            Starts++;
            return Task.FromResult(false);
        }

        public Task<bool> StopInstanceAsync(StudioInstanceProfile profile)
        {
            Stops++;
            return Task.FromResult(true);
        }

        public Task<bool> StartUltraLightLoginAsync(StudioInstanceProfile profile, AppSettings settings) => Task.FromResult(false);
        public bool TryAdoptRunningInstance(StudioInstanceProfile profile)
        {
            if (!UntrackedStudio) return false;
            profile.Status = InstanceStatus.Running;
            return true;
        }

        public bool ToggleWindowVisibility(StudioInstanceProfile profile) => false;
        public bool ShowWindow(StudioInstanceProfile profile) => false;
        public bool HideWindow(StudioInstanceProfile profile) => false;
        public Task<bool> SendChatAsync(StudioInstanceProfile profile, string message) => Task.FromResult(false);
        public void RefreshStatus(StudioInstanceProfile profile) { }
        public void StopAllInstances() { }
    }

    private sealed class FakeLauncher : IProcessLauncher
    {
        public bool Running { get; set; } = true;
        public bool RelaunchFound { get; set; }
        public int? UntrackedPid { get; set; }

        public Task<Process?> LaunchInstanceAsync(StudioInstanceProfile instance, AppSettings settings)
        {
            instance.ProcessId = 4242;
            return Task.FromResult<Process?>(null);
        }

        public Task<Process?> LaunchUltraLightBackgroundAsync(StudioInstanceProfile instance, AppSettings settings) => LaunchInstanceAsync(instance, settings);
        public bool KillInstance(StudioInstanceProfile instance) => true;
        public bool IsInstanceRunning(StudioInstanceProfile instance) => Running;
        public int? FindProcessId(StudioInstanceProfile instance) => instance.ProcessId;
        public bool CloseUpdateRelaunch() => RelaunchFound;
        public int? FindUntrackedStudio(StudioInstanceProfile instance) => UntrackedPid;
    }

    private sealed class FakeWindowController : IWindowController
    {
        public bool UiShown { get; set; }
        public int BroughtToFront { get; private set; }

        public bool IsStudioUiShown(int? processId, int cdpPort) => UiShown;
        public void BringToFront(int processId) => BroughtToFront++;

        public List<IntPtr> FindWindowHandlesForProcess(int processId) => new();
        public List<IntPtr> FindWindowHandlesForPort(int cdpPort, int? processId = null) => new();
        public List<StudioWindowHandleInfo> GetWindowHandlesInfoForInstance(int? processId, int cdpPort) => new();
        public bool SetSingleWindowVisibility(IntPtr hWnd, bool visible) => true;
        public bool SetAllWindowsVisibilityForInstance(int? processId, int cdpPort, bool visible) => true;
        public bool SetWindowVisibility(int processId, bool visible) => true;
        public bool SetWindowVisibilityForInstance(int? processId, int cdpPort, bool visible) => true;
        public bool IsAnyWindowVisible(int processId) => false;
        public bool IsAnyWindowVisibleForInstance(int? processId, int cdpPort) => false;
        public void ClearCacheForPort(int cdpPort) { }
    }
}
