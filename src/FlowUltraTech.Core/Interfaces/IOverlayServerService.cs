using System;
using System.Threading;
using System.Threading.Tasks;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Interfaces;

public interface IOverlayServerService : IDisposable
{
    bool IsRunning { get; }
    int ActivePort { get; }
    Task<int> StartAsync(int preferredPort = 19450, CancellationToken ct = default);
    Task StopAsync();
    string GetOverlayUrl(Guid profileId);
    OverlayConfig GetConfig(Guid profileId);
    Task UpdateConfigAsync(Guid profileId, OverlayConfig config);
    Task BroadcastLiveEventAsync(Guid profileId, OverlayLiveEventPayload payload);
    Task TriggerFlashDealAsync(Guid profileId, string title, int durationSeconds);
    Task StopFlashDealAsync(Guid profileId);
    int GetConnectedClientCount(Guid profileId);
}
