using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TiktokStudioSandbox.Core.Interfaces;
using TiktokStudioSandbox.Core.Models;

namespace TiktokStudioSandbox.Core.Services;

public class RuleEngine : IRuleEngine
{
    public string? EvaluateChatRule(IEnumerable<AutomationRule> rules, LiveChatMessage chat)
    {
        var now = DateTime.Now;
        var eligibleRules = rules
            .Where(r => r.IsEnabled && r.TriggerType == RuleTriggerType.ChatKeyword && !string.IsNullOrWhiteSpace(r.Pattern))
            .ToList();

        foreach (var rule in eligibleRules)
        {
            if ((now - rule.LastTriggered).TotalSeconds < rule.CooldownSeconds)
            {
                continue;
            }

            bool isMatch = false;
            if (rule.IsRegex)
            {
                try
                {
                    isMatch = Regex.IsMatch(chat.Comment, rule.Pattern, RegexOptions.IgnoreCase);
                }
                catch { }
            }
            else
            {
                isMatch = chat.Comment.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase);
            }

            if (isMatch)
            {
                rule.LastTriggered = now;
                return FormatTemplate(rule.ResponseTemplate, new Dictionary<string, string>
                {
                    { "User", chat.Username },
                    { "Nickname", chat.Nickname },
                    { "Comment", chat.Comment }
                });
            }
        }

        return null;
    }

    public string? EvaluateGiftRule(IEnumerable<AutomationRule> rules, LiveGiftMessage gift)
    {
        var now = DateTime.Now;
        var eligibleRules = rules
            .Where(r => r.IsEnabled && r.TriggerType == RuleTriggerType.GiftMinDiamonds)
            .OrderByDescending(r => r.MinDiamonds)
            .ToList();

        foreach (var rule in eligibleRules)
        {
            if (gift.DiamondCount < rule.MinDiamonds)
            {
                continue;
            }

            if ((now - rule.LastTriggered).TotalSeconds < rule.CooldownSeconds)
            {
                continue;
            }

            rule.LastTriggered = now;
            return FormatTemplate(rule.ResponseTemplate, new Dictionary<string, string>
            {
                { "User", gift.Username },
                { "Nickname", gift.Nickname },
                { "GiftName", gift.GiftName },
                { "Diamonds", gift.DiamondCount.ToString() },
                { "Count", gift.RepeatCount.ToString() }
            });
        }

        return null;
    }

    public string? EvaluateJoinRule(IEnumerable<AutomationRule> rules, LiveJoinMessage join)
    {
        var now = DateTime.Now;
        var eligibleRules = rules
            .Where(r => r.IsEnabled && r.TriggerType == RuleTriggerType.MemberJoin)
            .ToList();

        foreach (var rule in eligibleRules)
        {
            if ((now - rule.LastTriggered).TotalSeconds < rule.CooldownSeconds)
            {
                continue;
            }

            rule.LastTriggered = now;
            return FormatTemplate(rule.ResponseTemplate, new Dictionary<string, string>
            {
                { "User", join.Username },
                { "Nickname", join.Nickname }
            });
        }

        return null;
    }

    public string? EvaluateLikeRule(IEnumerable<AutomationRule> rules, LiveLikeMessage like)
    {
        var now = DateTime.Now;
        var eligibleRules = rules
            .Where(r => r.IsEnabled && r.TriggerType == RuleTriggerType.LikeMilestone)
            .OrderByDescending(r => r.MinDiamonds)
            .ToList();

        foreach (var rule in eligibleRules)
        {
            int threshold = rule.MinDiamonds;
            if (int.TryParse(rule.Pattern, out int parsedThreshold) && parsedThreshold > 0)
            {
                threshold = parsedThreshold;
            }

            if (like.TotalLikes < threshold && like.LikeCount < threshold)
            {
                continue;
            }

            if ((now - rule.LastTriggered).TotalSeconds < rule.CooldownSeconds)
            {
                continue;
            }

            rule.LastTriggered = now;
            return FormatTemplate(rule.ResponseTemplate, new Dictionary<string, string>
            {
                { "User", like.Username },
                { "Nickname", like.Nickname },
                { "Likes", like.TotalLikes.ToString() },
                { "Count", like.LikeCount.ToString() }
            });
        }

        return null;
    }

    private static string FormatTemplate(string template, Dictionary<string, string> tokens)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;
        string result = template;
        foreach (var (key, value) in tokens)
        {
            result = result.Replace($"{{{key}}}", value, StringComparison.OrdinalIgnoreCase);
        }
        return result;
    }
}
