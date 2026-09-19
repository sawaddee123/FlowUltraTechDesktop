using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using TiktokStudioSandbox.Core.Interfaces;
using TiktokStudioSandbox.Core.Models;

namespace TiktokStudioSandbox.Core.Services;

public class ProcessLauncher : IProcessLauncher
{
    private readonly IWindowController _windowController;
    private readonly IInstanceLogManager _logManager;

    public ProcessLauncher(IWindowController? windowController = null, IInstanceLogManager? logManager = null)
    {
        _windowController = windowController ?? new WindowController();
        _logManager = logManager ?? InstanceLogManager.Shared;
    }

    public Task<Process?> LaunchInstanceAsync(StudioInstanceProfile instance, AppSettings settings)
    {
        return LaunchInternalAsync(instance, settings, ultraLight: false);
    }

    public Task<Process?> LaunchUltraLightBackgroundAsync(StudioInstanceProfile instance, AppSettings settings)
    {
        return LaunchInternalAsync(instance, settings, ultraLight: true);
    }

    private async Task<Process?> LaunchInternalAsync(StudioInstanceProfile instance, AppSettings settings, bool ultraLight)
    {
        var logger = _logManager.GetLogger(instance);
        logger.Info("Process", $"Request to launch TikTok LIVE Studio (Port: {instance.CdpPort}, Mode: {instance.LaunchMode}, UltraLight: {ultraLight})");

        if (IsInstanceRunning(instance))
        {
            int? existingPid = instance.ProcessId ?? GetPidListeningOnPort(instance.CdpPort);
            if (existingPid.HasValue)
            {
                // ตรวจสอบว่าโปรเซสเดิมมีหน้าต่าง UI จริงหรือไม่
                var handles = _windowController.FindWindowHandlesForPort(instance.CdpPort, existingPid.Value);
                if (handles.Count > 0)
                {
                    logger.Info("Process", $"Instance already running with active UI (PID: {existingPid.Value}, HWNDs: {handles.Count}). Bringing to front.");
                    // โปรเซสเดิมยังมี UI แข็งแรง ให้ดึงขึ้นมาบนหน้าจอทันที
                    if (!ultraLight)
                    {
                        _windowController.SetWindowVisibilityForInstance(existingPid.Value, instance.CdpPort, true);
                    }
                    instance.Status = InstanceStatus.Running;
                    instance.ProcessId = existingPid.Value;
                    instance.IsWindowHidden = ultraLight;
                    return Process.GetProcessById(existingPid.Value);
                }
                else
                {
                    logger.Warn("Process", $"Detected zombie process (PID: {existingPid.Value}) on port {instance.CdpPort} without active UI. Terminating it...");
                    // เป็น Zombie Process (โปรเซสผีค้างใน Task Manager แต่ไม่มี UI) ให้ปิดทิ้งแล้วเริ่มใหม่
                    KillInstance(instance);
                    await Task.Delay(600);
                }
            }
        }

        string studioExe = TikTokStudioPathResolver.ResolveValidStudioPath(settings.TikTokStudioExePath);
        if (!File.Exists(studioExe))
        {
            logger.Error("Process", $"TikTok LIVE Studio executable not found at: {studioExe}");
            throw new FileNotFoundException($"TikTok LIVE Studio executable not found at: {studioExe}");
        }
        logger.Debug("Process", $"Resolved studio executable: {studioExe}");

        // ลบ Lockfile ของ Chromium ที่อาจตกค้างจากการ Force Kill รอบก่อน
        string profileDir = ProfileManagerService.ResolveProfileDirectoryStatic(instance);

        if (Directory.Exists(profileDir))
        {
            try
            {
                string lockFile = Path.Combine(profileDir, "SingletonLock");
                if (File.Exists(lockFile)) File.Delete(lockFile);
                string socketFile = Path.Combine(profileDir, "SingletonSocket");
                if (File.Exists(socketFile)) File.Delete(socketFile);
                string cookieFile = Path.Combine(profileDir, "SingletonCookie");
                if (File.Exists(cookieFile)) File.Delete(cookieFile);
                logger.Debug("Process", "Cleaned up legacy Chromium singleton lock/socket files");
            }
            catch { }
        }

        string baseFlags = ultraLight
            ? "--disable-gpu --disable-gpu-compositing --disable-software-rasterizer --disable-d3d11 --disable-accelerated-2d-canvas --disable-gpu-rasterization --no-sandbox"
            : settings.DefaultChromiumFlags;

        string chromiumArgs = $"--remote-debugging-port={instance.CdpPort} {baseFlags} {instance.CustomChromiumFlags}".Trim();

        ProcessStartInfo startInfo;

        if (instance.LaunchMode == InstanceLaunchMode.DefaultProfile)
        {
            // Main System / Default Profile Mode (no --user-data-dir flag)
            startInfo = new ProcessStartInfo
            {
                FileName = studioExe,
                Arguments = chromiumArgs,
                UseShellExecute = true,
                WindowStyle = ultraLight ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
                WorkingDirectory = Path.GetDirectoryName(studioExe) ?? string.Empty
            };
        }
        else
        {
            // Direct Profile Mode (isolated profile folder)
            Directory.CreateDirectory(profileDir);

            string launchExe = studioExe;
            try
            {
                string folderTag = !string.IsNullOrWhiteSpace(instance.ProfileFolderName) ? instance.ProfileFolderName : instance.Name;
                string studioDir = Path.GetDirectoryName(studioExe) ?? string.Empty;
                string isolatedExe = Path.Combine(studioDir, $"TikTok LIVE Studio_{folderTag}.exe");
                if (!File.Exists(isolatedExe))
                {
                    try
                    {
                        File.CreateSymbolicLink(isolatedExe, studioExe);
                    }
                    catch
                    {
                        // Fallback safely to studioExe if symlink creation is not permitted
                    }
                }
                if (File.Exists(isolatedExe))
                {
                    launchExe = isolatedExe;
                }
            }
            catch { }

            string fullArgs = $"--user-data-dir=\"{profileDir}\" {chromiumArgs}";
            startInfo = new ProcessStartInfo
            {
                FileName = launchExe,
                Arguments = fullArgs,
                UseShellExecute = true,
                WindowStyle = ultraLight ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
                WorkingDirectory = Path.GetDirectoryName(studioExe) ?? string.Empty
            };
        }

        logger.Debug("Process", $"Spawning process with arguments: {startInfo.Arguments}");
        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (System.ComponentModel.Win32Exception wEx) when (wEx.NativeErrorCode == 740)
        {
            logger.Warn("Process", "UAC elevation required. Retrying with 'runas' verb...");
            startInfo.UseShellExecute = true;
            startInfo.Verb = "runas";
            process = Process.Start(startInfo);
        }

        if (process != null)
        {
            logger.Info("Process", $"Process spawned successfully. Initial PID: {process.Id}");
            instance.Status = InstanceStatus.Starting;
            instance.StartedAt = DateTime.Now;
            instance.IsWindowHidden = ultraLight;

            int? detectedPortPid = null;
            // Wait and poll for CDP port to open
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(500);
                int? portPid = GetPidListeningOnPort(instance.CdpPort);
                if (portPid.HasValue)
                {
                    detectedPortPid = portPid.Value;
                    logger.Info("Process", $"CDP port {instance.CdpPort} is responding! Listening PID: {detectedPortPid.Value}");
                    break;
                }
            }

            // Prefer root process from Process.Start if it's alive, otherwise find root via ProcessTreeHelper
            if (!process.HasExited)
            {
                instance.ProcessId = process.Id;
            }
            else if (detectedPortPid.HasValue)
            {
                var (rootPid, _) = ProcessTreeHelper.GetProcessTree(detectedPortPid.Value);
                instance.ProcessId = rootPid > 0 ? rootPid : detectedPortPid.Value;
            }

            if (ultraLight && instance.ProcessId.HasValue)
            {
                // Ensure all descendant windows are hidden
                try
                {
                    var (_, allPids) = ProcessTreeHelper.GetProcessTree(instance.ProcessId.Value);
                    foreach (var pid in allPids)
                    {
                        var hwnds = Native.Win32.FindWindowsForProcess(pid);
                        foreach (var hwnd in hwnds)
                        {
                            Native.Win32.ShowWindow(hwnd, Native.Win32.SW_HIDE);
                        }
                    }
                }
                catch { }
            }

            instance.Status = InstanceStatus.Running;
            instance.StatusMessage = ultraLight 
                ? $"Ultra-Light (Port {instance.CdpPort}, Hidden)" 
                : $"Running on port {instance.CdpPort}";
            logger.Info("Process", $"Instance '{instance.Name}' is now RUNNING. (PID: {instance.ProcessId}, Port: {instance.CdpPort})");
            return process;
        }

