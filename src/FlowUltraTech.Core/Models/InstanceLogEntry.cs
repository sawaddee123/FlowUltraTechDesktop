using System;

namespace FlowUltraTech.Core.Models;

public enum InstanceLogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

public class InstanceLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public InstanceLogLevel Level { get; set; } = InstanceLogLevel.Info;
    public string Category { get; set; } = "General";
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }

    public string FormattedTime => Timestamp.ToString("HH:mm:ss.fff");
    public string FormattedDate => Timestamp.ToString("yyyy-MM-dd");

    public string LevelDisplay => Level switch
    {
        InstanceLogLevel.Debug => "DEBUG",
        InstanceLogLevel.Info => "INFO",
        InstanceLogLevel.Warn => "WARN",
        InstanceLogLevel.Error => "ERROR",
        _ => "INFO"
    };

    public string FullLine
    {
        get
        {
            string line = $"[{FormattedTime}] [{LevelDisplay,-5}] [{Category}] {Message}";
            if (!string.IsNullOrWhiteSpace(Details))
            {
                line += $"\n    Details: {Details}";
            }
            return line;
        }
    }
}
