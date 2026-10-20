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

    public event Action<StudioInstanceProfile>? InstanceExited;

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

            profile.IsWindowHidden = false;
            StartProcessExitWatcher(profile);
            // ติดตามและดึงหน้าต่างขึ้นมาแสดงบนหน้าจอทันทีที่ Electron เรนเดอร์เสร็จ (รองรับจังหวะที่แอปหรือ Proxy บูตช้า)
            _ = Task.Run(async () =>
            {
                logger.Info("Window", $"Waiting for TikTok Studio UI window to appear (Port: {profile.CdpPort}, Max timeout: 30s)...");
                bool realWindowShown = false;
                bool splashDetected = false;
                for (int i = 0; i < 60; i++)
                {
                    await Task.Delay(500);
                    if (profile.Status != InstanceStatus.Running) break;
                    var handles = _windowController.FindWindowHandlesForPort(profile.CdpPort, profile.ProcessId);
                    if (handles.Count > 0)
                    {
                        // ตรวจสอบว่าพบหน้าต่าง UI จริงที่มี Title ของ TikTok Studio แล้วหรือไม่ (ไม่รวม background webview)
                        var realUi = handles.FirstOrDefault(h =>
                        {
                            var sb = new System.Text.StringBuilder(256);
                            Native.Win32.GetWindowText(h, sb, 256);
                            string title = sb.ToString();
                            if (string.IsNullOrWhiteSpace(title)) return false;
                            if (title.Contains("Make Your Day", StringComparison.OrdinalIgnoreCase) ||
                                title.Contains("For You", StringComparison.OrdinalIgnoreCase) ||
                                title.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase))
                            {
                                return false;
                            }
                            return title.Contains("Studio", StringComparison.OrdinalIgnoreCase) ||
                                   title.Contains("TikTok LIVE", StringComparison.OrdinalIgnoreCase);
                        });

                        if (realUi != IntPtr.Zero)
                        {
                            bool hasRect = Native.Win32.GetWindowRect(realUi, out var rect);
                            int area = hasRect ? rect.Width * rect.Height : 0;
                            bool isMainDashboard = area >= 500000 || (rect.Width >= 900 && rect.Height >= 550);

                            if (isMainDashboard || i >= 30) // Main UI dashboard found or >= 15 seconds elapsed
                            {
                                logger.Info("Window", $"Found TikTok Studio Main UI window (HWND: {realUi}, {rect.Width}x{rect.Height}) after {(i + 1) * 0.5:F1}s for port {profile.CdpPort}. Restoring window to front.");
                                _windowController.SetWindowVisibilityForInstance(profile.ProcessId, profile.CdpPort, true);
                                realWindowShown = true;

                                // ย้ำการดึงขึ้นมาหน้าสุดเฉพาะหน้าต่างหลัก อีก 3 วินาที เพื่อป้องกันจังหวะที่ splash screen หรือ webview สลับหน้าแล้วแย่ง Focus คืน
                                for (int r = 0; r < 3; r++)
                                {
                                    await Task.Delay(1000);
                                    if (profile.Status != InstanceStatus.Running) break;
                                    _windowController.SetWindowVisibilityForInstance(profile.ProcessId, profile.CdpPort, true);
                                }
                                break;
                            }
                            else if (!splashDetected)
                            {
                                splashDetected = true;
                                logger.Info("Window", $"Detected TikTok Studio startup/splash window (HWND: {realUi}, {rect.Width}x{rect.Height}) after {(i + 1) * 0.5:F1}s. Showing splash and waiting for main dashboard UI...");
                                _windowController.SetWindowVisibilityForInstance(profile.ProcessId, profile.CdpPort, true);
                            }
                        }
                    }

                    if ((i + 1) % 10 == 0 && i < 59)
                    {
                        logger.Debug("Window", $"Still waiting for TikTok Studio UI window... ({(i + 1) * 0.5:F0}s elapsed, Port: {profile.CdpPort})");
                    }
                }

                if (!realWindowShown && profile.Status == InstanceStatus.Running)
                {
                    logger.Warn("Window", $"Window handle not found after 30 seconds for port {profile.CdpPort} (PID: {profile.ProcessId}). " +
                        $"TikTok Studio may still be loading CEF UI over a slow proxy or running in background. " +
                        $"You can click the Eye [👁️] button anytime to force show the window when ready.");
                }
            });

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

    public Task<bool> StopInstanceAsync(StudioInstanceProfile profile)
    {
        var logger = _logManager.GetLogger(profile);
        logger.Info("Orchestrator", $"Stopping instance '{profile.Name}' (Port: {profile.CdpPort}, PID: {profile.ProcessId})...");
        bool result = _launcher.KillInstance(profile);
        _windowController.ClearCacheForPort(profile.CdpPort);
        profile.Status = InstanceStatus.Stopped;
        profile.ProcessId = null;
        profile.StatusMessage = "Stopped";
        profile.IsWindowHidden = false;
        logger.Info("Orchestrator", $"Instance '{profile.Name}' stopped. (Result: {result})");
        return Task.FromResult(result);
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
                        InstanceExited?.Invoke(profile);
                    }
                    catch { }
                    break;
                }
            }
        });
    }
}