        instance.Status = InstanceStatus.Error;
        instance.StatusMessage = "Failed to launch process";
        logger.Error("Process", "Process.Start returned null. Failed to launch process.");
        return null;
    }

    public bool KillInstance(StudioInstanceProfile instance)
    {
        var logger = _logManager.GetLogger(instance);
        logger.Info("Process", $"Terminating instance '{instance.Name}' (Port: {instance.CdpPort}, PID: {instance.ProcessId})...");
        bool killed = false;
        var pidsToKill = new HashSet<int>();
        var rootPids = new HashSet<int>();

        // 1. Find process listening on this instance's CDP port and resolve its whole tree
        int? listeningPid = GetPidListeningOnPort(instance.CdpPort);
        if (listeningPid.HasValue && listeningPid.Value != Environment.ProcessId)
        {
            var (root, all) = ProcessTreeHelper.GetProcessTree(listeningPid.Value);
            if (root > 0 && root != Environment.ProcessId) rootPids.Add(root);
            foreach (var p in all)
            {
                if (p > 0 && p != Environment.ProcessId) pidsToKill.Add(p);
            }
        }

        // 2. Resolve process tree from saved ProcessId if available
        if (instance.ProcessId.HasValue && instance.ProcessId.Value != Environment.ProcessId)
        {
            var (root, all) = ProcessTreeHelper.GetProcessTree(instance.ProcessId.Value);
            if (root > 0 && root != Environment.ProcessId) rootPids.Add(root);
            foreach (var p in all)
            {
                if (p > 0 && p != Environment.ProcessId) pidsToKill.Add(p);
            }
        }

        // 3. Fallback: Search for any running "TikTok LIVE Studio" process using FindProcessId
        if (rootPids.Count == 0 && pidsToKill.Count == 0)
        {
            int? fallbackPid = FindProcessId(instance);
            if (fallbackPid.HasValue && fallbackPid.Value != Environment.ProcessId)
            {
                var (root, all) = ProcessTreeHelper.GetProcessTree(fallbackPid.Value);
                if (root > 0 && root != Environment.ProcessId) rootPids.Add(root);
                foreach (var p in all)
                {
                    if (p > 0 && p != Environment.ProcessId) pidsToKill.Add(p);
                }
            }
        }

        // 4. Fallback: If still no specific PID found, collect any running "TikTok LIVE Studio" processes
        if (rootPids.Count == 0 && pidsToKill.Count == 0)
        {
            try
            {
                var studioProcs = Process.GetProcessesByName("TikTok LIVE Studio");
                foreach (var proc in studioProcs)
                {
                    if (proc.Id != Environment.ProcessId)
                    {
                        pidsToKill.Add(proc.Id);
                    }
                }
            }
            catch { }
        }

        // 5. First kill root ancestors using ForceKillProcessTree to cascade down all children
        foreach (var root in rootPids)
        {
            ForceKillProcessTree(root);
            killed = true;
        }

        // 6. Ensure any remaining child or orphaned processes in this instance's tree are terminated
        foreach (var pid in pidsToKill)
        {
            try
            {
                var p = Process.GetProcessById(pid);
                if (!p.HasExited)
                {
                    ForceKillProcessTree(pid);
                    killed = true;
                }
            }
            catch { }
        }

        // 7. Final cleanup check: if any "TikTok LIVE Studio" process remains alive, terminate them
        try
        {
            var remainingProcs = Process.GetProcessesByName("TikTok LIVE Studio");
            foreach (var proc in remainingProcs)
            {
                if (proc.Id != Environment.ProcessId && !proc.HasExited)
                {
                    ForceKillProcessTree(proc.Id);
                    killed = true;
                }
            }
        }
        catch { }

        instance.ProcessId = null;
        instance.Status = InstanceStatus.Stopped;
        instance.StatusMessage = "Stopped";
        instance.IsWindowHidden = false;
        logger.Info("Process", $"Instance '{instance.Name}' terminated. Status: Stopped (Killed: {killed})");
        return killed;
    }

    public bool IsInstanceRunning(StudioInstanceProfile instance)
    {
        // First check if CDP port is actively listening
        if (IsPortListening("127.0.0.1", instance.CdpPort))
        {
            if (!instance.ProcessId.HasValue)
            {
                instance.ProcessId = GetPidListeningOnPort(instance.CdpPort);
            }
            return true;
        }

        if (instance.ProcessId.HasValue)
        {
            try
            {
                var proc = Process.GetProcessById(instance.ProcessId.Value);
                if (!proc.HasExited) return true;
            }
            catch
            {
                instance.ProcessId = null;
            }
        }

        return false;
    }

    public int? FindProcessId(StudioInstanceProfile instance)
    {
        int? portPid = GetPidListeningOnPort(instance.CdpPort);
        if (portPid.HasValue) return portPid;

        var processes = Process.GetProcessesByName("TikTok LIVE Studio");
        if (processes.Length == 1)
        {
            return processes[0].Id;
        }

        var active = processes.OrderByDescending(p => p.StartTime).FirstOrDefault();
        return active?.Id;
    }

    public static int? GetPidListeningOnPort(int port)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano -p tcp",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(1500);

            foreach (var line in output.Split('\n'))
            {
                if (line.Contains($":{port} ") && line.Contains("LISTENING", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5 && int.TryParse(parts[^1], out int pid))
                    {
                        if (pid == Environment.ProcessId) return null;
                        return pid;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    public static void ForceKillProcessTree(int pid)
    {
        if (pid <= 0 || pid == Environment.ProcessId) return;

        // ขั้นตอนที่ 1: สั่ง .NET Kill process tree โดยตรง
        try
        {
            var proc = Process.GetProcessById(pid);
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
                if (proc.WaitForExit(1000)) return;
            }
        }
        catch { }

        // ขั้นตอนที่ 2: ใช้คำสั่ง Windows taskkill /F /T
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = $"/F /T /PID {pid}",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var proc = Process.Start(psi);
            proc?.WaitForExit(2000);
        }
        catch { }

        // ขั้นตอนที่ 3: หากยังปิดไม่ลง (กรณีติดสิทธิ์ Access is denied จาก UAC) ใช้ Elevated taskkill
        try
        {
            var check = Process.GetProcessById(pid);
            if (!check.HasExited)
            {
                var elevated = new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = $"/F /T /PID {pid}",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(elevated)?.WaitForExit(2500);
            }
        }
        catch { }
    }

    public static void KillAllStudioProcesses()
    {
        try
        {
            var procs = Process.GetProcessesByName("TikTok LIVE Studio");
            foreach (var p in procs)
            {
                if (p.Id != Environment.ProcessId)
                {
                    ForceKillProcessTree(p.Id);
                }
            }
        }
        catch { }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = "/F /IM \"TikTok LIVE Studio.exe\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi)?.WaitForExit(2000);
        }
        catch { }
    }

    private static bool IsPortListening(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            var result = client.BeginConnect(host, port, null, null);
            bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(300));
            if (!success) return false;
            client.EndConnect(result);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool ForwardLoginCallback(StudioInstanceProfile instance, string callbackUrl, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(callbackUrl)) return false;

        string studioExe = TikTokStudioPathResolver.ResolveValidStudioPath(settings.TikTokStudioExePath);
        if (!File.Exists(studioExe)) return false;

        string profileDir = ProfileManagerService.ResolveProfileDirectoryStatic(instance);

        var psi = new ProcessStartInfo
        {
            FileName = studioExe,
            Arguments = $"--user-data-dir=\"{profileDir}\" \"{callbackUrl}\"",
            UseShellExecute = true
        };
        Process.Start(psi);
        return true;
    }

    public static bool CloneSessionFromDefaultProfile(StudioInstanceProfile targetInstance)
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string defaultProfilePath = Path.Combine(appData, "TikTok LIVE Studio");
            if (!Directory.Exists(defaultProfilePath)) return false;

            string targetDir = ProfileManagerService.ResolveProfileDirectoryStatic(targetInstance);
            Directory.CreateDirectory(targetDir);

            // 1. Copy Cookies
            string srcNetwork = Path.Combine(defaultProfilePath, "Network");
            string dstNetwork = Path.Combine(targetDir, "Network");
            if (Directory.Exists(srcNetwork))
            {
                Directory.CreateDirectory(dstNetwork);
                foreach (var file in Directory.GetFiles(srcNetwork, "*Cookies*", SearchOption.TopDirectoryOnly))
                {
                    File.Copy(file, Path.Combine(dstNetwork, Path.GetFileName(file)), true);
                }
            }

            // 2. Copy Local Storage
            string srcStorage = Path.Combine(defaultProfilePath, "Local Storage");
            string dstStorage = Path.Combine(targetDir, "Local Storage");
            if (Directory.Exists(srcStorage))
            {
                Directory.CreateDirectory(dstStorage);
                CopyDirectoryRecursive(srcStorage, dstStorage);
            }

            // 3. Copy Session Storage
            string srcSessionStorage = Path.Combine(defaultProfilePath, "Session Storage");
            string dstSessionStorage = Path.Combine(targetDir, "Session Storage");
            if (Directory.Exists(srcSessionStorage))
            {
                Directory.CreateDirectory(dstSessionStorage);
                CopyDirectoryRecursive(srcSessionStorage, dstSessionStorage);
            }

            // 4. Copy TTStore (AccountService, services.json, login.json, avatarGaussian.png, localStore.json)
            string srcTTStore = Path.Combine(defaultProfilePath, "TTStore");
            string dstTTStore = Path.Combine(targetDir, "TTStore");
            if (Directory.Exists(srcTTStore))
            {
                Directory.CreateDirectory(dstTTStore);
                CopyDirectoryRecursive(srcTTStore, dstTTStore);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
        }
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectoryRecursive(subDir, Path.Combine(targetDir, Path.GetFileName(subDir)));
        }
    }

    private static readonly object _protocolBindLock = new();

    public static bool BindProtocolToProfile(StudioInstanceProfile instance, AppSettings settings)
    {
        lock (_protocolBindLock)
        {
            try
            {
                if (!OperatingSystem.IsWindows()) return false;

                string studioExe = TikTokStudioPathResolver.ResolveValidStudioPath(settings.TikTokStudioExePath);
                if (!File.Exists(studioExe)) return false;

                string command;
                if (instance.LaunchMode == InstanceLaunchMode.DefaultProfile)
                {
                    string dir = Path.GetDirectoryName(studioExe) ?? "";
                    string parentDir = Path.GetDirectoryName(dir) ?? "";
                    string launcherExe = Path.Combine(parentDir, "TikTok LIVE Studio Launcher.exe");
                    if (!File.Exists(launcherExe)) launcherExe = studioExe;
                    command = $"\"{launcherExe}\" \"%1\"";
                }
                else
                {
                    string profileDir = ProfileManagerService.ResolveProfileDirectoryStatic(instance);
                    Directory.CreateDirectory(profileDir);
                    command = $"\"{studioExe}\" --user-data-dir=\"{profileDir}\" \"%1\"";
                }

                using var rootKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\live-studio-app");
                rootKey?.SetValue("", "URL:live-studio-app");
                rootKey?.SetValue("URL Protocol", "");
                using var cmdKey = rootKey?.CreateSubKey(@"shell\open\command");

                // Only rewrite if command changed to minimize registry collisions
                string? existingCmd = cmdKey?.GetValue("")?.ToString();
                if (!string.Equals(existingCmd, command, StringComparison.OrdinalIgnoreCase))
                {
                    cmdKey?.SetValue("", command);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

