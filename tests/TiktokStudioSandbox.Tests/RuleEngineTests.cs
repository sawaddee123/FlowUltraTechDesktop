using System;
using System.Collections.Generic;
using TiktokStudioSandbox.Core.Interfaces;
using TiktokStudioSandbox.Core.Models;
using TiktokStudioSandbox.Core.Services;
using Xunit;

namespace TiktokStudioSandbox.Tests;

public class RuleEngineTests
{
    [Fact]
    public void EvaluateChatRule_MatchesKeyword_ReturnsFormattedTemplate()
    {
        // Arrange
        var engine = new RuleEngine();
        var rules = new List<AutomationRule>
        {
            new AutomationRule
            {
                IsEnabled = true,
                TriggerType = RuleTriggerType.ChatKeyword,
                Pattern = "!hello",
                ResponseTemplate = "ยินดีต้อนรับ @{User} ({Nickname}) สู่ไลฟ์!",
                CooldownSeconds = 0
            }
        };

        var chat = new LiveChatMessage("johndoe", "John", "สวัสดีครับ !hello ทุกคน");

        // Act
        var response = engine.EvaluateChatRule(rules, chat);

        // Assert
        Assert.Equal("ยินดีต้อนรับ @johndoe (John) สู่ไลฟ์!", response);
    }

    [Fact]
    public void EvaluateGiftRule_MeetsThreshold_ReturnsThankYouMessage()
    {
        // Arrange
        var engine = new RuleEngine();
        var rules = new List<AutomationRule>
        {
            new AutomationRule
            {
                IsEnabled = true,
                TriggerType = RuleTriggerType.GiftMinDiamonds,
                MinDiamonds = 10,
                ResponseTemplate = "ขอบคุณ @{User} สำหรับ {GiftName} จำนวน {Diamonds} เพชร ({Count} ชิ้น)!",
                CooldownSeconds = 0
            }
        };

        var gift = new LiveGiftMessage("somchai", "สมชาย", "Rosa", 10, 1);

        // Act
        var response = engine.EvaluateGiftRule(rules, gift);

        // Assert
        Assert.Equal("ขอบคุณ @somchai สำหรับ Rosa จำนวน 10 เพชร (1 ชิ้น)!", response);
    }

    [Fact]
    public void EvaluateGiftRule_BelowThreshold_ReturnsNull()
    {
        // Arrange
        var engine = new RuleEngine();
        var rules = new List<AutomationRule>
        {
            new AutomationRule
            {
                IsEnabled = true,
                TriggerType = RuleTriggerType.GiftMinDiamonds,
                MinDiamonds = 100,
                ResponseTemplate = "บิ๊กกิฟต์!",
                CooldownSeconds = 0
            }
        };

        var gift = new LiveGiftMessage("somchai", "สมชาย", "Rose", 1, 1);

        // Act
        var response = engine.EvaluateGiftRule(rules, gift);

        // Assert
        Assert.Null(response);
    }

    [Fact]
    public void EvaluateJoinRule_EligibleRule_ReturnsWelcomeMessage()
    {
        // Arrange
        var engine = new RuleEngine();
        var rules = new List<AutomationRule>
        {
            new AutomationRule
            {
                IsEnabled = true,
                TriggerType = RuleTriggerType.MemberJoin,
                ResponseTemplate = "ยินดีต้อนรับคุณ @{User} ({Nickname}) เข้าสู่ห้องไลฟ์ครับ!",
                CooldownSeconds = 0
            }
        };

        var join = new LiveJoinMessage("alice99", "Alice");

        // Act
        var response = engine.EvaluateJoinRule(rules, join);

        // Assert
        Assert.Equal("ยินดีต้อนรับคุณ @alice99 (Alice) เข้าสู่ห้องไลฟ์ครับ!", response);
    }

    [Fact]
    public void EvaluateLikeRule_MeetsThreshold_ReturnsThankYouMessage()
    {
        // Arrange
        var engine = new RuleEngine();
        var rules = new List<AutomationRule>
        {
            new AutomationRule
            {
                IsEnabled = true,
                TriggerType = RuleTriggerType.LikeMilestone,
                MinDiamonds = 50,
                ResponseTemplate = "ขอบคุณ @{User} ({Nickname}) สำหรับ {Likes} ไลก์ (กดเพิ่ม {Count}) มากๆ ครับ!",
                CooldownSeconds = 0
            }
        };

        var like = new LiveLikeMessage("bob123", "Bob", 15, 100);

        // Act
        var response = engine.EvaluateLikeRule(rules, like);

        // Assert
        Assert.Equal("ขอบคุณ @bob123 (Bob) สำหรับ 100 ไลก์ (กดเพิ่ม 15) มากๆ ครับ!", response);
    }
}
