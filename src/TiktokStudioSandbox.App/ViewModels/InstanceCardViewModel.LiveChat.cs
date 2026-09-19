using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TiktokStudioSandbox.Core.Interfaces;
using TiktokStudioSandbox.Core.Models;
using TiktokStudioSandbox.Core.Services;

namespace TiktokStudioSandbox.App.ViewModels;

public partial class InstanceCardViewModel : ObservableObject
{
    // ==========================================
    // 💬 Real-time Live Chat Monitor Properties
    // ==========================================
    public ObservableCollection<LiveChatMessageItem> LiveChatMessages { get; } = new();
    public ICollectionView FilteredLiveChatMessages { get; }

    [ObservableProperty]
    private bool _isLiveChatMonitoring = true;

    [ObservableProperty]
    private string _chatSearchText = string.Empty;

    [ObservableProperty]
    private string _chatTypeFilter = "ALL";

    [ObservableProperty]
    private bool _isAutoReplyEnabled = true;

    [ObservableProperty]
    private LiveChatMessageItem? _selectedChatMessage;

    [ObservableProperty]
    private string _liveChatStatus = "พร้อมมอนิเตอร์แชทสด";

    [ObservableProperty]
    private int _totalChatCount;

    [ObservableProperty]
    private int _totalGiftCount;

    [ObservableProperty]
    private int _totalDiamondsCount;

    [ObservableProperty]
    private int _totalAutoRepliesCount;

    [ObservableProperty]
    private int _totalOtherCount;

    [ObservableProperty]
    private int _totalLikeFollowCount;

    [ObservableProperty]
    private bool _filterShowChat = true;

    [ObservableProperty]
    private bool _filterShowGift = true;

    [ObservableProperty]
    private bool _filterShowLikeFollow = true;

    [ObservableProperty]
    private bool _filterShowAutoReply = true;

    [ObservableProperty]
    private bool _filterShowOther = true;

    public string FilterChatLabel => $"💬 แชท ({TotalChatCount})";
    public string FilterGiftLabel => TotalDiamondsCount > 0 
        ? $"🎁 ของขวัญ ({TotalGiftCount} [{TotalDiamondsCount} 💎])" 
        : $"🎁 ของขวัญ ({TotalGiftCount})";
    public string FilterLikeFollowLabel => $"❤️ ไลก์/ติดตาม ({TotalLikeFollowCount})";
    public string FilterAutoReplyLabel => $"🤖 บอทตอบ ({TotalAutoRepliesCount})";
    public string FilterOtherLabel => $"📦 อื่นๆ ({TotalOtherCount})";

