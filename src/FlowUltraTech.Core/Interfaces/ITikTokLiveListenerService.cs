using System;
using System.Threading;
using System.Threading.Tasks;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Interfaces;

public interface ITikTokLiveListenerService : IDisposable
{
    Task<bool> StartAsync(
        string uniqueId,
        Action<LiveChatMessageItem> onEventReceived,
        Action<string> onStatusChanged,
        CancellationToken ct = default);

    Task StopAsync(string uniqueId);

    bool IsConnected(string uniqueId);

    string? GetCurrentRoomId(string uniqueId);
}
