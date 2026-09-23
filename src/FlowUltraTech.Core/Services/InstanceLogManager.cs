using System;
using System.Collections.Concurrent;
using System.IO;
using FlowUltraTech.Core.Interfaces;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Services;

public class InstanceLogManager : IInstanceLogManager
{
    private static readonly Lazy<InstanceLogManager> _shared = new(() => new InstanceLogManager());
    public static InstanceLogManager Shared => _shared.Value;

    private readonly ConcurrentDictionary<string, IInstanceLogger> _loggersByFolder = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<int, IInstanceLogger> _loggersByPort = new();
    private readonly string _logsDirectory;

    public string LogsDirectory => _logsDirectory;

    public InstanceLogManager(string? customLogsDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(customLogsDirectory))
        {
            _logsDirectory = customLogsDirectory;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _logsDirectory = Path.Combine(appData, "FlowUltraTech", "Logs");
        }

        try
        {
            Directory.CreateDirectory(_logsDirectory);
        }
        catch { }
    }

    public IInstanceLogger GetLogger(StudioInstanceProfile profile)
    {
        string folder = !string.IsNullOrWhiteSpace(profile.ProfileFolderName) 
            ? profile.ProfileFolderName 
            : (!string.IsNullOrWhiteSpace(profile.SandboxBoxName) ? profile.SandboxBoxName : "Default");

        var logger = GetLogger(folder, profile.CdpPort, profile.Name);
        _loggersByPort[profile.CdpPort] = logger;
        return logger;
    }

    public IInstanceLogger GetLogger(string profileFolderName, int cdpPort, string accountName = "")
    {
        string key = string.IsNullOrWhiteSpace(profileFolderName) ? $"Port_{cdpPort}" : profileFolderName;

        var logger = _loggersByFolder.GetOrAdd(key, _ => 
            new InstanceLogger(profileFolderName, cdpPort, accountName, _logsDirectory));

        if (cdpPort > 0)
        {
            _loggersByPort[cdpPort] = logger;
        }

        return logger;
    }

    public IInstanceLogger? TryGetLoggerByPort(int cdpPort)
    {
        if (_loggersByPort.TryGetValue(cdpPort, out var logger))
        {
            return logger;
        }
        if (cdpPort > 100 && _loggersByPort.TryGetValue(cdpPort - 100, out logger))
        {
            return logger;
        }
        return null;
    }

    public (bool Success, string Logs, string FileName) ReadLogs(string profileFolderName, int cdpPort = 0, int maxLines = 3000, string accountName = "")
    {
        string? targetPath = null;
        string fileName = "";

        if (!string.IsNullOrWhiteSpace(profileFolderName))
        {
            string safeName = string.Join("_", profileFolderName.Split(Path.GetInvalidFileNameChars()));
            string expectedPath = Path.Combine(_logsDirectory, $"{safeName}_debug.log");
            if (File.Exists(expectedPath))
            {
                targetPath = expectedPath;
                fileName = $"{safeName}_debug.log";
            }
            else if (Directory.Exists(_logsDirectory))
            {
                var matches = Directory.GetFiles(_logsDirectory, $"*{safeName}*.log");
                if (matches.Length > 0)
                {
                    targetPath = matches[0];
                    fileName = Path.GetFileName(targetPath);
                }
            }
        }

        if (targetPath == null && !string.IsNullOrWhiteSpace(accountName))
        {
            string safeAccount = string.Join("_", accountName.Split(Path.GetInvalidFileNameChars()));
            string expectedPath = Path.Combine(_logsDirectory, $"{safeAccount}_debug.log");
            if (File.Exists(expectedPath))
            {
                targetPath = expectedPath;
                fileName = $"{safeAccount}_debug.log";
            }
            else if (Directory.Exists(_logsDirectory))
            {
                var matches = Directory.GetFiles(_logsDirectory, $"*{safeAccount}*.log");
                if (matches.Length > 0)
                {
                    targetPath = matches[0];
                    fileName = Path.GetFileName(targetPath);
                }
            }
        }

        if (targetPath == null && cdpPort > 0)
        {
            var logger = TryGetLoggerByPort(cdpPort);
            if (logger is InstanceLogger instLogger && File.Exists(instLogger.LogFilePath))
            {
                targetPath = instLogger.LogFilePath;
                fileName = Path.GetFileName(targetPath);
            }
            else if (Directory.Exists(_logsDirectory))
            {
                var matches = Directory.GetFiles(_logsDirectory, $"*{cdpPort}*.log");
                if (matches.Length > 0)
                {
                    targetPath = matches[0];
                    fileName = Path.GetFileName(targetPath);
                }
            }
        }

        // If still null, try finding the newest log file in _logsDirectory
        if (targetPath == null && Directory.Exists(_logsDirectory))
        {
            try
            {
                var dirInfo = new DirectoryInfo(_logsDirectory);
                var latestFile = System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.OrderByDescending(dirInfo.GetFiles("*.log"), f => f.LastWriteTime));
                if (latestFile != null)
                {
                    targetPath = latestFile.FullName;
                    fileName = latestFile.Name;
                }
            }
            catch { }
        }

        if (targetPath == null || !File.Exists(targetPath))
        {
            return (false, $"[Log file not found for profile: '{profileFolderName}' (Directory: {_logsDirectory})]", "");
        }

        try
        {
            using var fs = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, System.Text.Encoding.UTF8);

            var lines = new System.Collections.Generic.List<string>();
            while (!reader.EndOfStream)
            {
                lines.Add(reader.ReadLine() ?? "");
            }

            if (lines.Count > maxLines)
            {
                lines = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Skip(lines, lines.Count - maxLines));
            }

            return (true, string.Join(Environment.NewLine, lines), fileName);
        }
        catch (Exception ex)
        {
            return (false, $"[Failed to read log file: {ex.Message}]", fileName);
        }
    }
}
