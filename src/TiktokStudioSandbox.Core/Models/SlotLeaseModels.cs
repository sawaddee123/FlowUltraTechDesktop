using System;

namespace TiktokStudioSandbox.Core.Models;

public class SlotAcquireResult
{
    public bool Success { get; set; }
    public string? LeaseId { get; set; }
    public string? LeaseToken { get; set; }
    public string? Message { get; set; }
    public int StatusCode { get; set; }
}

public class SlotHeartbeatResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int StatusCode { get; set; }
    public bool IsRevoked { get; set; }
    public bool IsExpired { get; set; }
}

public class SlotReleaseResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int StatusCode { get; set; }
}

public class ActiveSlotLease
{
    public string LeaseId { get; set; } = string.Empty;
    public string ProfileFolderName { get; set; } = string.Empty;
    public string Hwid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSuccessfulHeartbeat { get; set; } = DateTime.UtcNow;
    public int ConsecutiveFailures { get; set; } = 0;
    public Action? OnWatchdogTriggered { get; set; }
}
