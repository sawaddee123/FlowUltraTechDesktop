using System;
using System.Diagnostics;
using System.Threading.Tasks;
using TiktokStudioSandbox.Core.Interfaces;
using TiktokStudioSandbox.Core.Models;

namespace TiktokStudioSandbox.Core.Services;

public class InstanceOrchestrator : IInstanceOrchestrator
{
    private readonly IProcessLauncher _launcher;
    private readonly IWindowController _windowController;
    private readonly ICdpAutomationService _cdpService;
    private readonly IInstanceLogManager _logManager;

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

            var proc = await _launcher.LaunchInstanceAsync(profile, settings);
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

            if (!settings.AutoHideOnLaunch)
            {
                profile.IsWindowHidden = false;
                // ติดตามและดึงหน้าต่างขึ้นมาแสดงบนหน้าจอทันทีที่ Electron เรนเดอร์เสร็จ (รองรับจังหวะที่แอปบูตช้า)
                _ = Task.Run(async () =>
                {
                    for (int i = 0; i < 16; i++)
                    {
                        await Task.Delay(500);
                        if (profile.Status != InstanceStatus.Running) break;
                        var handles = _windowController.FindWindowHandlesForPort(profile.CdpPort, profile.ProcessId);
                        if (handles.Count > 0)
                        {
                            logger.Info("Window", $"Found {handles.Count} window handle(s) for port {profile.CdpPort}. Showing window.");
                            _windowController.SetWindowVisibilityForInstance(profile.ProcessId, profile.CdpPort, true);
                            break;
                        }
                    }
                });
            }
            else if (settings.AutoHideOnLaunch && profile.ProcessId.HasValue)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(4000);
                    if (profile.Status == InstanceStatus.Running && profile.ProcessId.HasValue)
                    {
                        logger.Info("Window", $"AutoHideOnLaunch active: Hiding window for PID {profile.ProcessId.Value}");
                        _windowController.SetWindowVisibility(profile.ProcessId.Value, false);
                        profile.IsWindowHidden = true;
                    }
                });
            }

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

            var proc = await _launcher.LaunchUltraLightBackgroundAsync(profile, settings);
            if (proc == null && !profile.ProcessId.HasValue)
            {
                profile.Status = InstanceStatus.Error;
                profile.StatusMessage = "ไม่สามารถเปิดโหมด Ultra-Light ได้";
                logger.Error("Orchestrator", "Failed to launch in Ultra-Light mode.");
                return false;
            }

            profile.Status = InstanceStatus.Running;
            profile.IsWindowHidden = true;
            profile.StatusMessage = "⚡ Ultra-Light Active (ซ่อนหน้าต่าง 0% GPU) พร้อมรับการยืนยันตัวตนจากบราวเซอร์";
            logger.Info("Orchestrator", "Ultra-Light mode is active (0% GPU, hidden window).");
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

    public Task<bool> StopInstanceAsync(StudioInstanceProfile profile)
    {
        var logger = _logManager.GetLogger(profile);
        logger.Info("Orchestrator", $"Stopping instance '{profile.Name}' (Port: {profile.CdpPort}, PID: {profile.ProcessId})...");
        bool result = _launcher.KillInstance(profile);
        profile.Status = InstanceStatus.Stopped;
        profile.StatusMessage = "Stopped";
        profile.IsWindowHidden = false;
        logger.Info("Orchestrator", $"Instance '{profile.Name}' stopped. (Result: {result})");
        return Task.FromResult(result);
    }

    public bool ToggleWindowVisibility(StudioInstanceProfile profile)
    {
        var logger = _logManager.GetLogger(profile);
        bool targetVisible = profile.IsWindowHidden; // if currently hidden, make visible
        bool success = _windowController.SetWindowVisibilityForInstance(profile.ProcessId, profile.CdpPort, targetVisible);
        if (success || !targetVisible)
        {
            profile.IsWindowHidden = !targetVisible;
        }
        logger.Info("Window", $"Toggle window visibility for '{profile.Name}': Visible={!profile.IsWindowHidden} (Success: {success})");
        return success;
    }

    public bool ShowWindow(StudioInstanceProfile profile)
    {
        var logger = _logManager.GetLogger(profile);
        bool success = _windowController.SetWindowVisibilityForInstance(profile.ProcessId, profile.CdpPort, true);
        if (profile.ProcessId.HasValue)
        {
            _windowController.BringToFront(profile.ProcessId.Value);
        }
        if (success)
        {
            profile.IsWindowHidden = false;
        }
        logger.Info("Window", $"Show window for '{profile.Name}' (Success: {success})");
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
}
