using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace FlowUltraTech.Core.Models;

public static class JsonFormatter
{
    public static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string TryFormatJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        string trimmed = raw.Trim();
        if (!((trimmed.StartsWith('{') && trimmed.EndsWith('}')) || 
              (trimmed.StartsWith('[') && trimmed.EndsWith(']'))))
        {
            return raw;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            return JsonSerializer.Serialize(doc.RootElement, IndentedOptions);
        }
        catch
        {
            return raw;
        }
    }
}

public class CookieItem
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Path { get; set; } = "/";
    public string Expires { get; set; } = string.Empty;
    public bool HttpOnly { get; set; }
    public bool Secure { get; set; }
    public bool IsAuthToken { get; set; }
}

public class StorageDumpResult
{
    public List<CookieItem> Cookies { get; set; } = new();
    public Dictionary<string, string> LocalStorage { get; set; } = new();
    public Dictionary<string, string> SessionStorage { get; set; } = new();
    public string? SessionIdToken { get; set; }
    public string? CsrfToken { get; set; }
    public string? MsToken { get; set; }
    public DateTime FetchedAt { get; set; } = DateTime.Now;
}

public class StorageEntry
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class CaptchaElementItem
{
    public string TagName { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string Html { get; set; } = string.Empty;
    public string FrameUrl { get; set; } = string.Empty;
    public int Depth { get; set; }
    public bool IsVisible { get; set; } = true;
    public double Width { get; set; }
    public double Height { get; set; }
    public double X { get; set; }
    public double Y { get; set; }

    public string Summary => $"<{TagName}" + 
        (!string.IsNullOrEmpty(Id) ? $" id=\"{Id}\"" : "") + 
        (!string.IsNullOrEmpty(ClassName) ? $" class=\"{ClassName}\"" : "") + ">" +
        (!IsVisible ? " [Hidden/Inactive]" : "");
}

public class CaptchaDetectionResult
{
    public bool Found => Elements.Count > 0;
    public bool IsActive { get; set; }
    public CaptchaType Type { get; set; } = CaptchaType.None;
    public CaptchaState State { get; set; } = CaptchaState.NotPresent;
    public string TypeDescription { get; set; } = "None";
    public bool HasWidgetLoaded { get; set; }
    public string? ImageSrc { get; set; }
    public string? PromptText { get; set; }
    public List<double[]> DetectedTargetCoordinates { get; set; } = new();

    public List<CaptchaElementItem> Elements { get; set; } = new();
    public string CombinedHtml { get; set; } = string.Empty;
    public string FullHtml
    {
        get => CombinedHtml;
        set => CombinedHtml = value;
    }
    public DateTime DetectedAt { get; set; } = DateTime.Now;
    public string? CountdownText { get; set; }
    public bool HasInactivityModal { get; set; }
    public string? CaptchaSessionId { get; set; }
    private byte[]? _capturedImageBytes;
    public byte[]? CapturedImageBytes
    {
        get => _capturedImageBytes ?? DirectImageBytes ?? CroppedImageBytes;
        set => _capturedImageBytes = value;
    }
    public byte[]? DirectImageBytes { get; set; }
    public byte[]? CroppedImageBytes { get; set; }
    public byte[]? PieceImageBytes { get; set; }
    public string? DirectImageDimensions { get; set; }
    public string? CroppedImageDimensions { get; set; }
    public string? CapturedImagePath { get; set; }
    public string? ImageHash { get; set; }
}

public class CaptchaSolveResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public double ElapsedSeconds { get; set; }
    public string? ServerResponseJson { get; set; }
    public DateTime SolvedAt { get; set; } = DateTime.Now;
    public string CaptchaType { get; set; } = string.Empty;
    public List<string> Logs { get; set; } = new();
}

public enum LiveChatMessageType
{
    Chat,           // 💬 ข้อความแชท
    Gift,           // 🎁 ของขวัญ
    Like,           // ❤️ กดถูกใจ
    Follow,         // 👤 กดติดตาม
    Share,          // 🔄 แชร์ไลฟ์
    SentMessage,    // 🚀 ข้อความที่เราส่งออก
    AutoReply,      // 🤖 บอทตอบกลับอัตโนมัติ
    System,         // ℹ️ แจ้งเตือนระบบ
    Other           // 📦 ข้อมูลดิบ/อื่นๆ (Raw JSON)
}

