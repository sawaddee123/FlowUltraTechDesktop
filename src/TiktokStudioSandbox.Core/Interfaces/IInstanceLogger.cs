using System;
using System.Collections.Generic;
using TiktokStudioSandbox.Core.Models;

namespace TiktokStudioSandbox.Core.Interfaces;

public interface IInstanceLogger
{
    string AccountName { get; }
    string ProfileFolderName { get; }
    int CdpPort { get; }
    string LogFilePath { get; }

    event Action<InstanceLogEntry>? LogAdded;

    IReadOnlyList<InstanceLogEntry> GetRecentLogs();
    void Log(InstanceLogLevel level, string category, string message, string? details = null);
    void Debug(string category, string message, string? details = null);
    void Info(string category, string message, string? details = null);
    void Warn(string category, string message, string? details = null);
    void Error(string category, string message, string? details = null, Exception? exception = null);
    void Error(string category, string message, Exception exception);
    void Clear();
}
