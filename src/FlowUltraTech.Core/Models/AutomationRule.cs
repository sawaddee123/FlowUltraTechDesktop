using System;

namespace FlowUltraTech.Core.Models;

public class AutomationRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Auto Reply Rule";
    public bool IsEnabled { get; set; } = true;
    public RuleTriggerType TriggerType { get; set; } = RuleTriggerType.ChatKeyword;
    
    /// <summary>
    /// For ChatKeyword: pattern to match, e.g. "!help" or "สวัสดี"
    /// </summary>
    public string Pattern { get; set; } = string.Empty;
    public bool IsRegex { get; set; } = false;

    /// <summary>
    /// For GiftMinDiamonds: minimum diamond count threshold
    /// </summary>
    public int MinDiamonds { get; set; } = 1;

    /// <summary>
    /// Response template, e.g. "ขอบคุณ @{User} สำหรับ {GiftName} x{Count} ชิ้นมากครับ!"
    /// </summary>
    public string ResponseTemplate { get; set; } = "ขอบคุณครับ!";

    /// <summary>
    /// Cooldown between triggering this rule in seconds
    /// </summary>
    public int CooldownSeconds { get; set; } = 3;
    public DateTime LastTriggered { get; set; } = DateTime.MinValue;
}
