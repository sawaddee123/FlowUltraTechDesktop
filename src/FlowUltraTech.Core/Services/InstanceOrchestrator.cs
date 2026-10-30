using System;
using System.Diagnostics;
using System.Threading.Tasks;
using FlowUltraTech.Core.Interfaces;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Services;

public class InstanceOrchestrator : IInstanceOrchestrator
{
    private readonly IProcessLauncher _launcher;
    private readonly IWindowController _windowController;
    private readonly ICdpAutomationService _cdpService;
    private readonly IInstanceLogManager _logManager;
    private DateTime _lastUpdateRelaunchAt;

    public event Action<StudioInstanceProfile>? InstanceExited;
    public event Action<StudioInstanceProfile>? InstanceBootStalled;
    public event Action<StudioInstanceProfile>? InstanceUpdated;

    /// <summary>How long Studio gets to show its UI before the launch counts as stalled.</summary>
    public TimeSpan StudioBootTimeout { get; set; } = TimeSpan.FromSeconds(90);

    public InstanceOrchestrator(
        IProcessLauncher launcher,
        IWindowController windowController,
        ICdpAutomationService cdpService,
        IInstanceLogManager? logManager = null)
    {
        _launcher = launcher;
        _windowController = windowController;
        _cdpService = cdpService;
        _logManager = logManager ?? InstanceLogManager.Shared;
    }

    public void StopAllInstances()
    {
        ProcessLauncher.KillAllStudioProcesses();
    }

    public async Task<bool> StartInstanceAsync(StudioInstanceProfile profile, AppSettings settings)
    {
        var logger = _logManager.GetLogger(profile);
        logger.Info("Orchestrator", $"Starting instance '{profile.Name}' (Port: {profile.CdpPort}, Mode: {profile.LaunchMode})...");

        try
        {
            profile.Status = InstanceStatus.Starting;
            profile.StatusMessage = "Starting instance...";

            // Auto-bind Windows protocol live-studio-app:// to this profile so browser login callbacks route directly here!
            ProcessLauncher.BindProtocolToProfile(profile, settings);

            // Off the UI thread: the launcher does blocking port/window/kill checks before its first await.
            var proc = await Task.Run(() => _launcher.LaunchInstanceAsync(profile, settings));
            if (profile.Status == InstanceStatus.Stopped)
            {
                logger.Warn("Orchestrator", $"Instance '{profile.Name}' was stopped during launch. Keeping it stopped.");
                return false;
            }
            if (proc == null && !profile.ProcessId.HasValue)
            {
                profile.Status = InstanceStatus.Error;
                profile.StatusMessage = "Failed to launch process.";
                logger.Error("Orchestrator", $"Failed to launch instance: {profile.StatusMessage}");
                return false;
            }

            profile.Status = InstanceStatus.Running;
            profile.StatusMessage = $"Running (Port {profile.CdpPort})";
            logger.Info("Orchestrator", $"Instance '{profile.Name}' is running on port {profile.CdpPort} (PID: {profile.ProcessId})");

            profile.IsWindowHidden = false;
            StartProcessExitWatcher(profile);
            _ = Task.Run(() => WatchStudioBootAsync(profile));

            return true;
        }
        catch (Exception ex)
        {
            profile.Status = InstanceStatus.Error;
            profile.StatusMessage = $"Error: {ex.Message}";
            logger.Error("Orchestrator", $"Exception starting instance: {ex.Message}", null, ex);
            return false;
        }
    }

