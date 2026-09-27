using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlowUltraTech.Core.Interfaces;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Services;

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
            "FlowUltraTech",
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

    private long _currentFileSizeBytes = -1;

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
                // Batch trim 200 items at once to eliminate continuous array copying on every log call
                int trimCount = Math.Min(200, _logs.Count - MaxInMemoryLogs + 200);
                _logs.RemoveRange(0, trimCount);
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
                if (_currentFileSizeBytes < 0)
                {
                    try
                    {
                        var fi = new FileInfo(_logFilePath);
                        _currentFileSizeBytes = fi.Exists ? fi.Length : 0;
                    }
                    catch
                    {
                        _currentFileSizeBytes = 0;
                    }
                }

                int addedBytes = System.Text.Encoding.UTF8.GetByteCount(text) + 2;

                // Check file size using cached in-memory tracker without blocking on disk metadata
                if (_currentFileSizeBytes + addedBytes > MaxFileSizeBytes)
                {
                    string backupPath = _logFilePath + ".bak";
                    try
                    {
                        if (File.Exists(backupPath)) File.Delete(backupPath);
                        if (File.Exists(_logFilePath)) File.Move(_logFilePath, backupPath);
                    }
                    catch { }
                    _currentFileSizeBytes = 0;
                }

                File.AppendAllText(_logFilePath, text + Environment.NewLine);
                _currentFileSizeBytes += addedBytes;
            }
        }
        catch { }
    }
}