    public string[] ChatTypeOptions => ["ALL", "Chat (แชท)", "Gift (ของขวัญ)", "Like/Follow (ถูกใจ/ติดตาม)", "AutoReply (บอทตอบ)", "Other (อื่นๆ / Raw JSON)"];
    public string LiveChatCountSummary => $"ทั้งหมด {LiveChatMessages.Count} รายการ (แชท: {TotalChatCount}, ของขวัญ: {TotalGiftCount}{(TotalDiamondsCount > 0 ? $" [{TotalDiamondsCount} 💎]" : "")}, ไลก์/ติดตาม: {TotalLikeFollowCount}, อื่นๆ: {TotalOtherCount})";
    public string LiveChatMonitorButtonText => IsLiveChatMonitoring ? "🔴 หยุดมอนิเตอร์ชั่วคราว" : "▶ เริ่มมอนิเตอร์แชทสด";
    public string LiveChatMonitorBadgeText => IsLiveChatMonitoring
        ? (IsRunning ? "🟢 มอนิเตอร์สด (แอปเปิดอยู่)" : "🟢 มอนิเตอร์สด (ก่อนเปิดแอป)")
        : "⚪ พักการมอนิเตอร์";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveChannelDisplay))]
    private string _streamerUsername = string.Empty;

    [ObservableProperty]
    private string _channelStatusText = string.Empty;

    public string ActiveChannelDisplay
    {
        get
        {
            string target = !string.IsNullOrWhiteSpace(StreamerUsername)
                ? NormalizeUsername(StreamerUsername)
                : (!string.IsNullOrWhiteSpace(LoggedInDisplayId) ? NormalizeUsername(LoggedInDisplayId) : string.Empty);

            return !string.IsNullOrWhiteSpace(target)
                ? $"📺 ตรวจจับ: @{target}"
                : "📺 ยังไม่ได้ระบุช่อง";
        }
    }

    public static string NormalizeUsername(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string clean = raw.Trim();
        int atIdx = clean.IndexOf('@');
        if (atIdx >= 0)
        {
            clean = clean.Substring(atIdx + 1);
        }
        else if (clean.Contains("tiktok.com/", StringComparison.OrdinalIgnoreCase))
        {
            int domainIdx = clean.IndexOf("tiktok.com/", StringComparison.OrdinalIgnoreCase);
            clean = clean.Substring(domainIdx + "tiktok.com/".Length);
        }
        int slashIdx = clean.IndexOf('/');
        if (slashIdx >= 0)
        {
            clean = clean.Substring(0, slashIdx);
        }
        int qIdx = clean.IndexOf('?');
        if (qIdx >= 0)
        {
            clean = clean.Substring(0, qIdx);
        }
        return clean.Trim();
    }


    private void EnsureChatTimer()
    {
        if (_chatMonitorTimer == null)
        {
            try
            {
                _chatMonitorTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                _chatMonitorTimer.Tick += async (_, _) =>
                {
                    if (IsLiveChatMonitoring)
                    {
                        await PollLiveChatInternalAsync();
                    }
                };
            }
            catch { }
        }
    }

    partial void OnIsLiveChatMonitoringChanged(bool value)
    {
        EnsureChatTimer();
        if (value)
        {
            _chatMonitorTimer?.Start();
            _ = StartLiveChatMonitoringAsync();
        }
        else
        {
            _chatMonitorTimer?.Stop();
            _ = StopLiveChatMonitoringAsync();
        }
        OnPropertyChanged(nameof(LiveChatMonitorButtonText));
        OnPropertyChanged(nameof(LiveChatMonitorBadgeText));
    }

    partial void OnChatSearchTextChanged(string value) => FilteredLiveChatMessages?.Refresh();
    partial void OnChatTypeFilterChanged(string value) => FilteredLiveChatMessages?.Refresh();
    partial void OnFilterShowChatChanged(bool value) => FilteredLiveChatMessages?.Refresh();
    partial void OnFilterShowGiftChanged(bool value) => FilteredLiveChatMessages?.Refresh();
    partial void OnFilterShowLikeFollowChanged(bool value) => FilteredLiveChatMessages?.Refresh();
    partial void OnFilterShowAutoReplyChanged(bool value) => FilteredLiveChatMessages?.Refresh();
    partial void OnFilterShowOtherChanged(bool value) => FilteredLiveChatMessages?.Refresh();

    [RelayCommand]
    public void SelectAllFilters()
    {
        FilterShowChat = true;
        FilterShowGift = true;
        FilterShowLikeFollow = true;
        FilterShowAutoReply = true;
        FilterShowOther = true;
    }


    private bool FilterLiveChatMessageItem(object obj)
    {
        if (obj is not LiveChatMessageItem item) return false;

        bool typeMatch = item.Type switch
        {
            LiveChatMessageType.Chat or LiveChatMessageType.SentMessage => FilterShowChat,
            LiveChatMessageType.Gift => FilterShowGift,
            LiveChatMessageType.Like or LiveChatMessageType.Follow or LiveChatMessageType.Share => FilterShowLikeFollow,
            LiveChatMessageType.AutoReply => FilterShowAutoReply,
            LiveChatMessageType.Other => FilterShowOther,
            _ => true
        };

        if (item.IsAutoReplied && !FilterShowAutoReply && !FilterShowChat)
        {
            typeMatch = false;
        }

        if (!typeMatch) return false;

        if (!string.IsNullOrWhiteSpace(ChatSearchText))
        {
            bool match = (item.SenderDisplay != null && item.SenderDisplay.Contains(ChatSearchText, StringComparison.OrdinalIgnoreCase)) ||
                         (item.Message != null && item.Message.Contains(ChatSearchText, StringComparison.OrdinalIgnoreCase)) ||
                         (item.GiftName != null && item.GiftName.Contains(ChatSearchText, StringComparison.OrdinalIgnoreCase)) ||
                         (item.AutoReplyContent != null && item.AutoReplyContent.Contains(ChatSearchText, StringComparison.OrdinalIgnoreCase)) ||
                         (item.RawJson != null && item.RawJson.Contains(ChatSearchText, StringComparison.OrdinalIgnoreCase));
            if (!match) return false;
        }

        return true;
    }


    // ==========================================
    // 💬 Live Chat Monitor Commands & Polling
    // ==========================================
    public async Task StartLiveChatMonitoringAsync()
    {
        if (!IsLiveChatMonitoring) return;

        string? targetChannel = !string.IsNullOrWhiteSpace(StreamerUsername)
            ? NormalizeUsername(StreamerUsername)
            : (!string.IsNullOrWhiteSpace(LoggedInDisplayId) ? NormalizeUsername(LoggedInDisplayId) : null);

        if (string.IsNullOrWhiteSpace(targetChannel))
        {
            RunOnUi(() =>
            {
                LiveChatStatus = "พร้อมมอนิเตอร์ (กรุณาระบุ Channel Username ในช่องตรวจจับ)";
            });
            return;
        }

        if (!string.IsNullOrEmpty(_currentListeningChannel) && !string.Equals(_currentListeningChannel, targetChannel, StringComparison.OrdinalIgnoreCase))
        {
            await _liveListenerService.StopAsync(_currentListeningChannel);
        }
        _currentListeningChannel = targetChannel;

        RunOnUi(() =>
        {
            LiveChatStatus = $"กำลังเชื่อมต่อ @{targetChannel}...";
        });
        Logger.Info("Chat", $"เริ่มการเชื่อมต่อมอนิเตอร์สดช่อง @{targetChannel}");

        bool success = await _liveListenerService.StartAsync(
            targetChannel,
            onEventReceived: (item) =>
            {
                string signature = $"{item.Timestamp:HHmmss}_{item.Type}_{item.Username}_{item.Message}_{item.GiftName}_{item.RepeatCount}";
                if (!TryAddChatSignature(signature)) return;

                RunOnUi(() =>
                {
                    if (LiveChatMessages.Count > 1500) LiveChatMessages.RemoveAt(0);
                    LiveChatMessages.Add(item);

                    if (item.Type == LiveChatMessageType.Chat || item.Type == LiveChatMessageType.SentMessage)
                        TotalChatCount++;
                    else if (item.Type == LiveChatMessageType.Gift)
                    {
                        TotalGiftCount += item.RepeatCount;
                        TotalDiamondsCount += item.DiamondCount * item.RepeatCount;
                    }
                    else if (item.Type == LiveChatMessageType.Like || item.Type == LiveChatMessageType.Follow || item.Type == LiveChatMessageType.Share)
                    {
                        TotalLikeFollowCount++;
                    }
                    else if (item.Type == LiveChatMessageType.Other)
                    {
                        TotalOtherCount++;
                    }

                    if (item.IsAutoReplied || item.Type == LiveChatMessageType.AutoReply)
                        TotalAutoRepliesCount++;

                    NotifyLiveChatCounts();
                    LiveChatStatus = $"อัปเดตแชทสด @{targetChannel} ({DateTime.Now:HH:mm:ss})";
                });

                Logger.Info("Chat", $"[{item.TypeDisplay}] {item.SenderDisplay}: {item.Message}", item.RawJson);

                // Auto-reply rule evaluation (only when TikTok Studio is running, via UI element controller)
                if (IsAutoReplyEnabled && Status == InstanceStatus.Running && _settings.Rules != null && _settings.Rules.Count > 0)
                {
                    if (item.Type == LiveChatMessageType.Chat && !string.IsNullOrWhiteSpace(item.Message))
                    {
                        var chatMsg = new LiveChatMessage(item.Username, item.Nickname, item.Message);
                        string? replyText = _ruleEngine.EvaluateChatRule(_settings.Rules, chatMsg);
                        if (!string.IsNullOrWhiteSpace(replyText))
                        {
                            item.IsAutoReplied = true;
                            item.AutoReplyContent = replyText;
                            _ = Task.Run(async () =>
                            {
                                await _orchestrator.SendChatAsync(Profile, replyText);
                            });
                        }
                    }
                    else if (item.Type == LiveChatMessageType.Gift)
                    {
                        var giftMsg = new LiveGiftMessage(item.Username, item.Nickname, item.GiftName ?? "Gift", item.DiamondCount, item.RepeatCount);
                        string? replyText = _ruleEngine.EvaluateGiftRule(_settings.Rules, giftMsg);
                        if (!string.IsNullOrWhiteSpace(replyText))
                        {
                            item.IsAutoReplied = true;
                            item.AutoReplyContent = replyText;
                            _ = Task.Run(async () =>
                            {
                                await _orchestrator.SendChatAsync(Profile, replyText);
                            });
                        }
                    }
                }
            },
            onStatusChanged: (status) =>
            {
                RunOnUi(() =>
                {
                    LiveChatStatus = status;
                });
                Logger.Info("Chat", $"[LiveStatus] {status}");
            });

        if (!success && Status == InstanceStatus.Running)
        {
            _ = PollLiveChatInternalAsync();
        }
    }

    public async Task StopLiveChatMonitoringAsync()
    {
        string? targetChannel = _currentListeningChannel ?? (!string.IsNullOrWhiteSpace(StreamerUsername)
            ? NormalizeUsername(StreamerUsername)
            : (!string.IsNullOrWhiteSpace(LoggedInDisplayId) ? NormalizeUsername(LoggedInDisplayId) : null));

        if (!string.IsNullOrWhiteSpace(targetChannel))
        {
            await _liveListenerService.StopAsync(targetChannel);
        }
        _currentListeningChannel = null;
        RunOnUi(() =>
        {
            LiveChatStatus = "หยุดมอนิเตอร์ชั่วคราว";
        });
    }

    public async Task PollLiveChatInternalAsync()
    {
        if (!IsLiveChatMonitoring) return;

        string? targetChannel = !string.IsNullOrWhiteSpace(StreamerUsername)
            ? NormalizeUsername(StreamerUsername)
            : (!string.IsNullOrWhiteSpace(LoggedInDisplayId) ? NormalizeUsername(LoggedInDisplayId) : null);

        if (!string.IsNullOrWhiteSpace(targetChannel) && _liveListenerService.IsConnected(targetChannel))
        {
            // Already connected and streaming via TikTokLive_Sharp
            return;
        }

        try
        {
            int targetPort = Status == InstanceStatus.Running ? CdpPort : (CdpPort > 0 ? CdpPort + 100 : 9225);
            var fetchedItems = await _cdpService.FetchLiveChatMessagesAsync(targetPort, targetChannel);
            if (fetchedItems == null || fetchedItems.Count == 0)
            {
                RunOnUi(() =>
                {
                    if (LiveChatMessages.Count == 0)
                    {
                        LiveChatStatus = !string.IsNullOrWhiteSpace(targetChannel)
                            ? (Status == InstanceStatus.Running
                                ? $"กำลังมอนิเตอร์ @{targetChannel} (รอตรวจจับข้อความ... {DateTime.Now:HH:mm:ss})"
                                : $"กำลังมอนิเตอร์ @{targetChannel} (กด '🌐 เปิดหน้าไลฟ์' เพื่อเปิดเบราว์เซอร์ตรวจจับ หรือกด '▶ เริ่มทำงาน')")
                            : $"พร้อมมอนิเตอร์ (ระบุช่องเป้าหมาย หรือกด '🌐 เปิดหน้าไลฟ์')";
                    }
                });
                return;
            }

            var newItems = new List<LiveChatMessageItem>();
            foreach (var item in fetchedItems)
            {
                string signature = $"{item.Timestamp:HHmmss}_{item.Type}_{item.Username}_{item.Message}_{item.GiftName}_{item.RepeatCount}";
                if (TryAddChatSignature(signature))
                {
                    newItems.Add(item);
                }
            }

            if (newItems.Count == 0)
            {
                RunOnUi(() =>
                {
                    LiveChatStatus = $"มอนิเตอร์ @{targetChannel} (รับแล้ว {LiveChatMessages.Count} รายการ - รอแชทใหม่ {DateTime.Now:HH:mm:ss})";
                });
                return;
            }

            foreach (var item in newItems)
            {
                // Evaluate rules if AutoReply is enabled
                if (IsAutoReplyEnabled && _settings.Rules != null && _settings.Rules.Count > 0)
                {
                    if (item.Type == LiveChatMessageType.Chat && !string.IsNullOrWhiteSpace(item.Message))
                    {
                        var chatMsg = new LiveChatMessage(item.Username, item.Nickname, item.Message);
                        string? replyText = _ruleEngine.EvaluateChatRule(_settings.Rules, chatMsg);
                        if (!string.IsNullOrWhiteSpace(replyText))
                        {
                            item.IsAutoReplied = true;
                            item.AutoReplyContent = replyText;

                            _ = Task.Run(async () =>
                            {
                                await _orchestrator.SendChatAsync(Profile, replyText);
                            });
                        }
                    }
                    else if (item.Type == LiveChatMessageType.Gift)
                    {
                        var giftMsg = new LiveGiftMessage(item.Username, item.Nickname, item.GiftName ?? "Gift", item.DiamondCount, item.RepeatCount);
                        string? replyText = _ruleEngine.EvaluateGiftRule(_settings.Rules, giftMsg);
                        if (!string.IsNullOrWhiteSpace(replyText))
                        {
                            item.IsAutoReplied = true;
                            item.AutoReplyContent = replyText;

                            _ = Task.Run(async () =>
                            {
                                await _orchestrator.SendChatAsync(Profile, replyText);
                            });
                        }
                    }
                }
            }

            RunOnUi(() =>
            {
                foreach (var item in newItems)
                {
                    if (LiveChatMessages.Count > 1500) LiveChatMessages.RemoveAt(0);
                    LiveChatMessages.Add(item);

                    if (item.Type == LiveChatMessageType.Chat || item.Type == LiveChatMessageType.SentMessage)
                        TotalChatCount++;
                    else if (item.Type == LiveChatMessageType.Gift)
                    {
                        TotalGiftCount += item.RepeatCount;
                        TotalDiamondsCount += item.DiamondCount * item.RepeatCount;
                    }
                    else if (item.Type == LiveChatMessageType.Like || item.Type == LiveChatMessageType.Follow || item.Type == LiveChatMessageType.Share)
                    {
                        TotalLikeFollowCount++;
                    }
                    else if (item.Type == LiveChatMessageType.Other)
                    {
                        TotalOtherCount++;
                    }

                    if (item.IsAutoReplied || item.Type == LiveChatMessageType.AutoReply)
                        TotalAutoRepliesCount++;
                }

                NotifyLiveChatCounts();
                LiveChatStatus = $"อัปเดตแชทสดล่าสุด {DateTime.Now:HH:mm:ss} (รับข้อความใหม่ {newItems.Count} ข้อความ)";
            });

            Logger.Info("Chat", $"[LiveChat] ตรวจพบเหตุการณ์ใหม่ {newItems.Count} รายการ (@{targetChannel ?? "all"})",
                string.Join("\n", newItems.Select(n => $"[{n.FormattedTime}] [{n.TypeDisplay}] {n.SenderDisplay}: {n.Message}")));
        }
        catch (Exception ex)
        {
            Logger.Error("Chat", $"มอนิเตอร์แชทสดขัดข้อง: {ex.Message}", ex);
            LiveChatStatus = $"มอนิเตอร์แชทสดล้มเหลว: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task SendLiveChatAsync()
    {
        if (Status != InstanceStatus.Running)
        {
            LiveChatStatus = "หน้าต่างยังไม่ได้เปิดทำงาน (กรุณากด Start ก่อน)";
            return;
        }

        if (string.IsNullOrWhiteSpace(TestChatMessage)) return;

        string msgToSend = TestChatMessage.Trim();
        LiveChatStatus = "กำลังส่งข้อความ...";
        bool ok = await _orchestrator.SendChatAsync(Profile, msgToSend);
        if (ok)
        {
            LiveChatStatus = $"ส่งข้อความสำเร็จ ({DateTime.Now:HH:mm:ss})";
            var sentItem = new LiveChatMessageItem
            {
                Type = LiveChatMessageType.SentMessage,
                Username = LoggedInDisplayId ?? "Me",
                Nickname = LoggedInNickname ?? "เจ้าของไลฟ์",
                Message = msgToSend,
                Timestamp = DateTime.Now
            };
            RunOnUi(() =>
            {
                LiveChatMessages.Add(sentItem);
                TotalChatCount++;
                NotifyLiveChatCounts();
            });
            TestChatMessage = string.Empty;
        }
        else
        {
            LiveChatStatus = "ส่งไม่สำเร็จ (ตรวจพอร์ต CDP หรือห้องไลฟ์)";
        }
    }

    [RelayCommand]
    public async Task SendQuickTemplate(string? template)
    {
        if (!string.IsNullOrWhiteSpace(template))
        {
            TestChatMessage = template;
            await SendLiveChatAsync();
        }
    }

    [RelayCommand]
    public void ClearLiveChat()
    {
        LiveChatMessages.Clear();
        _seenChatSignatures.Clear();
        _chatSignatureQueue.Clear();
        SelectedChatMessage = null;
        TotalChatCount = 0;
        TotalGiftCount = 0;
        TotalDiamondsCount = 0;
        TotalLikeFollowCount = 0;
        TotalAutoRepliesCount = 0;
        TotalOtherCount = 0;
        NotifyLiveChatCounts();
        LiveChatStatus = "ล้างประวัติแชทสดเรียบร้อย";
        Logger.Info("Chat", "ล้างประวัติแชทสดและตัวนับเรียบร้อย");
    }

    [RelayCommand]
    public void CopyAllLiveChat()
    {
        var sb = new StringBuilder();
        foreach (LiveChatMessageItem item in FilteredLiveChatMessages)
        {
            sb.AppendLine(item.FullText);
        }
        if (sb.Length > 0)
        {
            Clipboard.SetText(sb.ToString());
            LiveChatStatus = "คัดลอกแชทสดทั้งหมดลง Clipboard เรียบร้อย!";
        }
    }

    [RelayCommand]
    public void CopyAllLiveChatJson()
    {
        var list = FilteredLiveChatMessages.Cast<LiveChatMessageItem>().Select(m => m.FormattedJson).ToList();
        if (list.Count > 0)
        {
            string allJson = "[\n" + string.Join(",\n", list) + "\n]";
            Clipboard.SetText(allJson);
            LiveChatStatus = $"คัดลอก JSON แชททั้งหมด ({list.Count} รายการ) ลง Clipboard เรียบร้อย!";
            Logger.Info("Chat", $"คัดลอก JSON แชททั้งหมด {list.Count} รายการลง Clipboard");
        }
    }

    [RelayCommand]
    public void CopySelectedChatMessage(LiveChatMessageItem? item)
    {
        var target = item ?? SelectedChatMessage;
        if (target != null)
        {
            Clipboard.SetText(target.FullText);
            LiveChatStatus = "คัดลอกข้อความแชทลง Clipboard แล้ว!";
        }
    }

    [RelayCommand]
    public void CopySelectedChatMessageJson(LiveChatMessageItem? item)
    {
        var target = item ?? SelectedChatMessage;
        if (target != null)
        {
            Clipboard.SetText(target.FormattedJson);
            LiveChatStatus = $"คัดลอก JSON ข้อความ [{target.TypeDisplay}] ลง Clipboard แล้ว!";
        }
    }

    [RelayCommand]
    public void ReplyToChatMessage(LiveChatMessageItem? item)
    {
        var target = item ?? SelectedChatMessage;
        if (target != null)
        {
            string name = !string.IsNullOrWhiteSpace(target.Username) ? $"@{target.Username}" : target.Nickname;
            TestChatMessage = $"@{name} ";
        }
    }

    [RelayCommand]
    public void ToggleLiveChatMonitor()
    {
        IsLiveChatMonitoring = !IsLiveChatMonitoring;
        Logger.Info("Chat", $"เปลี่ยนสถานะการมอนิเตอร์แชทสดเป็น: {(IsLiveChatMonitoring ? "เปิดทำงาน" : "หยุดชั่วคราว")}");
    }

    [RelayCommand]
    public async Task ExportLiveChatAsync()
    {
        if (LiveChatMessages.Count == 0)
        {
            LiveChatStatus = "ไม่มีประวัติแชทสำหรับ Export";
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "บันทึกประวัติแชทสด",
            Filter = "Text Files (*.txt)|*.txt|JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            FileName = $"LiveChat_{Name}_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            DefaultExt = "txt"
        };

        if (dialog.ShowDialog() == true)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== TikTok LIVE Chat Log: {Name} (Port: {CdpPort}) - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            sb.AppendLine($"Summary: {LiveChatCountSummary}");
            sb.AppendLine(new string('-', 70));
            foreach (var msg in LiveChatMessages)
            {
                sb.AppendLine(msg.FullText);
            }
            await File.WriteAllTextAsync(dialog.FileName, sb.ToString(), Encoding.UTF8);
            LiveChatStatus = $"Export แชทสดเรียบร้อย: {Path.GetFileName(dialog.FileName)}";
        }
    }

    [RelayCommand]
    public void SendMockChat()
    {
        var random = new Random();
        string[] sampleUsers = ["somchai_live", "kaew_streamer", "tiktok_fan99", "gamer_pro_th", "lisa_blackpink_fan", "john_doe_999"];
        string[] sampleNicks = ["สมชาย สายเปย์", "น้องแก้ว", "แฟนคลับ TikTok", "เซียนเกมเมอร์", "ลิซ่า FC", "John Thailand"];
        string[] sampleChats = [
            "สวัสดีครับแอดมิน!",
            "ไลฟ์สนุกมากครับ วันนี้มีแจกอะไรไหม",
            "แวะมาทักทายจ้า สู้ๆ นะคะ",
            "วันนี้ภาพคมชัดมากครับ",
            "กดติดตามแล้วนะคะ อย่าลืมฟอลกลับนะ",
            "ขอบคุณสำหรับสาระดีๆ ครับ"
        ];
        string[] sampleGifts = ["Rose (กุหลาบ)", "TikTok (หัวใจ)", "Finger Heart (มินิฮาร์ท)", "Galaxy (กาแล็กซี่)", "Dragon (มังกร)"];
        int[] sampleDiamonds = [1, 5, 10, 1000, 5000];

        int roll = random.Next(0, 10);
        int userIdx = random.Next(0, sampleUsers.Length);

        LiveChatMessageItem mockItem;
        if (roll < 5) // Normal Chat
        {
            mockItem = new LiveChatMessageItem
            {
                Type = LiveChatMessageType.Chat,
                Username = sampleUsers[userIdx],
                Nickname = sampleNicks[userIdx],
                Message = sampleChats[random.Next(0, sampleChats.Length)],
                Timestamp = DateTime.Now
            };
        }
        else if (roll < 8) // Gift
        {
            int giftIdx = random.Next(0, sampleGifts.Length);
            mockItem = new LiveChatMessageItem
            {
                Type = LiveChatMessageType.Gift,
                Username = sampleUsers[userIdx],
                Nickname = sampleNicks[userIdx],
                Message = $"ส่ง {sampleGifts[giftIdx]} x1",
                GiftName = sampleGifts[giftIdx],
                DiamondCount = sampleDiamonds[giftIdx],
                RepeatCount = 1,
                Timestamp = DateTime.Now
            };
        }
        else if (roll == 8) // Like
        {
            mockItem = new LiveChatMessageItem
            {
                Type = LiveChatMessageType.Like,
                Username = sampleUsers[userIdx],
                Nickname = sampleNicks[userIdx],
                Message = "ถูกใจการถ่ายทอดสด",
                Timestamp = DateTime.Now
            };
        }
        else // Follow
        {
            mockItem = new LiveChatMessageItem
            {
                Type = LiveChatMessageType.Follow,
                Username = sampleUsers[userIdx],
                Nickname = sampleNicks[userIdx],
                Message = "เริ่มติดตามผู้จัด",
                Timestamp = DateTime.Now
            };
        }

        // Auto reply check
        if (IsAutoReplyEnabled && _settings.Rules != null && _settings.Rules.Count > 0)
        {
            if (mockItem.Type == LiveChatMessageType.Chat)
            {
                var chatMsg = new LiveChatMessage(mockItem.Username, mockItem.Nickname, mockItem.Message);
                string? replyText = _ruleEngine.EvaluateChatRule(_settings.Rules, chatMsg);
                if (!string.IsNullOrWhiteSpace(replyText))
                {
                    mockItem.IsAutoReplied = true;
                    mockItem.AutoReplyContent = replyText;
                }
            }
            else if (mockItem.Type == LiveChatMessageType.Gift)
            {
                var giftMsg = new LiveGiftMessage(mockItem.Username, mockItem.Nickname, mockItem.GiftName ?? "Gift", mockItem.DiamondCount, mockItem.RepeatCount);
                string? replyText = _ruleEngine.EvaluateGiftRule(_settings.Rules, giftMsg);
                if (!string.IsNullOrWhiteSpace(replyText))
                {
                    mockItem.IsAutoReplied = true;
                    mockItem.AutoReplyContent = replyText;
                }
            }
        }

        RunOnUi(() =>
        {
            LiveChatMessages.Add(mockItem);
            if (mockItem.Type == LiveChatMessageType.Chat || mockItem.Type == LiveChatMessageType.SentMessage)
                TotalChatCount++;
            else if (mockItem.Type == LiveChatMessageType.Gift)
            {
                TotalGiftCount += mockItem.RepeatCount;
                TotalDiamondsCount += mockItem.DiamondCount * mockItem.RepeatCount;
            }
            else if (mockItem.Type == LiveChatMessageType.Like || mockItem.Type == LiveChatMessageType.Follow || mockItem.Type == LiveChatMessageType.Share)
            {
                TotalLikeFollowCount++;
            }
            else if (mockItem.Type == LiveChatMessageType.Other)
            {
                TotalOtherCount++;
            }

            if (mockItem.IsAutoReplied)
                TotalAutoRepliesCount++;

            NotifyLiveChatCounts();
            LiveChatStatus = $"เพิ่มแชทจำลอง ({mockItem.TypeDisplay}) จาก {mockItem.SenderDisplay}";
        });
    }

    [RelayCommand]
    public void SaveStreamerUsername()
    {
        string normalized = NormalizeUsername(StreamerUsername);
        StreamerUsername = normalized;
        Profile.StreamerUsername = normalized;

        try
        {
            var configStorage = new ConfigStorageService();
            configStorage.SaveSettings(_settings);
            ChannelStatusText = string.IsNullOrWhiteSpace(normalized)
                ? $"ล้างการระบุช่องตรวจจับแล้ว ({DateTime.Now:HH:mm:ss})"
                : $"บันทึกช่อง @{normalized} เรียบร้อย ({DateTime.Now:HH:mm:ss})";
            LiveChatStatus = string.IsNullOrWhiteSpace(normalized)
                ? "มอนิเตอร์แชทสดตามแท็บที่เปิดอยู่"
                : $"ตั้งเป้าหมายมอนิเตอร์ช่อง @{normalized} เรียบร้อย";
        }
        catch (Exception ex)
        {
            ChannelStatusText = $"บันทึกไม่สำเร็จ: {ex.Message}";
        }
        OnPropertyChanged(nameof(ActiveChannelDisplay));

        if (IsLiveChatMonitoring)
        {
            _ = StartLiveChatMonitoringAsync();
        }
    }

    [RelayCommand]
    public void UseCurrentAccountAsChannel()
    {
        string? target = !string.IsNullOrWhiteSpace(LoggedInDisplayId)
            ? LoggedInDisplayId
            : (!string.IsNullOrWhiteSpace(LoggedInNickname) ? LoggedInNickname : null);

        if (!string.IsNullOrWhiteSpace(target))
        {
            StreamerUsername = NormalizeUsername(target);
            SaveStreamerUsername();
        }
        else
        {
            ChannelStatusText = "ยังไม่มีข้อมูลโปรไฟล์ที่เข้าสู่ระบบ";
        }
    }

    [RelayCommand]
    public async Task OpenChannelLivePageAsync()
    {
        string target = !string.IsNullOrWhiteSpace(StreamerUsername)
            ? NormalizeUsername(StreamerUsername)
            : NormalizeUsername(LoggedInDisplayId);

        if (string.IsNullOrWhiteSpace(target))
        {
            ChannelStatusText = "กรุณาระบุ Channel Username ก่อนเปิดหน้าไลฟ์";
            return;
        }

        string liveUrl = $"https://www.tiktok.com/@{target}/live";
        ChannelStatusText = $"กำลังเปิดหน้าไลฟ์: {liveUrl}";

        // If instance is running and has CDP, try to navigate via CDP script
        if (Status == InstanceStatus.Running && CdpPort > 0)
        {
            try
            {
                string script = $"window.location.href = '{liveUrl}';";
                await _cdpService.ExecuteScriptAsync(CdpPort, script);
                ChannelStatusText = $"เปิดหน้าไลฟ์ใน TikTok Studio แล้ว: @{target}";
                return;
            }
            catch
            {
                // Fallback to default browser if CDP script navigation fails
            }
        }

        // If account is offline, launch browser with CDP remote debugging port so live chat can be monitored!
        int monitorPort = CdpPort > 0 ? CdpPort + 100 : 9225;
        string[] possibleBrowserPaths = [
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files\Google\Chrome\Application\chrome.exe"
        ];
        string? browserExe = possibleBrowserPaths.FirstOrDefault(File.Exists);
        if (!string.IsNullOrEmpty(browserExe))
        {
            try
            {
                string userDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TikTokStudioSandbox", "LiveMonitorProfile");
                Directory.CreateDirectory(userDataDir);
                Process.Start(new ProcessStartInfo
                {
                    FileName = browserExe,
                    Arguments = $"--remote-debugging-port={monitorPort} --user-data-dir=\"{userDataDir}\" --no-first-run --no-default-browser-check \"{liveUrl}\"",
                    UseShellExecute = false
                });
                ChannelStatusText = $"เปิดหน้าไลฟ์พร้อมตรวจจับ CDP (Port: {monitorPort}): @{target}";
                LiveChatStatus = $"เปิดหน้าไลฟ์ @{target} พร้อมตรวจจับแชทแล้ว (Port: {monitorPort}) - กำลังรอข้อความ...";
                Logger.Info("Chat", $"เปิดหน้าไลฟ์ @{target} พร้อมตรวจจับ CDP (Port: {monitorPort}) ที่ {liveUrl}");
                return;
            }
            catch (Exception ex)
            {
                Logger.Warn("Chat", $"ไม่สามารถเปิดเบราว์เซอร์พร้อม CDP ได้: {ex.Message}");
            }
        }

        // Fallback to system browser
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = liveUrl,
                UseShellExecute = true
            });
            ChannelStatusText = $"เปิดหน้าไลฟ์ในเบราว์เซอร์: @{target}";
            Logger.Info("Chat", $"เปิดหน้าไลฟ์ในเบราว์เซอร์หลัก: {liveUrl}");
        }
        catch (Exception ex)
        {
            ChannelStatusText = $"ไม่สามารถเปิดเบราว์เซอร์ได้: {ex.Message}";
            Logger.Error("Chat", $"เปิดหน้าไลฟ์ในเบราว์เซอร์ล้มเหลว: {ex.Message}", ex);
        }
    }

}
