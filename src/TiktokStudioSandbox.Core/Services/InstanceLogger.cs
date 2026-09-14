using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TiktokStudioSandbox.Core.Interfaces;
using TiktokStudioSandbox.Core.Models;

namespace TiktokStudioSandbox.Core.Services;

public class InstanceLogger : IInstanceLogger
{
    private const int MaxInMemoryLogs = 1000;
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    private readonly object _lock = new();
    private readonly List<InstanceLogEntry> _logs = new();
    private readonly string _logFilePath;

    public string AccountName { get; }
    public string ProfileFolderName { get; }
    public int CdpPort { get; }
    public string LogFilePath => _logFilePath;

    public event Action<InstanceLogEntry>? LogAdded;

    public InstanceLogger(string profileFolderName, int cdpPort, string accountName = "", string? logsDirectory = null)
    {
        ProfileFolderName = string.IsNullOrWhiteSpace(profileFolderName) ? "Default" : profileFolderName;
        CdpPort = cdpPort;
        AccountName = string.IsNullOrWhiteSpace(accountName) ? ProfileFolderName : accountName;

        string baseDir = logsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TiktokStudioSandbox",
            "Logs");

        try
        {
            Directory.CreateDirectory(baseDir);
        }
        catch { }

        string safeName = string.Join("_", ProfileFolderName.Split(Path.GetInvalidFileNameChars()));
        _logFilePath = Path.Combine(baseDir, $"{safeName}_debug.log");

        // Write a session start marker
        AppendToFile($"\n=== [Session Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}] Account: {AccountName} | Folder: {ProfileFolderName} | CDP Port: {CdpPort} ===\n");
    }

    public IReadOnlyList<InstanceLogEntry> GetRecentLogs()
    {
        lock (_lock)
        {
            return _logs.ToList();
        }
    }

    public void Log(InstanceLogLevel level, string category, string message, string? details = null)
    {
        var entry = new InstanceLogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Category = string.IsNullOrWhiteSpace(category) ? "General" : category,
            Message = message,
            Details = details
        };

        lock (_lock)
        {
            if (_logs.Count >= MaxInMemoryLogs)
            {
                _logs.RemoveRange(0, _logs.Count - MaxInMemoryLogs + 1);
            }
            _logs.Add(entry);
        }

        AppendToFile(entry.FullLine);

        try
        {
            LogAdded?.Invoke(entry);
        }
        catch { }
    }

    public void Debug(string category, string message, string? details = null)
        => Log(InstanceLogLevel.Debug, category, message, details);

    public void Info(string category, string message, string? details = null)
        => Log(InstanceLogLevel.Info, category, message, details);

    public void Warn(string category, string message, string? details = null)
        => Log(InstanceLogLevel.Warn, category, message, details);

    public void Error(string category, string message, string? details = null, Exception? exception = null)
    {
        string fullDetails = details ?? string.Empty;
        if (exception != null)
        {
            string exInfo = $"{exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}";
            fullDetails = string.IsNullOrWhiteSpace(fullDetails) ? exInfo : $"{fullDetails}\n{exInfo}";
        }
        Log(InstanceLogLevel.Error, category, message, string.IsNullOrWhiteSpace(fullDetails) ? null : fullDetails);
    }

    public void Error(string category, string message, Exception? exception)
        => Error(category, message, null, exception);

    public void Clear()
    {
        lock (_lock)
        {
            _logs.Clear();
        }
    }

    private void AppendToFile(string text)
    {
        try
        {
            lock (_lock)
            {
                // Check file size to avoid unbounded growth
                if (File.Exists(_logFilePath))
                {
                    var fileInfo = new FileInfo(_logFilePath);
                    if (fileInfo.Length > MaxFileSizeBytes)
                    {
                        string backupPath = _logFilePath + ".bak";
                        try
                        {
                            if (File.Exists(backupPath)) File.Delete(backupPath);
                            File.Move(_logFilePath, backupPath);
                        }
                        catch { }
                    }
                }

                File.AppendAllText(_logFilePath, text + Environment.NewLine);
            }
        }
        catch { }
    }
}
