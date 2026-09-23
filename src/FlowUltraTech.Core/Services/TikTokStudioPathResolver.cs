using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FlowUltraTech.Core.Services;

public static class TikTokStudioPathResolver
{
    public static string ResolveValidStudioPath(string? currentPath)
    {
        if (!string.IsNullOrWhiteSpace(currentPath) && File.Exists(currentPath))
        {
            // หาก currentPath ชี้ไปที่ Launcher.exe ให้ค้นหา TikTok LIVE Studio.exe ตัวจริง เพื่อให้สามารถควบคุม PID และส่ง Arguments ได้โดยตรง
            if (!currentPath.EndsWith("Launcher.exe", StringComparison.OrdinalIgnoreCase))
            {
                return currentPath;
            }

            string launcherDir = Path.GetDirectoryName(currentPath) ?? @"C:\Program Files\TikTok LIVE Studio";
            string? realExe = FindRealStudioExeInDir(launcherDir);
            if (!string.IsNullOrEmpty(realExe)) return realExe;
        }

        string baseDir = @"C:\Program Files\TikTok LIVE Studio";
        if (Directory.Exists(baseDir))
        {
            string? realExe = FindRealStudioExeInDir(baseDir);
            if (!string.IsNullOrEmpty(realExe)) return realExe;

            string baseTarget = Path.Combine(baseDir, "TikTok LIVE Studio.exe");
            if (File.Exists(baseTarget)) return baseTarget;

            string launcher = Path.Combine(baseDir, "TikTok LIVE Studio Launcher.exe");
            if (File.Exists(launcher)) return launcher;
        }

        // Check LocalAppData Programs
        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string localTarget = Path.Combine(localApp, @"Programs\TikTok LIVE Studio\TikTok LIVE Studio.exe");
        if (File.Exists(localTarget)) return localTarget;

        return currentPath ?? @"C:\Program Files\TikTok LIVE Studio\TikTok LIVE Studio.exe";
    }

    private static string? FindRealStudioExeInDir(string baseDir)
    {
        if (!Directory.Exists(baseDir)) return null;

        // 1. ตรวจสอบจาก launcher_config.json
        string configPath = Path.Combine(baseDir, "launcher_config.json");
        if (File.Exists(configPath))
        {
            try
            {
                string json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("cur_path", out var curPathProp))
                {
                    string? curPath = curPathProp.GetString();
                    if (!string.IsNullOrEmpty(curPath))
                    {
                        string target = Path.Combine(baseDir, curPath, "TikTok LIVE Studio.exe");
                        if (File.Exists(target)) return target;
                    }
                }
            }
            catch { }
        }

        // 2. ค้นหาโฟลเดอร์เวอร์ชันเรียงจากใหม่สุด (เช่น 1.35.2, 1.36.0...)
        var subDirs = Directory.GetDirectories(baseDir)
            .OrderByDescending(d => d)
            .ToList();

        foreach (var dir in subDirs)
        {
            string target = Path.Combine(dir, "TikTok LIVE Studio.exe");
            if (File.Exists(target)) return target;
        }

        return null;
    }
}