public class LiveChatMessageItem : INotifyPropertyChanged
{
    private bool _isAutoReplied;
    private string? _autoReplyContent;

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string FormattedTime => Timestamp.ToString("HH:mm:ss");
    public LiveChatMessageType Type { get; set; } = LiveChatMessageType.Chat;
    public string Username { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? GiftName { get; set; }
    public int DiamondCount { get; set; }
    public int RepeatCount { get; set; } = 1;
    public string? BadgeText { get; set; }
    public string? RawJson { get; set; }

    public string FormattedJson
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(RawJson))
            {
                return JsonFormatter.TryFormatJson(RawJson);
            }

            var obj = new
            {
                id = Id,
                time = Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                type = Type.ToString(),
                username = Username,
                nickname = Nickname,
                message = Message,
                giftName = GiftName,
                diamonds = DiamondCount,
                repeatCount = RepeatCount,
                isAutoReplied = IsAutoReplied,
                autoReplyContent = AutoReplyContent
            };
            return JsonSerializer.Serialize(obj, JsonFormatter.IndentedOptions);
        }
    }

    public bool IsAutoReplied
    {
        get => _isAutoReplied;
        set { _isAutoReplied = value; OnPropertyChanged(); OnPropertyChanged(nameof(AutoReplyDisplay)); }
    }

    public string? AutoReplyContent
    {
        get => _autoReplyContent;
        set { _autoReplyContent = value; OnPropertyChanged(); OnPropertyChanged(nameof(AutoReplyDisplay)); }
    }

    public string AutoReplyDisplay => IsAutoReplied ? $"🤖 {AutoReplyContent}" : "-";

    public string TypeDisplay => Type switch
    {
        LiveChatMessageType.Chat => "💬 แชท",
        LiveChatMessageType.Gift => $"🎁 {GiftName ?? "ของขวัญ"}{(DiamondCount > 0 ? $" ({DiamondCount} 💎)" : "")}",
        LiveChatMessageType.Like => "❤️ ถูกใจ",
        LiveChatMessageType.Follow => "👤 ติดตาม",
        LiveChatMessageType.Share => "🔄 แชร์",
        LiveChatMessageType.SentMessage => "🚀 ส่งออก",
        LiveChatMessageType.AutoReply => "🤖 บอทตอบ",
        LiveChatMessageType.Other => "📦 อื่นๆ/Raw",
        _ => "ℹ️ ระบบ"
    };

    public string TypeColor => Type switch
    {
        LiveChatMessageType.Chat => "#38BDF8",       // Sky blue
        LiveChatMessageType.Gift => "#F59E0B",       // Amber / Gold
        LiveChatMessageType.Like => "#F43F5E",       // Rose / Red
        LiveChatMessageType.Follow => "#10B981",     // Emerald green
        LiveChatMessageType.Share => "#A855F7",      // Purple
        LiveChatMessageType.SentMessage => "#34D399",// Green
        LiveChatMessageType.AutoReply => "#C084FC",  // Violet
        LiveChatMessageType.Other => "#A3E635",      // Lime / Green-yellow
        _ => "#94A3B8"                               // Slate gray
    };

    public string SenderDisplay => !string.IsNullOrWhiteSpace(Nickname)
        ? (!string.IsNullOrWhiteSpace(Username) && Username != Nickname ? $"{Nickname} (@{Username})" : Nickname)
        : (!string.IsNullOrWhiteSpace(Username) ? $"@{Username}" : "ผู้ชม");

    public string FullText => $"[{FormattedTime}] [{TypeDisplay}] {SenderDisplay}: {Message}" + 
        (GiftName != null ? $" ({GiftName} x{RepeatCount})" : "") +
        (IsAutoReplied ? $" -> [บอท: {AutoReplyContent}]" : "");

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}


