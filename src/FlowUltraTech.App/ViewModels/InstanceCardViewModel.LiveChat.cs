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
using FlowUltraTech.Core.Interfaces;
using FlowUltraTech.Core.Models;
using FlowUltraTech.Core.Services;

namespace FlowUltraTech.App.ViewModels;

public partial class InstanceCardViewModel : ObservableObject
{
    // ==========================================
    // Live Chat Monitor Properties
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

    public string FilterChatLabel => $"แชท ({TotalChatCount})";
    public string FilterGiftLabel => TotalDiamondsCount > 0 
        ? $"ของขวัญ ({TotalGiftCount} [{TotalDiamondsCount}])" 
        : $"ของขวัญ ({TotalGiftCount})";
    public string FilterLikeFollowLabel => $"ไลก์/ติดตาม ({TotalLikeFollowCount})";
    public string FilterAutoReplyLabel => $"บอทตอบ ({TotalAutoRepliesCount})";
    public string FilterOtherLabel => $"อื่นๆ ({TotalOtherCount})";

    public string[] ChatTypeOptions => ["ALL", "Chat (แชท)", "Gift (ของขวัญ)", "Like/Follow (ถูกใจ/ติดตาม)", "AutoReply (บอทตอบ)", "Other (อื่นๆ / Raw JSON)"];
    public string LiveChatCountSummary => $"ทั้งหมด {LiveChatMessages.Count} รายการ (แชท: {TotalChatCount}, ของขวัญ: {TotalGiftCount}{(TotalDiamondsCount > 0 ? $" [{TotalDiamondsCount}]" : "")}, ไลก์/ติดตาม: {TotalLikeFollowCount}, อื่นๆ: {TotalOtherCount})";
    public string LiveChatMonitorButtonText => IsLiveChatMonitoring ? "หยุดมอนิเตอร์ชั่วคราว" : "เริ่มมอนิเตอร์แชทสด";
    public string LiveChatMonitorBadgeText => IsLiveChatMonitoring
        ? (IsRunning ? "มอนิเตอร์สด (แอปเปิดอยู่)" : "มอนิเตอร์สด (ก่อนเปิดแอป)")
        : "พักการมอนิเตอร์";

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
                ? $"ตรวจจับ: @{target}"
                : "ยังไม่ได้ระบุช่อง";
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
    // Live Chat Monitor Commands & Polling
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

                    if (item.Type == LiveChatMessageType.AutoReply)
                        TotalAutoRepliesCount++;

