using System;
using System.Threading.Tasks;

namespace TiktokStudioSandbox.Core.Interfaces;

public class LicenseUpdateEventArgs : EventArgs
{
    public DateTime? ExpiredAt { get; set; }
    public int? MaxTiktokAccounts { get; set; }
    public string? Status { get; set; }
    public string? Plan { get; set; }
}

public class QuotaReducedEventArgs : EventArgs
{
    public int NewMax { get; set; }
    public string[] RevokedLeaseIds { get; set; } = Array.Empty<string>();
}

public class LicenseKickoutEventArgs : EventArgs
{
    public string Reason { get; set; } = string.Empty;
}

public interface IRealtimeClientService : IDisposable
{
    bool IsConnected { get; }
    event EventHandler<QuotaReducedEventArgs>? OnQuotaReduced;
    event EventHandler<LicenseKickoutEventArgs>? OnLicenseKickout;
    event EventHandler<LicenseUpdateEventArgs>? OnLicenseUpdated;

    Task ConnectAsync(string serverUrl, string token);
    Task DisconnectAsync();
    Task<Models.SlotHeartbeatResult> SendSlotHeartbeatAsync(string leaseId, string hwid, TimeSpan? timeout = null);
}
