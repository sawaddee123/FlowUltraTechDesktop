using System.Collections.Generic;
using TiktokStudioSandbox.Core.Models;

namespace TiktokStudioSandbox.Core.Interfaces;

public record LiveChatMessage(string Username, string Nickname, string Comment);
public record LiveGiftMessage(string Username, string Nickname, string GiftName, int DiamondCount, int RepeatCount);

public interface IRuleEngine
{
    string? EvaluateChatRule(IEnumerable<AutomationRule> rules, LiveChatMessage chat);
    string? EvaluateGiftRule(IEnumerable<AutomationRule> rules, LiveGiftMessage gift);
}