                    NotifyLiveChatCounts();
                    LiveChatStatus = $"อัปเดตแชทสด @{targetChannel} ({DateTime.Now:HH:mm:ss})";
                });

                ForwardLiveEventToOverlay(item);

                Logger.Info("Chat", $"[{item.TypeDisplay}] {item.SenderDisplay}: {item.Message}", item.RawJson);

                // Auto-reply evaluation (5-mode AutoReply Studio + Global Rules)
                if (IsAutoReplyEnabled && Status == InstanceStatus.Running)
                {
                    _ = EvaluateAndDispatchAutoReplyAsync(item, sendToRoom: true);
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
                                : $"กำลังมอนิเตอร์ @{targetChannel} (กด 'เปิดหน้าไลฟ์' เพื่อเปิดเบราว์เซอร์ตรวจจับ หรือกด 'เริ่มทำงาน')")
                            : $"พร้อมมอนิเตอร์ (ระบุช่องเป้าหมาย หรือกด 'เปิดหน้าไลฟ์')";
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
                if (IsAutoReplyEnabled)
                {
                    await EvaluateAndDispatchAutoReplyAsync(item, sendToRoom: Status == InstanceStatus.Running);
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

                    if (item.Type == LiveChatMessageType.AutoReply)
                        TotalAutoRepliesCount++;

                    ForwardLiveEventToOverlay(item);
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
            "ตัวนี้ราคาเท่าไหร่ กี่บาท",
            "มีส่งฟรีไหม ค่าส่งเท่าไหร่",
            "มีของไหม พร้อมส่งเลยหรือเปล่า",
            "ไลฟ์สนุกมาก วันนี้มีโปรอะไรไหม",
            "สนใจสินค้าในตะกร้า สั่งยังไง",
            "แวะมาทักทาย สู้ๆ นะ"
        ];
        string[] sampleGifts = ["Rose (กุหลาบ)", "TikTok (หัวใจ)", "Finger Heart (มินิฮาร์ท)", "Galaxy (กาแล็กซี่)", "Dragon (มังกร)"];
        int[] sampleDiamonds = [1, 5, 10, 1000, 5000];

        int roll = random.Next(0, 10);
        int userIdx = random.Next(0, sampleUsers.Length);

        LiveChatMessageItem mockItem;
        if (roll < 6) // Normal Chat
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
        else // Follow / Join
        {
            mockItem = new LiveChatMessageItem
            {
                Type = LiveChatMessageType.Follow,
                Username = sampleUsers[userIdx],
                Nickname = sampleNicks[userIdx],
                Message = "เข้าชมและติดตามผู้จัด",
                Timestamp = DateTime.Now
            };
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

            NotifyLiveChatCounts();
            LiveChatStatus = $"เพิ่มแชทจำลอง ({mockItem.TypeDisplay}) จาก {mockItem.SenderDisplay}";
        });

        if (IsAutoReplyEnabled)
        {
            _ = EvaluateAndDispatchAutoReplyAsync(mockItem, sendToRoom: Status == InstanceStatus.Running);
        }

        ForwardLiveEventToOverlay(mockItem);
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

        // Open the channel live URL in an external browser (never navigate inside TikTok Studio itself)

        // If account is offline, launch browser with CDP remote debugging port so live chat can be monitored!
        int monitorPort = CdpPort > 0 ? CdpPort + 100 : 9225;
        string[] possibleBrowserPaths = [
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
        ];
        string? browserExe = possibleBrowserPaths.FirstOrDefault(File.Exists);
        if (!string.IsNullOrEmpty(browserExe))
        {
            try
            {
                string userDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlowUltraTech", "LiveMonitorProfile");
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

    // ==========================================
    // Real-time Clock & Anti-Rebroadcast Live Proof Overlay Studio
    // ==========================================
    public string[] OverlayClockModeOptions =>
    [
        "นาฬิกาแขวนผนังโมเดิร์น (Realistic Wall)",
        "นาฬิกาแขวนคลาสสิก (Seiko Custom)",
        "นาฬิกาตั้งโต๊ะสตูดิโอ 3D (Desk 3D)",
        "นาฬิกาปลุกสองกระดิ่งวินเทจ (Twin Bell)",
        "นาฬิกาดิจิทัลบล็อกไม้ LED (Wood Block)",
        "นาฬิกาดิจิทัลโปร่งใส (Broadcast Clean)"
    ];

    public string[] OverlayBackgroundStyleOptions =>
    [
        "โปร่งใส (Transparent)",
        "กระจกดำเข้ม (Glass Dark)",
        "กระจกสว่าง (Glass Light)"
    ];

    public int OverlayClockModeIndex
    {
        get => (int)Profile.Overlay.ClockMode;
        set
        {
            int clamped = Math.Clamp(value, 0, 5);
            var mode = (OverlayClockMode)clamped;
            if (Profile.Overlay.ClockMode != mode)
            {
                Profile.Overlay.ClockMode = mode;
                OnPropertyChanged();
                SyncOverlayConfig();
            }
        }
    }

    public int OverlayBackgroundStyleIndex
    {
        get => (int)Profile.Overlay.BackgroundStyle;
        set
        {
            int clamped = Math.Clamp(value, 0, 2);
            var style = (OverlayBackgroundStyle)clamped;
            if (Profile.Overlay.BackgroundStyle != style)
            {
                Profile.Overlay.BackgroundStyle = style;
                OnPropertyChanged();
                SyncOverlayConfig();
            }
        }
    }

    public string OverlayBrandText
    {
        get => Profile.Overlay.BrandText;
        set
        {
            string clean = value ?? string.Empty;
            if (Profile.Overlay.BrandText != clean)
            {
                Profile.Overlay.BrandText = clean;
                OnPropertyChanged();
                SyncOverlayConfig();
            }
        }
    }

    public string OverlaySubtitleText
    {
        get => Profile.Overlay.SubtitleText;
        set
        {
            string clean = value ?? string.Empty;
            if (Profile.Overlay.SubtitleText != clean)
            {
                Profile.Overlay.SubtitleText = clean;
                OnPropertyChanged();
                SyncOverlayConfig();
            }
        }
    }

    public string OverlayPrimaryColorHex
    {
        get => Profile.Overlay.PrimaryColorHex;
        set
        {
            string clean = string.IsNullOrWhiteSpace(value) ? "#F8FAFC" : value.Trim();
            if (Profile.Overlay.PrimaryColorHex != clean)
            {
                Profile.Overlay.PrimaryColorHex = clean;
                OnPropertyChanged();
                SyncOverlayConfig();
            }
        }
    }

    public string OverlayAccentColorHex
    {
        get => Profile.Overlay.AccentColorHex;
        set
        {
            string clean = string.IsNullOrWhiteSpace(value) ? "#EF4444" : value.Trim();
            if (Profile.Overlay.AccentColorHex != clean)
            {
                Profile.Overlay.AccentColorHex = clean;
                OnPropertyChanged();
                SyncOverlayConfig();
            }
        }
    }

    public bool OverlayShowSeconds
    {
        get => Profile.Overlay.ShowSeconds;
        set
        {
            if (Profile.Overlay.ShowSeconds != value)
            {
                Profile.Overlay.ShowSeconds = value;
                OnPropertyChanged();
                SyncOverlayConfig();
            }
        }
    }

    public bool OverlayShowThaiDate
    {
        get => Profile.Overlay.ShowThaiDate;
        set
        {
            if (Profile.Overlay.ShowThaiDate != value)
            {
                Profile.Overlay.ShowThaiDate = value;
                OnPropertyChanged();
                SyncOverlayConfig();
            }
        }
    }

    public bool OverlayUse24HourFormat
    {
        get => Profile.Overlay.Use24HourFormat;
        set
        {
            if (Profile.Overlay.Use24HourFormat != value)
            {
                Profile.Overlay.Use24HourFormat = value;
                OnPropertyChanged();
                SyncOverlayConfig();
            }
        }
    }

    public bool OverlayEnableGlare
    {
        get => Profile.Overlay.EnableGlareEffect;
        set
        {
            if (Profile.Overlay.EnableGlareEffect != value)
            {
                Profile.Overlay.EnableGlareEffect = value;
                OnPropertyChanged();
                SyncOverlayConfig();
            }
        }
    }

    public bool OverlayEnableShadow
    {
        get => Profile.Overlay.EnableDynamicShadow;
        set
        {
            if (Profile.Overlay.EnableDynamicShadow != value)
            {
                Profile.Overlay.EnableDynamicShadow = value;
                OnPropertyChanged();
                SyncOverlayConfig();
            }
        }
    }

    public bool OverlayEnableJitter
    {
        get => Profile.Overlay.EnableMicroJitter;
        set
        {
            if (Profile.Overlay.EnableMicroJitter != value)
            {
                Profile.Overlay.EnableMicroJitter = value;
                OnPropertyChanged();
                SyncOverlayConfig();
            }
        }
    }

    public bool OverlayEnableLiveTicker
    {
        get => Profile.Overlay.EnableLiveEventTicker;
        set
        {
            if (Profile.Overlay.EnableLiveEventTicker != value)
            {
                Profile.Overlay.EnableLiveEventTicker = value;
                OnPropertyChanged();
                SyncOverlayConfig();
            }
        }
    }

    public bool OverlayEnableFlashDeal
    {
        get => Profile.Overlay.EnableFlashDealCountdown;
        set
        {
            if (Profile.Overlay.EnableFlashDealCountdown != value)
            {
                Profile.Overlay.EnableFlashDealCountdown = value;
                OnPropertyChanged();
                SyncOverlayConfig();
            }
        }
    }

    public string OverlayFlashDealTitle
    {
        get => Profile.Overlay.FlashDealTitle;
        set
        {
            string clean = value ?? string.Empty;
            if (Profile.Overlay.FlashDealTitle != clean)
            {
                Profile.Overlay.FlashDealTitle = clean;
                OnPropertyChanged();
                SyncOverlayConfig();
            }
        }
    }

    public int OverlayFlashDealDurationSeconds
    {
        get => Profile.Overlay.FlashDealDurationSeconds;
        set
        {
            int clamped = Math.Clamp(value <= 0 ? 300 : value, 5, 86400);
            if (Profile.Overlay.FlashDealDurationSeconds != clamped)
            {
                Profile.Overlay.FlashDealDurationSeconds = clamped;
                OnPropertyChanged();
                SyncOverlayConfig();
            }
        }
    }

    public string OverlayEndpointUrl =>
        _overlayService != null && _overlayService.ActivePort > 0
            ? _overlayService.GetOverlayUrl(Profile.Id)
            : $"http://127.0.0.1:{(_settings.OverlayServerPort > 0 ? _settings.OverlayServerPort : 19450)}/overlay/{Profile.Id:D}";

    public void NotifyOverlayEndpointChanged() => OnPropertyChanged(nameof(OverlayEndpointUrl));

    [ObservableProperty]
    private string _overlayStatusText = "พร้อมซิงก์เวลาจริง";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInjectOverlay))]
    [NotifyPropertyChangedFor(nameof(InjectOverlayButtonText))]
    private bool _isInjectingOverlay;

    public bool CanInjectOverlay => !IsInjectingOverlay;
    public string InjectOverlayButtonText => IsInjectingOverlay ? "กำลังส่งเข้าฉาก..." : "ส่งเข้า TikTok Studio";

    private void SyncOverlayConfig()
    {
        if (_overlayService != null)
        {
            _ = _overlayService.UpdateConfigAsync(Profile.Id, Profile.Overlay);
        }

        _onStateChanged?.Invoke();
        OverlayStatusText = $"อัปเดต Overlay เรียลไทม์ ({DateTime.Now:HH:mm:ss})";
    }

    private void OnExternalLiveEventReceived(string channel, LiveChatMessageItem item)
    {
        if (item == null) return;

        string targetChannel = !string.IsNullOrWhiteSpace(StreamerUsername)
            ? NormalizeUsername(StreamerUsername)
            : NormalizeUsername(LoggedInDisplayId);

        if (!string.IsNullOrWhiteSpace(targetChannel) &&
            !string.IsNullOrWhiteSpace(channel) &&
            !string.Equals(targetChannel, NormalizeUsername(channel), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ForwardLiveEventToOverlay(item);
    }

    internal void ForwardLiveEventToOverlay(LiveChatMessageItem? item)
    {
        if (item == null || _overlayService == null || !Profile.Overlay.EnableLiveEventTicker)
        {
            return;
        }

        string eventType = item.Type switch
        {
            LiveChatMessageType.Gift => "gift",
            LiveChatMessageType.Like => "like",
            LiveChatMessageType.Follow => "follow",
            LiveChatMessageType.Share => "follow",
            LiveChatMessageType.Other => "join",
            _ => "chat"
        };

        var payload = new OverlayLiveEventPayload
        {
            EventType = eventType,
            Nickname = !string.IsNullOrWhiteSpace(item.Nickname) ? item.Nickname : (item.Username ?? "ผู้ชม"),
            Username = item.Username ?? string.Empty,
            Message = !string.IsNullOrWhiteSpace(item.Message) ? item.Message : (item.GiftName ?? string.Empty),
            BadgeText = item.TypeDisplay,
            Timestamp = item.Timestamp == default ? DateTime.Now : item.Timestamp
        };

        _ = _overlayService.BroadcastLiveEventAsync(Profile.Id, payload);
    }

    [RelayCommand]
    public void OpenOverlayTab()
    {
        IsDetailOpen = true;
        SelectedDetailTabIndex = 7;
        OnPropertyChanged(nameof(IsDetailExpanded));
        OnPropertyChanged(nameof(DetailButtonText));
        OnPropertyChanged(nameof(DetailVisibility));
        OnPropertyChanged(nameof(OverlayEndpointUrl));
    }

    [RelayCommand]
    public void CopyOverlayUrl()
    {
        try
        {
            Clipboard.SetText(OverlayEndpointUrl);
        }
        catch { }

        OverlayStatusText = "คัดลอกลิงก์ Overlay แล้ว";
    }

    [RelayCommand]
    public async Task InjectOverlayAsync()
    {
        if (IsInjectingOverlay)
        {
            return;
        }

        IsInjectingOverlay = true;
        OverlayStatusText = "กำลังส่งเข้าฉาก...";
        try
        {
            var result = await _cdpService.InjectOverlaySourceAsync(
                CdpPort,
                OverlayEndpointUrl,
                msg => RunOnUi(() => OverlayStatusText = msg));

            OverlayStatusText = result.Message;
            if (!result.Success)
            {
                try
                {
                    Clipboard.SetText(OverlayEndpointUrl);
                }
                catch { }
            }
        }
        finally
        {
            IsInjectingOverlay = false;
        }
    }

    [RelayCommand]
    public async Task TriggerFlashDealAsync()
    {
        int duration = OverlayFlashDealDurationSeconds > 0 ? OverlayFlashDealDurationSeconds : 300;
        Profile.Overlay.EnableFlashDealCountdown = true;
        Profile.Overlay.FlashDealDurationSeconds = duration;
        Profile.Overlay.FlashDealEndsAtUtc = DateTime.UtcNow.AddSeconds(duration);
        OnPropertyChanged(nameof(OverlayEnableFlashDeal));

        if (_overlayService != null)
        {
            await _overlayService.TriggerFlashDealAsync(Profile.Id, OverlayFlashDealTitle, duration);
        }

        _onStateChanged?.Invoke();
        OverlayStatusText = $"เริ่มนาทีทอง ({duration} วินาที)";
    }

    [RelayCommand]
    public async Task StopFlashDealAsync()
    {
        Profile.Overlay.EnableFlashDealCountdown = false;
        Profile.Overlay.FlashDealEndsAtUtc = null;
        OnPropertyChanged(nameof(OverlayEnableFlashDeal));

        if (_overlayService != null)
        {
            await _overlayService.StopFlashDealAsync(Profile.Id);
        }

        _onStateChanged?.Invoke();
        OverlayStatusText = "หยุดนาทีทองแล้ว";
    }

    [RelayCommand]
    public async Task SendTestOverlayEventAsync()
    {
        var payload = new OverlayLiveEventPayload
        {
            EventType = "gift",
            Nickname = !string.IsNullOrWhiteSpace(LoggedInNickname) ? LoggedInNickname : "คุณลูกค้า VIP",
            Username = !string.IsNullOrWhiteSpace(LoggedInDisplayId) ? LoggedInDisplayId : "vip_viewer",
            Message = "ส่งของขวัญ กุหลาบ x9 • ยืนยันเวลาจริง",
            BadgeText = "ของขวัญ",
            Timestamp = DateTime.Now
        };

        if (_overlayService != null)
        {
            await _overlayService.BroadcastLiveEventAsync(Profile.Id, payload);
        }

        OverlayStatusText = "ส่งอีเวนต์ทดสอบเข้าป้ายสดแล้ว";
    }

    // ==========================================
    // Per-Instance Native Proxy System (No Proxifier Required)
    // ==========================================
    public string[] ProxyProtocolOptions => ["SOCKS5", "HTTPS", "HTTP"];

    public bool InstanceUseCustomProxy
    {
        get => Profile.UseCustomProxy;
        set
        {
            if (Profile.UseCustomProxy != value)
            {
                Profile.UseCustomProxy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProxySummaryBadgeText));
                ApplyAndSaveInstanceProxy();
            }
        }
    }

    public int InstanceProxyProtocolIndex
    {
        get => (int)Profile.ProxyProtocol;
        set
        {
            int clamped = Math.Clamp(value, 0, 2);
            var proto = (ProxyProtocol)clamped;
            if (Profile.ProxyProtocol != proto)
            {
                Profile.ProxyProtocol = proto;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProxySummaryBadgeText));
            }
        }
    }

    public string InstanceProxyHost
    {
        get => Profile.ProxyHost;
        set
        {
            string clean = value?.Trim() ?? string.Empty;
            if (Profile.ProxyHost != clean)
            {
                Profile.ProxyHost = clean;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProxySummaryBadgeText));
            }
        }
    }

    public int InstanceProxyPort
    {
        get => Profile.ProxyPort;
        set
        {
            int clamped = Math.Clamp(value <= 0 ? 8080 : value, 1, 65535);
            if (Profile.ProxyPort != clamped)
            {
                Profile.ProxyPort = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProxySummaryBadgeText));
            }
        }
    }

    public string InstanceProxyUsername
    {
        get => Profile.ProxyUsername;
        set
        {
            string clean = value?.Trim() ?? string.Empty;
            if (Profile.ProxyUsername != clean)
            {
                Profile.ProxyUsername = clean;
                OnPropertyChanged();
            }
        }
    }

    public string InstanceProxyPassword
    {
        get => Profile.ProxyPassword;
        set
        {
            string clean = value ?? string.Empty;
            if (Profile.ProxyPassword != clean)
            {
                Profile.ProxyPassword = clean;
                OnPropertyChanged();
            }
        }
    }

    public string ProxySummaryBadgeText =>
        Profile.UseCustomProxy && !string.IsNullOrWhiteSpace(Profile.ProxyHost)
            ? $"Proxy: {Profile.ProxyProtocol.ToString().ToUpperInvariant()} {Profile.ProxyHost}:{Profile.ProxyPort}"
            : "Proxy: เชื่อมต่อตรง (Direct)";

    [ObservableProperty]
    private string _instanceProxyStatusText = "โหมด Native Proxy พร้อมทำงาน (ไม่ต้องใช้ Proxifier)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TestInstanceProxyButtonText))]
    private bool _isTestingInstanceProxy;

    public string TestInstanceProxyButtonText => IsTestingInstanceProxy ? "กำลังเช็ก IP..." : "ทดสอบ Proxy";

    private void ApplyAndSaveInstanceProxy()
    {
        var effective = LocalProxyBridgeService.ResolveEffectiveProxy(Profile, _settings);
        if (effective != null)
        {
            int localPort = LocalProxyBridgeService.Shared.EnsureBridgeForInstance(Profile.Id, effective);
            InstanceProxyStatusText = $"เปิดใช้งาน {effective.Protocol}://{effective.Host}:{effective.Port} (Bridge 127.0.0.1:{localPort})";
        }
        else
        {
            LocalProxyBridgeService.Shared.StopBridgeForInstance(Profile.Id);
            InstanceProxyStatusText = "ปิด Proxy สำหรับบอทนี้แล้ว (เชื่อมต่อตรง)";
        }

        _onStateChanged?.Invoke();
    }

    [RelayCommand]
    public void SaveInstanceProxyConfig()
    {
        ApplyAndSaveInstanceProxy();
        OnPropertyChanged(nameof(ProxySummaryBadgeText));
    }

    [RelayCommand]
    public async Task TestInstanceProxyAsync()
    {
        if (IsTestingInstanceProxy) return;
        if (string.IsNullOrWhiteSpace(Profile.ProxyHost) || Profile.ProxyPort <= 0)
        {
            InstanceProxyStatusText = "กรุณาระบุ Host / IP และ Port ก่อนกดทดสอบ";
            return;
        }

        IsTestingInstanceProxy = true;
        InstanceProxyStatusText = $"กำลังทดสอบ {Profile.ProxyProtocol}://{Profile.ProxyHost}:{Profile.ProxyPort}...";
        try
        {
            var item = new ProxyItem
            {
                Id = Profile.Id,
                Name = Profile.Name,
                Protocol = Profile.ProxyProtocol,
                Host = Profile.ProxyHost.Trim(),
                Port = Profile.ProxyPort,
                Username = Profile.ProxyUsername?.Trim() ?? string.Empty,
                Password = Profile.ProxyPassword ?? string.Empty,
                IsActive = true
            };

            var res = await LocalProxyBridgeService.Shared.TestProxyAsync(item);
            InstanceProxyStatusText = res.Message;
            Logger.Info("Proxy", $"ผลการทดสอบ Proxy ({item.Protocol}://{item.Host}:{item.Port}): {res.Message}");
        }
        finally
        {
            IsTestingInstanceProxy = false;
        }
    }

    // ==========================================
    // 5-Mode Auto-Reply & Live Comment Studio
    // ==========================================
    private System.Windows.Threading.DispatcherTimer? _autoPostLoopTimer;
    private int _loopCountdownRemaining;
    private int _aiPeriodicCountdownRemaining;

    [ObservableProperty]
    private int _selectedAutoReplySubTabIndex = 0;

    partial void OnTestChatMessageChanged(string value)
    {
        OnPropertyChanged(nameof(TestChatMessageLength));
        OnPropertyChanged(nameof(TestChatMessageCounter));
    }

    public int TestChatMessageLength => TestChatMessage?.Length ?? 0;
    public string TestChatMessageCounter => $"{TestChatMessageLength}/200";

    // Mode 2: วนส่ง (Periodic Auto-Post Loop)
    public bool AutoReplyEnableLoopPost
    {
        get => Profile.AutoReply.EnableLoopPost;
        set
        {
            if (Profile.AutoReply.EnableLoopPost != value)
            {
                Profile.AutoReply.EnableLoopPost = value;
                OnPropertyChanged();
                EnsureAutoPostLoopTimer();
                _onStateChanged?.Invoke();
            }
        }
    }

    public string AutoReplyLoopMessagesText
    {
        get => Profile.AutoReply.LoopMessagesText;
        set
        {
            string clean = value ?? string.Empty;
            if (Profile.AutoReply.LoopMessagesText != clean)
            {
                Profile.AutoReply.LoopMessagesText = clean;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AutoReplyLoopMessagesCountText));
            }
        }
    }

    public int AutoReplyLoopIntervalSeconds
    {
        get => Profile.AutoReply.LoopIntervalSeconds;
        set
        {
            int clamped = Math.Clamp(value <= 0 ? 60 : value, 5, 3600);
            if (Profile.AutoReply.LoopIntervalSeconds != clamped)
            {
                Profile.AutoReply.LoopIntervalSeconds = clamped;
                _loopCountdownRemaining = clamped;
                OnPropertyChanged();
            }
        }
    }

    public bool AutoReplyLoopRandomOrder
    {
        get => Profile.AutoReply.LoopRandomOrder;
        set
        {
            if (Profile.AutoReply.LoopRandomOrder != value)
            {
                Profile.AutoReply.LoopRandomOrder = value;
                OnPropertyChanged();
            }
        }
    }

    public string AutoReplyLoopMessagesCountText =>
        $"มี {AutoReplyEngineService.ParseLines(Profile.AutoReply.LoopMessagesText).Count} ข้อความในรายการวนส่ง";

    [ObservableProperty]
    private string _autoReplyLoopStatusText = "ปิดการวนส่งอัตโนมัติ";

    // Mode 3: กฎคำ (Shorthand Keyword Rules + Anti-Spam / Anti-Ban)
    public bool AutoReplyEnableKeywordRules
    {
        get => Profile.AutoReply.EnableKeywordRules;
        set
        {
            if (Profile.AutoReply.EnableKeywordRules != value)
            {
                Profile.AutoReply.EnableKeywordRules = value;
                OnPropertyChanged();
                _onStateChanged?.Invoke();
            }
        }
    }

    public string AutoReplyKeywordRulesText
    {
        get => Profile.AutoReply.KeywordRulesText;
        set
        {
            string clean = value ?? string.Empty;
            if (Profile.AutoReply.KeywordRulesText != clean)
            {
                Profile.AutoReply.KeywordRulesText = clean;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ParsedKeywordRulesSummary));
            }
        }
    }

    public int AutoReplyRuleMinIntervalSeconds
    {
        get => Profile.AutoReply.RuleMinIntervalSeconds;
        set
        {
            int clamped = Math.Clamp(value, 1, 300);
            if (Profile.AutoReply.RuleMinIntervalSeconds != clamped)
            {
                Profile.AutoReply.RuleMinIntervalSeconds = clamped;
                OnPropertyChanged();
            }
        }
    }

    public int AutoReplyRuleUserCooldownSeconds
    {
        get => Profile.AutoReply.RuleUserCooldownSeconds;
        set
        {
            int clamped = Math.Clamp(value, 0, 3600);
            if (Profile.AutoReply.RuleUserCooldownSeconds != clamped)
            {
                Profile.AutoReply.RuleUserCooldownSeconds = clamped;
                OnPropertyChanged();
            }
        }
    }

    public int AutoReplyRuleMaxRepliesPerMinute
    {
        get => Profile.AutoReply.RuleMaxRepliesPerMinute;
        set
        {
            int clamped = Math.Clamp(value, 1, 120);
            if (Profile.AutoReply.RuleMaxRepliesPerMinute != clamped)
            {
                Profile.AutoReply.RuleMaxRepliesPerMinute = clamped;
                OnPropertyChanged();
            }
        }
    }

    public bool AutoReplyRulePrefixWithMention
    {
        get => Profile.AutoReply.RulePrefixWithMention;
        set
        {
            if (Profile.AutoReply.RulePrefixWithMention != value)
            {
                Profile.AutoReply.RulePrefixWithMention = value;
                OnPropertyChanged();
            }
        }
    }

    public string ParsedKeywordRulesSummary =>
        $"พร้อมใช้งาน {AutoReplyEngineService.ParseKeywordRules(Profile.AutoReply.KeywordRulesText).Count} กฎคำตอบ";

    // Mode 4: ทักทาย (Viewer Join Greeting)
    public string[] GreetingModeOptions => ["สุ่มข้อความจากรายการ", "ให้ AI สร้างคำทักทาย"];
    public string[] GreetingFrequencyOptions => ["ทักครั้งเดียว/คน ตลอดไลฟ์", "ทักทุกครั้งที่เข้าห้อง"];

    public bool AutoReplyEnableJoinGreeting
    {
        get => Profile.AutoReply.EnableJoinGreeting;
        set
        {
            if (Profile.AutoReply.EnableJoinGreeting != value)
            {
                Profile.AutoReply.EnableJoinGreeting = value;
                OnPropertyChanged();
                _onStateChanged?.Invoke();
            }
        }
    }

    public string AutoReplyJoinGreetingMessagesText
    {
        get => Profile.AutoReply.JoinGreetingMessagesText;
        set
        {
            string clean = value ?? string.Empty;
            if (Profile.AutoReply.JoinGreetingMessagesText != clean)
            {
                Profile.AutoReply.JoinGreetingMessagesText = clean;
                OnPropertyChanged();
            }
        }
    }

    public int AutoReplyGreetingModeIndex
    {
        get => (int)Profile.AutoReply.GreetingMode;
        set
        {
            var mode = (JoinGreetingMode)Math.Clamp(value, 0, 1);
            if (Profile.AutoReply.GreetingMode != mode)
            {
                Profile.AutoReply.GreetingMode = mode;
                OnPropertyChanged();
            }
        }
    }

    public int AutoReplyGreetingFrequencyIndex
    {
        get => (int)Profile.AutoReply.GreetingFrequency;
        set
        {
            var freq = (JoinGreetingFrequency)Math.Clamp(value, 0, 1);
            if (Profile.AutoReply.GreetingFrequency != freq)
            {
                Profile.AutoReply.GreetingFrequency = freq;
                OnPropertyChanged();
            }
        }
    }

    public int AutoReplyGreetingMinIntervalSeconds
    {
        get => Profile.AutoReply.GreetingMinIntervalSeconds;
        set
        {
            int clamped = Math.Clamp(value, 2, 300);
            if (Profile.AutoReply.GreetingMinIntervalSeconds != clamped)
            {
                Profile.AutoReply.GreetingMinIntervalSeconds = clamped;
                OnPropertyChanged();
            }
        }
    }

    // Mode 5: AI ตอบ (OpenRouter / OpenAI Integration)
    public string[] AiOperatingModeOptions => ["ตอบคอมเมนต์ผู้ชม", "โพสต์เองเป็นระยะ"];
    public string[] AiFilterModeOptions => ["ทุกคอมเมนต์", "เฉพาะคำถาม / สนใจสินค้า"];

    public bool AutoReplyEnableAi
    {
        get => Profile.AutoReply.EnableAiReply;
        set
        {
            if (Profile.AutoReply.EnableAiReply != value)
            {
                Profile.AutoReply.EnableAiReply = value;
                OnPropertyChanged();
                EnsureAutoPostLoopTimer();
                _onStateChanged?.Invoke();
            }
        }
    }

    public string AutoReplyAiApiKey
    {
        get => Profile.AutoReply.AiApiKey;
        set
        {
            string clean = value?.Trim() ?? string.Empty;
            if (Profile.AutoReply.AiApiKey != clean)
            {
                Profile.AutoReply.AiApiKey = clean;
                OnPropertyChanged();
            }
        }
    }

    public string AutoReplyAiModel
    {
        get => Profile.AutoReply.AiModel;
        set
        {
            string clean = string.IsNullOrWhiteSpace(value) ? "openai/gpt-4o-mini" : value.Trim();
            if (Profile.AutoReply.AiModel != clean)
            {
                Profile.AutoReply.AiModel = clean;
                OnPropertyChanged();
            }
        }
    }

    public string AutoReplyAiSystemPrompt
    {
        get => Profile.AutoReply.AiSystemPrompt;
        set
        {
            string clean = value ?? string.Empty;
            if (Profile.AutoReply.AiSystemPrompt != clean)
            {
                Profile.AutoReply.AiSystemPrompt = clean;
                OnPropertyChanged();
            }
        }
    }

    public int AutoReplyAiModeIndex
    {
        get => (int)Profile.AutoReply.AiMode;
        set
        {
            var mode = (AiOperatingMode)Math.Clamp(value, 0, 1);
            if (Profile.AutoReply.AiMode != mode)
            {
                Profile.AutoReply.AiMode = mode;
                OnPropertyChanged();
                EnsureAutoPostLoopTimer();
            }
        }
    }

    public int AutoReplyAiFilterModeIndex
    {
        get => (int)Profile.AutoReply.AiFilterMode;
        set
        {
            var filter = (AiReplyFilterMode)Math.Clamp(value, 0, 1);
            if (Profile.AutoReply.AiFilterMode != filter)
            {
                Profile.AutoReply.AiFilterMode = filter;
                OnPropertyChanged();
            }
        }
    }

    public int AutoReplyAiMinWords
    {
        get => Profile.AutoReply.AiMinWords;
        set
        {
            int clamped = Math.Clamp(value, 0, 100);
            if (Profile.AutoReply.AiMinWords != clamped)
            {
                Profile.AutoReply.AiMinWords = clamped;
                OnPropertyChanged();
            }
        }
    }

    public int AutoReplyAiMaxWords
    {
        get => Profile.AutoReply.AiMaxWords;
        set
        {
            int clamped = Math.Clamp(value <= 0 ? 30 : value, 5, 150);
            if (Profile.AutoReply.AiMaxWords != clamped)
            {
                Profile.AutoReply.AiMaxWords = clamped;
                OnPropertyChanged();
            }
        }
    }

    public int AutoReplyAiUserCooldownSeconds
    {
        get => Profile.AutoReply.AiUserCooldownSeconds;
        set
        {
            int clamped = Math.Clamp(value, 0, 3600);
            if (Profile.AutoReply.AiUserCooldownSeconds != clamped)
            {
                Profile.AutoReply.AiUserCooldownSeconds = clamped;
                OnPropertyChanged();
            }
        }
    }

    public string AutoReplyAiForbiddenWords
    {
        get => Profile.AutoReply.AiForbiddenWords;
        set
        {
            string clean = value ?? string.Empty;
            if (Profile.AutoReply.AiForbiddenWords != clean)
            {
                Profile.AutoReply.AiForbiddenWords = clean;
                OnPropertyChanged();
            }
        }
    }

    [ObservableProperty]
    private string _autoReplyStudioStatusText = "พร้อมทำงาน (ตั้งค่าและกดบันทึกได้ทันที)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TestAiReplyButtonText))]
    private bool _isTestingAiReply;

    public string TestAiReplyButtonText => IsTestingAiReply ? "กำลังทดสอบ AI..." : "ทดสอบ AI ตอบ";

    [RelayCommand]
    public void SaveAutoReplyStudioConfig()
    {
        _onStateChanged?.Invoke();
        OnPropertyChanged(nameof(ParsedKeywordRulesSummary));
        OnPropertyChanged(nameof(AutoReplyLoopMessagesCountText));
        EnsureAutoPostLoopTimer();
        AutoReplyStudioStatusText = $"บันทึกการตั้งค่าระบบตอบกลับอัตโนมัติแล้ว ({DateTime.Now:HH:mm:ss})";
    }

    [RelayCommand]
    public async Task TriggerLoopPostNowAsync()
    {
        string? nextMsg = AutoReplyEngineService.Shared.GetNextLoopMessage(Profile.Id, Profile.AutoReply);
        if (string.IsNullOrWhiteSpace(nextMsg))
        {
            var lines = AutoReplyEngineService.ParseLines(Profile.AutoReply.LoopMessagesText);
            nextMsg = lines.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(nextMsg))
        {
            AutoReplyLoopStatusText = "ไม่มีข้อความในรายการวนส่ง";
            return;
        }

        await DispatchBotMessageToLiveRoomAsync(nextMsg, "วนส่ง");
        AutoReplyLoopStatusText = $"ส่งข้อความวนส่งแล้ว ({DateTime.Now:HH:mm:ss})";
    }

    [RelayCommand]
    public async Task TestAiReplyAsync()
    {
        if (IsTestingAiReply) return;
        IsTestingAiReply = true;
        AutoReplyStudioStatusText = "กำลังส่งคำถามทดสอบไปยัง OpenRouter / AI...";
        try
        {
            var (ok, msg, _) = await AutoReplyEngineService.Shared.TestAiReplyAsync(Profile.AutoReply);
            AutoReplyStudioStatusText = msg;
        }
        finally
        {
            IsTestingAiReply = false;
        }
    }

    private void EnsureAutoPostLoopTimer()
    {
        bool needTimer = Profile.AutoReply.EnableLoopPost ||
                         (Profile.AutoReply.EnableAiReply && Profile.AutoReply.AiMode == AiOperatingMode.PeriodicPost);

        if (!needTimer)
        {
            _autoPostLoopTimer?.Stop();
            AutoReplyLoopStatusText = "ปิดการวนส่งอัตโนมัติ";
            return;
        }

        if (_autoPostLoopTimer == null)
        {
            try
            {
                _autoPostLoopTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _autoPostLoopTimer.Tick += async (_, _) => await OnAutoPostLoopTickAsync();
            }
            catch { }
        }

        if (_loopCountdownRemaining <= 0)
            _loopCountdownRemaining = Math.Max(5, Profile.AutoReply.LoopIntervalSeconds);
        if (_aiPeriodicCountdownRemaining <= 0)
            _aiPeriodicCountdownRemaining = Math.Max(15, Profile.AutoReply.AiPeriodicIntervalSeconds);

        _autoPostLoopTimer?.Start();
        AutoReplyLoopStatusText = $"ทำงานอยู่ • ส่งรอบถัดไปใน {_loopCountdownRemaining} วินาที";
    }

    private async Task OnAutoPostLoopTickAsync()
    {
        if (Profile.AutoReply.EnableLoopPost)
        {
            _loopCountdownRemaining--;
            if (_loopCountdownRemaining <= 0)
            {
                _loopCountdownRemaining = Math.Max(5, Profile.AutoReply.LoopIntervalSeconds);
                if (IsAutoReplyEnabled && Status == InstanceStatus.Running)
                {
                    string? msg = AutoReplyEngineService.Shared.GetNextLoopMessage(Profile.Id, Profile.AutoReply);
                    if (!string.IsNullOrWhiteSpace(msg))
                    {
                        await DispatchBotMessageToLiveRoomAsync(msg, "วนส่ง");
                    }
                }
            }
            AutoReplyLoopStatusText = Status == InstanceStatus.Running
                ? $"กำลังวนส่ง • รอบถัดไปใน {_loopCountdownRemaining} วินาที"
                : $"เปิดรอไว้ • จะเริ่มส่งเมื่อเปิดแอป ({_loopCountdownRemaining}s)";
        }

        if (Profile.AutoReply.EnableAiReply && Profile.AutoReply.AiMode == AiOperatingMode.PeriodicPost)
        {
            _aiPeriodicCountdownRemaining--;
            if (_aiPeriodicCountdownRemaining <= 0)
            {
                _aiPeriodicCountdownRemaining = Math.Max(15, Profile.AutoReply.AiPeriodicIntervalSeconds);
                if (IsAutoReplyEnabled && Status == InstanceStatus.Running)
                {
                    string? aiPost = await AutoReplyEngineService.Shared.GeneratePeriodicAiPostAsync(Profile.AutoReply);
                    if (!string.IsNullOrWhiteSpace(aiPost))
                    {
                        await DispatchBotMessageToLiveRoomAsync(aiPost, "AI โพสต์");
                    }
                }
            }
        }
    }

    private async Task DispatchBotMessageToLiveRoomAsync(string messageText, string sourceTag)
    {
        if (string.IsNullOrWhiteSpace(messageText)) return;

        bool sent = false;
        if (Status == InstanceStatus.Running)
        {
            sent = await _orchestrator.SendChatAsync(Profile, messageText);
        }

        RunOnUi(() =>
        {
            var botItem = new LiveChatMessageItem
            {
                Type = LiveChatMessageType.AutoReply,
                Username = LoggedInDisplayId ?? "Bot",
                Nickname = $"บอท ({sourceTag})",
                Message = messageText,
                IsAutoReplied = true,
                AutoReplyContent = messageText,
                Timestamp = DateTime.Now
            };
            if (LiveChatMessages.Count > 1500) LiveChatMessages.RemoveAt(0);
            LiveChatMessages.Add(botItem);
            TotalAutoRepliesCount++;
            NotifyLiveChatCounts();
            LiveChatStatus = sent
                ? $"[{sourceTag}] ส่งข้อความสำเร็จ ({DateTime.Now:HH:mm:ss})"
                : $"[{sourceTag}] จำลองข้อความแล้ว ({DateTime.Now:HH:mm:ss})";
        });
    }

    private async Task EvaluateAndDispatchAutoReplyAsync(LiveChatMessageItem item, bool sendToRoom)
    {
        if (item == null || !IsAutoReplyEnabled) return;

        try
        {
            string? replyText = null;
            string sourceTag = "บอทตอบ";

            if (item.Type == LiveChatMessageType.Chat && !string.IsNullOrWhiteSpace(item.Message))
            {
                if (_settings.Rules != null && _settings.Rules.Count > 0)
                {
                    var chatMsg = new LiveChatMessage(item.Username ?? string.Empty, item.Nickname ?? string.Empty, item.Message);
                    replyText = _ruleEngine.EvaluateChatRule(_settings.Rules, chatMsg);
                    sourceTag = "กฎหลัก";
                }

                if (string.IsNullOrWhiteSpace(replyText))
                {
                    var evalResult = await AutoReplyEngineService.Shared.EvaluateIncomingCommentAsync(
                        Profile.Id,
                        Profile.AutoReply,
                        item.Username ?? string.Empty,
                        item.Nickname ?? string.Empty,
                        item.Message);

                    if (evalResult.HasValue && !string.IsNullOrWhiteSpace(evalResult.Value.ReplyText))
                    {
                        replyText = evalResult.Value.ReplyText;
                        sourceTag = evalResult.Value.SourceTag;
                    }
                }
            }
            else if (item.Type == LiveChatMessageType.Follow || item.Type == LiveChatMessageType.Other)
            {
                if (_settings.Rules != null && _settings.Rules.Count > 0)
                {
                    var joinMsg = new LiveJoinMessage(item.Username ?? string.Empty, item.Nickname ?? string.Empty);
                    replyText = _ruleEngine.EvaluateJoinRule(_settings.Rules, joinMsg);
                    sourceTag = "ทักทาย";
                }

                if (string.IsNullOrWhiteSpace(replyText))
                {
                    replyText = await AutoReplyEngineService.Shared.EvaluateViewerJoinAsync(
                        Profile.Id,
                        Profile.AutoReply,
                        item.Username ?? string.Empty,
                        item.Nickname ?? string.Empty);
                    sourceTag = "ทักทาย";
                }
            }
            else if (item.Type == LiveChatMessageType.Gift && _settings.Rules != null && _settings.Rules.Count > 0)
            {
                var giftMsg = new LiveGiftMessage(item.Username, item.Nickname, item.GiftName ?? "Gift", item.DiamondCount, item.RepeatCount);
                replyText = _ruleEngine.EvaluateGiftRule(_settings.Rules, giftMsg);
                sourceTag = "ขอบคุณของขวัญ";
            }
            else if (item.Type == LiveChatMessageType.Like && _settings.Rules != null && _settings.Rules.Count > 0)
            {
                var likeMsg = new LiveLikeMessage(item.Username, item.Nickname, item.RepeatCount, TotalLikeFollowCount);
                replyText = _ruleEngine.EvaluateLikeRule(_settings.Rules, likeMsg);
                sourceTag = "ขอบคุณไลก์";
            }

            if (!string.IsNullOrWhiteSpace(replyText))
            {
                RunOnUi(() =>
                {
                    item.IsAutoReplied = true;
                    item.AutoReplyContent = replyText;
                    TotalAutoRepliesCount++;
                    NotifyLiveChatCounts();
                    FilteredLiveChatMessages?.Refresh();
                });

                if (sendToRoom)
                {
                    await _orchestrator.SendChatAsync(Profile, replyText);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("AutoReply", $"ประมวลผลตอบกลับอัตโนมัติขัดข้อง: {ex.Message}");
        }
    }
}