    public async Task<bool> StartUltraLightLoginAsync(StudioInstanceProfile profile, AppSettings settings)
    {
        var logger = _logManager.GetLogger(profile);
        logger.Info("Orchestrator", $"Starting Ultra-Light Login mode for '{profile.Name}' (Port: {profile.CdpPort})...");

        try
        {
            profile.Status = InstanceStatus.Starting;
            profile.StatusMessage = "กำลังเริ่มโหมด Ultra-Light เพื่อรอรับล็อกอิน...";

            // ผูก Protocol live-studio-app:// เข้ากับโปรไฟล์นี้ เพื่อให้บราวเซอร์ส่งกลับมายังโปรไฟล์นี้โดยตรง
            ProcessLauncher.BindProtocolToProfile(profile, settings);

            var proc = await Task.Run(() => _launcher.LaunchUltraLightBackgroundAsync(profile, settings));
            if (profile.Status == InstanceStatus.Stopped)
            {
                logger.Warn("Orchestrator", "Ultra-Light launch was stopped during launch. Keeping it stopped.");
                return false;
            }
            if (proc == null && !profile.ProcessId.HasValue)
            {
                profile.Status = InstanceStatus.Error;
                profile.StatusMessage = "ไม่สามารถเปิดโหมด Ultra-Light ได้";
                logger.Error("Orchestrator", "Failed to launch in Ultra-Light mode.");
                return false;
            }

            profile.Status = InstanceStatus.Running;
            profile.IsWindowHidden = true;
            profile.StatusMessage = "โหมด Ultra-Light ทำงาน (ซ่อนหน้าต่าง) พร้อมรับการยืนยันตัวตน";
            logger.Info("Orchestrator", "Ultra-Light mode is active (0% GPU, hidden window).");
            StartProcessExitWatcher(profile);
            return true;
        }
        catch (Exception ex)
        {
            profile.Status = InstanceStatus.Error;
            profile.StatusMessage = $"Error: {ex.Message}";
            logger.Error("Orchestrator", $"Exception in Ultra-Light mode: {ex.Message}", null, ex);
            return false;
        }
    }

    public async Task<bool> StopInstanceAsync(StudioInstanceProfile profile)
    {
        var logger = _logManager.GetLogger(profile);
        logger.Info("Orchestrator", $"Stopping instance '{profile.Name}' (Port: {profile.CdpPort}, PID: {profile.ProcessId})...");
        // Off the UI thread: killing waits up to several seconds per process.
        bool result = await Task.Run(() => _launcher.KillInstance(profile));
        _windowController.ClearCacheForPort(profile.CdpPort);
        profile.Status = InstanceStatus.Stopped;
        profile.ProcessId = null;
        profile.StatusMessage = "Stopped";
        profile.IsWindowHidden = false;
        logger.Info("Orchestrator", $"Instance '{profile.Name}' stopped. (Result: {result})");
        return result;
    }

    public bool ToggleWindowVisibility(StudioInstanceProfile profile)
    {
        var logger = _logManager.GetLogger(profile);
        bool targetVisible = profile.IsWindowHidden; // if currently hidden, make visible
        logger.Info("Window", $"User toggle window visibility for '{profile.Name}' -> TargetVisible={targetVisible} (Port: {profile.CdpPort}, PID: {profile.ProcessId})");
        bool success = _windowController.SetWindowVisibilityForInstance(profile.ProcessId, profile.CdpPort, targetVisible);
        if (targetVisible && profile.ProcessId.HasValue)
        {
            _windowController.BringToFront(profile.ProcessId.Value);
        }
        if (success || !targetVisible)
        {
            profile.IsWindowHidden = !targetVisible;
        }
        logger.Info("Window", $"Toggle window visibility result for '{profile.Name}': IsWindowHidden={profile.IsWindowHidden} (Success: {success})");
        return success;
    }

    public bool ShowWindow(StudioInstanceProfile profile)
    {
        var logger = _logManager.GetLogger(profile);
        logger.Info("Window", $"ShowWindow requested for '{profile.Name}' (Port: {profile.CdpPort}, PID: {profile.ProcessId})...");
        bool success = _windowController.SetWindowVisibilityForInstance(profile.ProcessId, profile.CdpPort, true);
        if (profile.ProcessId.HasValue)
        {
            _windowController.BringToFront(profile.ProcessId.Value);
        }
        if (success)
        {
            profile.IsWindowHidden = false;
            logger.Info("Window", $"ShowWindow for '{profile.Name}': Success. Window brought to front.");
        }
        else
        {
            logger.Warn("Window", $"ShowWindow for '{profile.Name}': No top-level HWND found yet (Port: {profile.CdpPort}, PID: {profile.ProcessId}). Studio may still be loading or minimized.");
        }
        return success;
    }

    public bool HideWindow(StudioInstanceProfile profile)
    {
        var logger = _logManager.GetLogger(profile);
        logger.Info("Window", $"HideWindow requested for '{profile.Name}' (Port: {profile.CdpPort}, PID: {profile.ProcessId})...");
        bool success = _windowController.SetWindowVisibilityForInstance(profile.ProcessId, profile.CdpPort, false);
        if (success) profile.IsWindowHidden = true;
        logger.Info("Window", $"HideWindow for '{profile.Name}': Result={success}. IsWindowHidden={profile.IsWindowHidden}.");
        return success;
    }

