using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TiktokStudioSandbox.Core.Models;

namespace TiktokStudioSandbox.Core.Interfaces;

public interface ISlotLeaseClientService : IDisposable
{
    string DefaultBaseEndpoint { get; set; }
    IRealtimeClientService? RealtimeService { get; set; }

    Task<SlotAcquireResult> AcquireSlotAsync(string profileFolderName, string hwid, string authToken, string? baseEndpoint = null);
    Task<SlotHeartbeatResult> SendHeartbeatAsync(string leaseId, string hwid, string authToken, string? baseEndpoint = null);
    Task<SlotReleaseResult> ReleaseSlotAsync(string leaseId, string authToken, string? baseEndpoint = null);

    void RegisterActiveLease(string leaseId, string profileFolderName, string hwid, string authToken, Action onWatchdogTriggered);
    void UnregisterActiveLease(string leaseId);
    ActiveSlotLease? GetLease(string leaseId);
    IReadOnlyDictionary<string, ActiveSlotLease> ActiveLeases { get; }

    void StartHeartbeatTimer(TimeSpan? interval = null);
    void StopHeartbeatTimer();
    Task RunHeartbeatAndWatchdogCycleAsync();
}
