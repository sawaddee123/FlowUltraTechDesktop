using System;

namespace FlowUltraTech.Core.Models;

public enum OverlayClockMode
{
    RealisticWall = 0,
    SeikoCustom = 1,
    Desk3D = 2,
    TwinBell = 3,
    WoodBlockLed = 4,
    DigitalClean = 5
}

public enum OverlayBackgroundStyle
{
    Transparent = 0,
    GlassDark = 1,
    GlassLight = 2
}

public class OverlayConfig
{
    public bool IsEnabled { get; set; } = true;
    public OverlayClockMode ClockMode { get; set; } = OverlayClockMode.RealisticWall;
    public OverlayBackgroundStyle BackgroundStyle { get; set; } = OverlayBackgroundStyle.Transparent;
    public string TimeZoneId { get; set; } = "Asia/Bangkok";
    public bool Use24HourFormat { get; set; } = true;
    public bool ShowSeconds { get; set; } = true;
    public bool ShowThaiDate { get; set; } = true;
    public string PrimaryColorHex { get; set; } = "#F8FAFC";
    public string AccentColorHex { get; set; } = "#EF4444";
    public string BrandText { get; set; } = "LIVE STUDIO";
    public string SubtitleText { get; set; } = "REALTIME VERIFIED";
    public bool EnableGlareEffect { get; set; } = true;
    public bool EnableDynamicShadow { get; set; } = true;
    public bool EnableMicroJitter { get; set; } = true;
    public bool EnableLiveEventTicker { get; set; } = true;
    public bool EnableFlashDealCountdown { get; set; } = false;
    public string FlashDealTitle { get; set; } = "โปรนาทีทอง ส่งฟรี";
    public int FlashDealDurationSeconds { get; set; } = 300;
    public DateTime? FlashDealEndsAtUtc { get; set; } = null;

    public OverlayConfig Clone()
    {
        return new OverlayConfig
        {
            IsEnabled = IsEnabled,
            ClockMode = ClockMode,
            BackgroundStyle = BackgroundStyle,
            TimeZoneId = string.IsNullOrWhiteSpace(TimeZoneId) ? "Asia/Bangkok" : TimeZoneId,
            Use24HourFormat = Use24HourFormat,
            ShowSeconds = ShowSeconds,
            ShowThaiDate = ShowThaiDate,
            PrimaryColorHex = string.IsNullOrWhiteSpace(PrimaryColorHex) ? "#F8FAFC" : PrimaryColorHex,
            AccentColorHex = string.IsNullOrWhiteSpace(AccentColorHex) ? "#EF4444" : AccentColorHex,
            BrandText = BrandText ?? string.Empty,
            SubtitleText = SubtitleText ?? string.Empty,
            EnableGlareEffect = EnableGlareEffect,
            EnableDynamicShadow = EnableDynamicShadow,
            EnableMicroJitter = EnableMicroJitter,
            EnableLiveEventTicker = EnableLiveEventTicker,
            EnableFlashDealCountdown = EnableFlashDealCountdown,
            FlashDealTitle = FlashDealTitle ?? string.Empty,
            FlashDealDurationSeconds = FlashDealDurationSeconds,
            FlashDealEndsAtUtc = FlashDealEndsAtUtc
        };
    }
}

public class OverlayLiveEventPayload
{
    public string EventType { get; set; } = "chat";
    public string Nickname { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string BadgeText { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

public class OverlayInjectResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string InjectedUrl { get; set; } = string.Empty;
    public string? StepFailed { get; set; }
}