    public async Task<bool> SendChatAsync(StudioInstanceProfile profile, string message)
    {
        var logger = _logManager.GetLogger(profile);
        if (profile.Status != InstanceStatus.Running)
        {
            logger.Warn("Chat", $"Cannot send chat to '{profile.Name}': Instance is not running.");
            return false;
        }

        logger.Info("Chat", $"Sending live chat: \"{message}\" (Port: {profile.CdpPort})");
        bool ok = await _cdpService.SendChatMessageAsync(profile.CdpPort, message);
        logger.Info("Chat", $"Send live chat result: {(ok ? "Success" : "Failed")}");
        return ok;
    }

    public void RefreshStatus(StudioInstanceProfile profile)
    {
        bool isRunning = _launcher.IsInstanceRunning(profile);
        if (isRunning)
        {
            profile.Status = InstanceStatus.Running;
            bool isVisible = _windowController.IsAnyWindowVisibleForInstance(profile.ProcessId, profile.CdpPort);
            profile.IsWindowHidden = !isVisible;
        }
        else
        {
            profile.Status = InstanceStatus.Stopped;
            profile.ProcessId = null;
            profile.IsWindowHidden = false;
        }
    }

    // Studio shows its own window once the UI is ready, so only wait for that: forcing it earlier gave the black window.
    private async Task WatchStudioBootAsync(StudioInstanceProfile profile)
    {
        var logger = _logManager.GetLogger(profile);
        var launchedAt = profile.StartedAt;
        var elapsed = Stopwatch.StartNew();
        logger.Info("Window", $"Waiting for TikTok Studio to show its UI (Port: {profile.CdpPort}, Max timeout: {StudioBootTimeout.TotalSeconds:F0}s)...");

        while (elapsed.Elapsed < StudioBootTimeout)
        {
            await Task.Delay(500);
            if (profile.Status != InstanceStatus.Running || profile.StartedAt != launchedAt) return;

            if (_windowController.IsStudioUiShown(profile.ProcessId, profile.CdpPort))
            {
                logger.Info("Window", $"TikTok Studio UI is up after {elapsed.Elapsed.TotalSeconds:F1}s (Port: {profile.CdpPort}). Bringing it to front.");
                if (profile.ProcessId.HasValue)
                {
                    _windowController.BringToFront(profile.ProcessId.Value);
                }
                return;
            }
        }

        logger.Warn("Window", $"TikTok Studio did not show its UI within {StudioBootTimeout.TotalSeconds:F0}s (Port: {profile.CdpPort}, PID: {profile.ProcessId}, SafeRendering: {profile.UseSafeRendering}).");
        InstanceBootStalled?.Invoke(profile);
    }

    private bool IsUpdateRelaunch()
    {
        if (_launcher.CloseUpdateRelaunch())
        {
            _lastUpdateRelaunchAt = DateTime.Now;
            return true;
        }
        // Accounts updating together share one relaunch, and another account's watcher may have closed it already
        return DateTime.Now - _lastUpdateRelaunchAt < TimeSpan.FromSeconds(30);
    }

    private void StartProcessExitWatcher(StudioInstanceProfile profile)
    {
        int targetPort = profile.CdpPort;
        _ = Task.Run(async () =>
        {
            await Task.Delay(2500);

            while (profile.Status == InstanceStatus.Running && profile.CdpPort == targetPort)
            {
                await Task.Delay(1500);

                if (profile.Status != InstanceStatus.Running || profile.CdpPort != targetPort) break;

                bool isRunning = _launcher.IsInstanceRunning(profile);
                if (!isRunning)
                {
                    var logger = _logManager.GetLogger(profile);
                    logger.Info("Orchestrator", $"Detected process exit for instance '{profile.Name}' (Port: {profile.CdpPort}). Updating status to Stopped.");
                    profile.Status = InstanceStatus.Stopped;
                    profile.ProcessId = null;
                    profile.StatusMessage = "Stopped (โปรแกรมถูกปิด)";
                    profile.IsWindowHidden = false;
                    _windowController.ClearCacheForPort(profile.CdpPort);

                    try
                    {
                        if (IsUpdateRelaunch())
                        {
                            logger.Info("Orchestrator", $"TikTok Studio updated itself and relaunched outside '{profile.Name}'. Closed the stray relaunch.");
                            InstanceUpdated?.Invoke(profile);
                        }
                        else
                        {
                            InstanceExited?.Invoke(profile);
                        }
                    }
                    catch { }
                    break;
                }
            }
        });
    }
}
