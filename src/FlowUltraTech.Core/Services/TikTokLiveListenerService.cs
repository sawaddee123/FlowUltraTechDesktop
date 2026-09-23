using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TikTokLiveSharp.Client;
using TikTokLiveSharp.Events;
using TikTokLiveSharp.Events.Objects;
using FlowUltraTech.Core.Interfaces;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Services;

public class TikTokLiveListenerService : ITikTokLiveListenerService
{
    private readonly ConcurrentDictionary<string, (TikTokLiveClient Client, CancellationTokenSource Cts, string RoomId)> _activeClients = new(StringComparer.OrdinalIgnoreCase);

    public async Task<bool> StartAsync(
        string uniqueId,
        Action<LiveChatMessageItem> onEventReceived,
        Action<string> onStatusChanged,
        CancellationToken ct = default)
    {
        string normalized = NormalizeUsername(uniqueId);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            onStatusChanged("ชื่อช่องไม่ถูกต้อง");
            return false;
        }

        // If already running for this channel, stop first
        await StopAsync(normalized);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var client = new TikTokLiveClient(normalized, (string?)null);

        onStatusChanged($"กำลังเชื่อมต่อ @{normalized}...");

        // 1. 💬 Chat Message
        client.OnChatMessage += (sender, chat) =>
        {
            try
            {
                var item = new LiveChatMessageItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Type = LiveChatMessageType.Chat,
                    Nickname = chat.Sender?.NickName ?? "ผู้ชม",
                    Username = chat.Sender?.UniqueId ?? normalized,
                    Message = chat.Message ?? string.Empty,
                    Timestamp = DateTime.Now,
                    RawJson = JsonSerializer.Serialize(new
                    {
                        type = "chat",
                        nickname = chat.Sender?.NickName,
                        username = chat.Sender?.UniqueId,
                        userId = chat.Sender?.Id,
                        message = chat.Message,
                        messageId = chat.MessageId,
                        roomId = chat.RoomId,
                        timestamp = chat.TimeStamp
                    }, JsonFormatter.IndentedOptions)
                };
                onEventReceived(item);
            }
            catch { }
        };

        // 2. 🎁 Gift
        client.OnGift += (sender, gift) =>
        {
            try
            {
                string giftName = gift.Gift?.Name ?? "Gift";
                int diamondCost = (int)(gift.Gift?.DiamondCost ?? 0);
                int count = (int)gift.Amount;

                var item = new LiveChatMessageItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Type = LiveChatMessageType.Gift,
                    Nickname = gift.Sender?.NickName ?? "ผู้ชม",
                    Username = gift.Sender?.UniqueId ?? normalized,
                    GiftName = giftName,
                    DiamondCount = diamondCost * (count > 0 ? count : 1),
                    RepeatCount = count > 0 ? count : 1,
                    Message = $"ส่ง {giftName} x{count}{(diamondCost > 0 ? $" ({diamondCost * count} 💎)" : "")}",
                    Timestamp = DateTime.Now,
                    RawJson = JsonSerializer.Serialize(new
                    {
                        type = "gift",
                        nickname = gift.Sender?.NickName,
                        username = gift.Sender?.UniqueId,
                        userId = gift.Sender?.Id,
                        giftName = giftName,
                        diamondCost = diamondCost,
                        amount = count,
                        streakFinished = gift.StreakFinished
                    }, JsonFormatter.IndentedOptions)
                };
                onEventReceived(item);
            }
            catch { }
        };

        // 3. ❤️ Like
        client.OnLike += (sender, like) =>
        {
            try
            {
                var item = new LiveChatMessageItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Type = LiveChatMessageType.Like,
                    Nickname = like.Sender?.NickName ?? "ผู้ชม",
                    Username = like.Sender?.UniqueId ?? normalized,
                    Message = $"กดถูกใจ {(like.Count > 1 ? $"{like.Count} ครั้ง " : "")}(รวม {like.Total} ไลก์)",
                    Timestamp = DateTime.Now,
                    RawJson = JsonSerializer.Serialize(new
                    {
                        type = "like",
                        nickname = like.Sender?.NickName,
                        username = like.Sender?.UniqueId,
                        userId = like.Sender?.Id,
                        count = like.Count,
                        total = like.Total
                    }, JsonFormatter.IndentedOptions)
                };
                onEventReceived(item);
            }
            catch { }
        };

        // 4. 👤 Follow
        client.OnFollow += (sender, follow) =>
        {
            try
            {
                var item = new LiveChatMessageItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Type = LiveChatMessageType.Follow,
                    Nickname = follow.User?.NickName ?? "ผู้ชม",
                    Username = follow.User?.UniqueId ?? normalized,
                    Message = "ติดตามช่องแล้ว!",
                    Timestamp = DateTime.Now,
                    RawJson = JsonSerializer.Serialize(new
                    {
                        type = "follow",
                        nickname = follow.User?.NickName,
                        username = follow.User?.UniqueId,
                        userId = follow.User?.Id,
                        followCount = follow.FollowCount
                    }, JsonFormatter.IndentedOptions)
                };
                onEventReceived(item);
            }
            catch { }
        };

        // 5. 🔄 Share
        client.OnShare += (sender, share) =>
        {
            try
            {
                var item = new LiveChatMessageItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Type = LiveChatMessageType.Share,
                    Nickname = share.User?.NickName ?? "ผู้ชม",
                    Username = share.User?.UniqueId ?? normalized,
                    Message = $"แชร์ไลฟ์ {(share.Amount > 1 ? $"x{share.Amount}" : "")}",
                    Timestamp = DateTime.Now,
                    RawJson = JsonSerializer.Serialize(new
                    {
                        type = "share",
                        nickname = share.User?.NickName,
                        username = share.User?.UniqueId,
                        userId = share.User?.Id,
                        amount = share.Amount
                    }, JsonFormatter.IndentedOptions)
                };
                onEventReceived(item);
            }
            catch { }
        };

        // 6. 🚪 Join
        client.OnJoin += (sender, join) =>
        {
            try
            {
                var item = new LiveChatMessageItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Type = LiveChatMessageType.Other,
                    Nickname = join.User?.NickName ?? "ผู้ชม",
                    Username = join.User?.UniqueId ?? normalized,
                    Message = "เข้าร่วมห้องไลฟ์",
                    Timestamp = DateTime.Now,
                    RawJson = JsonSerializer.Serialize(new
                    {
                        type = "join",
                        nickname = join.User?.NickName,
                        username = join.User?.UniqueId,
                        userId = join.User?.Id
                    }, JsonFormatter.IndentedOptions)
                };
                onEventReceived(item);
            }
            catch { }
        };

        // 7. ⚠️ Exception handling
        client.OnException += (sender, ex) =>
        {
            onStatusChanged($"เกิดข้อผิดพลาดในการเชื่อมต่อ @{normalized}: {ex.Message}");
        };

        // 8. 🔴 Live Status Events
        client.OnLiveEnded += (sender, msg) =>
        {
            onStatusChanged($"ไลฟ์ของ @{normalized} สิ้นสุดแล้ว");
        };

        client.OnLivePaused += (sender, msg) =>
        {
            onStatusChanged($"ไลฟ์ของ @{normalized} หยุดชั่วคราว");
        };

        client.OnLiveResumed += (sender, msg) =>
        {
            onStatusChanged($"ไลฟ์ของ @{normalized} ดำเนินการต่อ");
        };

        try
        {
            // Start connection on background thread
            string roomId = await client.Start(cts.Token);
            _activeClients[normalized] = (client, cts, roomId);

            onStatusChanged($"เชื่อมต่อ @{normalized} สำเร็จ (Room ID: {roomId}) - กำลังรับข้อมูล...");
            return true;
        }
        catch (Exception ex)
        {
            onStatusChanged($"เชื่อมต่อ @{normalized} ไม่สำเร็จ: {ex.Message}");
            return false;
        }
    }

    public async Task StopAsync(string uniqueId)
    {
        string normalized = NormalizeUsername(uniqueId);
        if (_activeClients.TryRemove(normalized, out var tuple))
        {
            try
            {
                tuple.Cts.Cancel();
                await tuple.Client.Stop();
            }
            catch { }
            finally
            {
                tuple.Cts.Dispose();
            }
        }
    }

    public bool IsConnected(string uniqueId)
    {
        string normalized = NormalizeUsername(uniqueId);
        return _activeClients.TryGetValue(normalized, out var tuple) && tuple.Client.Connected;
    }

    public string? GetCurrentRoomId(string uniqueId)
    {
        string normalized = NormalizeUsername(uniqueId);
        return _activeClients.TryGetValue(normalized, out var tuple) ? tuple.RoomId : null;
    }

    public void Dispose()
    {
        foreach (var key in _activeClients.Keys)
        {
            _ = StopAsync(key);
        }
    }

    private static string NormalizeUsername(string username)
    {
        return username.Trim().TrimStart('@');
    }
}
