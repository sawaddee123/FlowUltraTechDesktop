using System.Collections.Generic;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Interfaces;

public record LiveChatMessage(string Username, string Nickname, string Comment);
public record LiveGiftMessage(string Username, string Nickname, string GiftName, int DiamondCount, int RepeatCount);
public record LiveJoinMessage(string Username, string Nickname);
public record LiveLikeMessage(string Username, string Nickname, int LikeCount, int TotalLikes);

public interface IRuleEngine
{
    string? EvaluateChatRule(IEnumerable<AutomationRule> rules, LiveChatMessage chat);
    string? EvaluateGiftRule(IEnumerable<AutomationRule> rules, LiveGiftMessage gift);
    string? EvaluateJoinRule(IEnumerable<AutomationRule> rules, LiveJoinMessage join);
    string? EvaluateLikeRule(IEnumerable<AutomationRule> rules, LiveLikeMessage like);
}
