using System;
using System.Collections.Concurrent;
using System.IO;
using TiktokStudioSandbox.Core.Interfaces;
using TiktokStudioSandbox.Core.Models;

namespace TiktokStudioSandbox.Core.Services;

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
            _logsDirectory = Path.Combine(appData, "TiktokStudioSandbox", "Logs");
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
        return null;
    }
}
