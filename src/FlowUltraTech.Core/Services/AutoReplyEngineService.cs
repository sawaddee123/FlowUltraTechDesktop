using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlowUltraTech.Core.Models;

namespace FlowUltraTech.Core.Services;

/// <summary>
/// Full 5-Mode Auto-Reply & Live Comment Engine:
/// 1. ส่งทันที (Instant Send)
/// 2. วนส่ง (Periodic Loop Post)
/// 3. กฎคำ (Shorthand Keyword Rules + Anti-Spam / Anti-Ban Rate Limiter)
/// 4. ทักทาย (Viewer Join Greeting - Random or AI Generated, Once per viewer or Every join)
/// 5. AI ตอบ (OpenRouter / OpenAI Smart Responder with Question Filter, Word Limits & Forbidden Words Filter)
/// </summary>
public sealed class AutoReplyEngineService
{
    public static AutoReplyEngineService Shared { get; } = new();

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private sealed class InstanceRuntimeState
    {
        public readonly object SyncRoot = new();
        public DateTime LastReplySentUtc = DateTime.MinValue;
        public DateTime LastGreetingSentUtc = DateTime.MinValue;
        public readonly Queue<DateTime> ReplyTimestampsUtc = new();
        public readonly ConcurrentDictionary<string, DateTime> UserRuleCooldownUtc = new(StringComparer.OrdinalIgnoreCase);
        public readonly ConcurrentDictionary<string, DateTime> UserAiCooldownUtc = new(StringComparer.OrdinalIgnoreCase);
        public readonly ConcurrentDictionary<string, byte> GreetedUsers = new(StringComparer.OrdinalIgnoreCase);
        public int LoopMessageIndex = 0;
    }

    private readonly ConcurrentDictionary<Guid, InstanceRuntimeState> _states = new();

    private static readonly string[] ThaiQuestionIndicators =
    {
        "?", "？", "ไหม", "มั้ย", "มัย", "หรือเปล่า", "รึเปล่า", "หรอ", "เหรอ",
        "เท่าไหร่", "เท่าไร", "กี่บาท", "กี่วัน", "กี่ชิ้น", "ยังไง", "อย่างไร", "แบบไหน",
        "ตัวไหน", "สีไหน", "ไซส์ไหน", "ไซส์อะไร", "อะไรบ้าง", "ที่ไหน", "เมื่อไหร่",
        "ราคา", "ค่าส่ง", "ส่งฟรี", "พร้อมส่ง", "มีของ", "มีเก็บปลายทาง", "ปลายทาง",
        "สนใจ", "สอบถาม", "ขอทราบ", "แนะนำ", "ลดได้", "มีโปร", "ของแท้"
    };

    public void ResetInstanceState(Guid instanceId)
    {
        _states.TryRemove(instanceId, out _);
    }

    private InstanceRuntimeState GetState(Guid instanceId)
    {
        return _states.GetOrAdd(instanceId, _ => new InstanceRuntimeState());
    }

    /// <summary>
    /// Parses multi-line shorthand keyword rules of the format:
    /// คำ1, คำ2 = คำตอบ1 | คำตอบ2
    /// </summary>
    public static List<ParsedKeywordRule> ParseKeywordRules(string? rulesText)
    {
        var result = new List<ParsedKeywordRule>();
        if (string.IsNullOrWhiteSpace(rulesText)) return result;

        var lines = rulesText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith("//"))
                continue;

            int eqIdx = line.IndexOf('=');
            if (eqIdx <= 0 || eqIdx >= line.Length - 1)
                continue;

            string left = line.Substring(0, eqIdx).Trim();
            string right = line.Substring(eqIdx + 1).Trim();

            var keywords = left
                .Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => !string.IsNullOrEmpty(k))
                .ToList();

            var responses = right
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Trim())
                .Where(r => !string.IsNullOrEmpty(r))
                .ToList();

            if (keywords.Count > 0 && responses.Count > 0)
            {
                result.Add(new ParsedKeywordRule
                {
                    Keywords = keywords,
                    Responses = responses
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Extracts non-empty lines from a multi-line text block (used for Loop Post and Join Greetings).
    /// </summary>
    public static List<string> ParseLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        return text
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith("#"))
            .ToList();
    }

    /// <summary>
    /// Gets the next message for Mode 2: วนส่ง (Periodic Loop Post).
    /// </summary>
    public string? GetNextLoopMessage(Guid instanceId, InstanceAutoReplyConfig config)
    {
        if (config == null || !config.EnableLoopPost) return null;
        var messages = ParseLines(config.LoopMessagesText);
        if (messages.Count == 0) return null;

        var state = GetState(instanceId);
        lock (state.SyncRoot)
        {
            string chosen;
            if (config.LoopRandomOrder)
            {
                chosen = messages[Random.Shared.Next(messages.Count)];
            }
            else
            {
                int idx = Math.Abs(state.LoopMessageIndex++) % messages.Count;
                chosen = messages[idx];
            }

            return TruncateForTikTok(chosen);
        }
    }

    /// <summary>
    /// Evaluates an incoming viewer comment against Mode 3 (กฎคำ + Anti-Spam) and Mode 5 (AI ตอบ).
    /// Returns the reply text and the source tag ("RULE" or "AI"), or null if no reply should be sent.
    /// </summary>
    public async Task<(string? ReplyText, string SourceTag)?> EvaluateIncomingCommentAsync(
        Guid instanceId,
        InstanceAutoReplyConfig config,
        string username,
        string nickname,
        string commentText,
        CancellationToken ct = default)
    {
        if (config == null || string.IsNullOrWhiteSpace(commentText))
            return null;

        if (!config.EnableKeywordRules && !(config.EnableAiReply && config.AiMode == AiOperatingMode.ReplyToComments))
            return null;

        string userKey = !string.IsNullOrWhiteSpace(username) ? username.Trim() : nickname.Trim();
        if (string.IsNullOrWhiteSpace(userKey)) userKey = "viewer";
        string displayName = !string.IsNullOrWhiteSpace(nickname) ? nickname.Trim() : userKey;

        var state = GetState(instanceId);
        var nowUtc = DateTime.UtcNow;

        // 1. Check Anti-Spam Global Minimum Interval & Max Replies Per Minute
        if (!CanPassGlobalRateLimit(state, config, nowUtc))
            return null;

        // 2. Mode 3: Try Shorthand Keyword Rules first
        if (config.EnableKeywordRules)
        {
            if (!IsUserOnCooldown(state.UserRuleCooldownUtc, userKey, config.RuleUserCooldownSeconds, nowUtc))
            {
                var rules = ParseKeywordRules(config.KeywordRulesText);
                foreach (var rule in rules)
                {
                    bool matched = rule.Keywords.Any(k => commentText.Contains(k, StringComparison.OrdinalIgnoreCase));
                    if (matched && rule.Responses.Count > 0)
                    {
                        string template = rule.Responses[Random.Shared.Next(rule.Responses.Count)];
                        bool usedPlaceholder = template.Contains("{user}", StringComparison.OrdinalIgnoreCase) ||
                                               template.Contains("{nickname}", StringComparison.OrdinalIgnoreCase);

                        string formatted = template
                            .Replace("{user}", displayName, StringComparison.OrdinalIgnoreCase)
                            .Replace("{nickname}", displayName, StringComparison.OrdinalIgnoreCase)
                            .Trim();

                        if (config.RulePrefixWithMention && !usedPlaceholder && !formatted.StartsWith("@"))
                        {
                            formatted = $"@{displayName} {formatted}";
                        }

                        formatted = FilterForbiddenWords(formatted, config.AiForbiddenWords);
                        if (!string.IsNullOrWhiteSpace(formatted))
                        {
                            RecordReplySent(state, state.UserRuleCooldownUtc, userKey, nowUtc);
                            return (TruncateForTikTok(formatted), "กฎคำ");
                        }
                    }
                }
            }
        }

        // 3. Mode 5: Try AI Responder (OpenRouter / OpenAI) if enabled
        if (config.EnableAiReply &&
            config.AiMode == AiOperatingMode.ReplyToComments &&
            !string.IsNullOrWhiteSpace(config.AiApiKey))
        {
            if (IsUserOnCooldown(state.UserAiCooldownUtc, userKey, config.AiUserCooldownSeconds, nowUtc))
                return null;

            if (config.AiFilterMode == AiReplyFilterMode.QuestionsOnly && !IsLikelyQuestionOrInquiry(commentText))
                return null;

            string? aiReply = await GenerateAiReplyAsync(config, displayName, commentText, isGreeting: false, isPeriodicPost: false, ct);
            if (!string.IsNullOrWhiteSpace(aiReply))
            {
                RecordReplySent(state, state.UserAiCooldownUtc, userKey, DateTime.UtcNow);
                return (TruncateForTikTok(aiReply), "AI");
            }
        }

        return null;
    }

    /// <summary>
    /// Evaluates Mode 4: ทักทาย (Viewer Join Greeting) when a viewer enters the live stream.
    /// </summary>
    public async Task<string?> EvaluateViewerJoinAsync(
        Guid instanceId,
        InstanceAutoReplyConfig config,
        string username,
        string nickname,
        CancellationToken ct = default)
    {
        if (config == null || !config.EnableJoinGreeting)
            return null;

        string userKey = !string.IsNullOrWhiteSpace(username) ? username.Trim() : nickname.Trim();
        if (string.IsNullOrWhiteSpace(userKey)) return null;
        string displayName = !string.IsNullOrWhiteSpace(nickname) ? nickname.Trim() : userKey;

        var state = GetState(instanceId);
        var nowUtc = DateTime.UtcNow;

        lock (state.SyncRoot)
        {
            int minGapSec = Math.Max(2, config.GreetingMinIntervalSeconds);
            if ((nowUtc - state.LastGreetingSentUtc).TotalSeconds < minGapSec)
                return null;

            if (config.GreetingFrequency == JoinGreetingFrequency.OncePerViewer)
            {
                if (!state.GreetedUsers.TryAdd(userKey, 1))
                    return null; // Already greeted during this session
            }

            state.LastGreetingSentUtc = nowUtc;
        }

        if (config.GreetingMode == JoinGreetingMode.AiGenerated && !string.IsNullOrWhiteSpace(config.AiApiKey))
        {
            string? aiGreeting = await GenerateAiReplyAsync(
                config,
                displayName,
                $"ผู้ชมชื่อ {displayName} เพิ่งเข้ามาในไลฟ์สด ช่วยกล่าวทักทายต้อนรับสั้นๆ อย่างเป็นกันเอง",
                isGreeting: true,
                isPeriodicPost: false,
                ct);
            if (!string.IsNullOrWhiteSpace(aiGreeting))
                return TruncateForTikTok(aiGreeting);
        }

        var templates = ParseLines(config.JoinGreetingMessagesText);
        if (templates.Count == 0) return null;

        string chosen = templates[Random.Shared.Next(templates.Count)];
        string formatted = chosen
            .Replace("{user}", displayName, StringComparison.OrdinalIgnoreCase)
            .Replace("{nickname}", displayName, StringComparison.OrdinalIgnoreCase)
            .Trim();

        formatted = FilterForbiddenWords(formatted, config.AiForbiddenWords);
        return string.IsNullOrWhiteSpace(formatted) ? null : TruncateForTikTok(formatted);
    }

    /// <summary>
    /// Generates a periodic promotional post using AI when AiMode == AiOperatingMode.PeriodicPost.
    /// </summary>
    public async Task<string?> GeneratePeriodicAiPostAsync(
        InstanceAutoReplyConfig config,
        CancellationToken ct = default)
    {
        if (config == null || !config.EnableAiReply || string.IsNullOrWhiteSpace(config.AiApiKey))
            return null;

        string? post = await GenerateAiReplyAsync(
            config,
            "ผู้ชมในไลฟ์",
            "ช่วยคิดข้อความสั้นๆ 1 ประโยคสำหรับกระตุ้นยอดขาย ชวนผู้ชมกดดูสินค้าในตะกร้าหรือพิมพ์สอบถามในแชท",
            isGreeting: false,
            isPeriodicPost: true,
            ct);

        return string.IsNullOrWhiteSpace(post) ? null : TruncateForTikTok(post);
    }

    /// <summary>
    /// Tests the OpenRouter / OpenAI configuration with a sample comment and returns the generated response + latency.
    /// </summary>
    public async Task<(bool Success, string Message, long LatencyMs)> TestAiReplyAsync(
        InstanceAutoReplyConfig config,
        string sampleComment = "ตัวนี้ราคาเท่าไหร่ ส่งฟรีไหม",
        CancellationToken ct = default)
    {
        if (config == null || string.IsNullOrWhiteSpace(config.AiApiKey))
        {
            return (false, "กรุณากรอก API Key (เช่น sk-or-v1-...) ก่อนทดสอบ", 0);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            string? reply = await GenerateAiReplyAsync(config, "ลูกค้าทดสอบ", sampleComment, isGreeting: false, isPeriodicPost: false, ct, throwOnError: true);
            sw.Stop();
            if (string.IsNullOrWhiteSpace(reply))
            {
                return (false, "AI ตอบกลับเป็นค่าว่าง กรุณาตรวจสอบชื่อโมเดลหรือคำต้องห้าม", sw.ElapsedMilliseconds);
            }
            return (true, $"AI ตอบกลับ ({sw.ElapsedMilliseconds} ms): \"{reply}\"", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return (false, $"เชื่อมต่อ AI ไม่สำเร็จ: {ex.GetBaseException().Message}", sw.ElapsedMilliseconds);
        }
    }

    private async Task<string?> GenerateAiReplyAsync(
        InstanceAutoReplyConfig config,
        string viewerName,
        string userPromptText,
        bool isGreeting,
        bool isPeriodicPost,
        CancellationToken ct,
        bool throwOnError = false)
    {
        try
        {
            string baseUrl = string.IsNullOrWhiteSpace(config.AiBaseUrl)
                ? "https://openrouter.ai/api/v1"
                : config.AiBaseUrl.Trim().TrimEnd('/');

            string endpoint = baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
                ? baseUrl
                : $"{baseUrl}/chat/completions";

            string model = string.IsNullOrWhiteSpace(config.AiModel)
                ? "openai/gpt-4o-mini"
                : config.AiModel.Trim();

            int minWords = Math.Max(0, config.AiMinWords);
            int maxWords = Math.Max(5, config.AiMaxWords);

            var sysBuilder = new StringBuilder();
            sysBuilder.AppendLine(string.IsNullOrWhiteSpace(config.AiSystemPrompt)
                ? "คุณคือแอดมินไลฟ์สดขายของบน TikTok พูดจาสุภาพ เป็นกันเอง กระชับ"
                : config.AiSystemPrompt.Trim());

            sysBuilder.AppendLine($"กฎเหล็ก: ตอบเป็นภาษาไทยธรรมชาติ ความยาวประมาณ {(minWords > 0 ? $"{minWords}-" : "")}{maxWords} คำ (ห้ามเกิน 160 ตัวอักษร) ห้ามใส่เครื่องหมายคำพูดครอบข้อความ");
            if (!string.IsNullOrWhiteSpace(config.AiForbiddenWords))
            {
                sysBuilder.AppendLine($"ห้ามใช้คำต้องห้ามเหล่านี้เด็ดขาด: {config.AiForbiddenWords}");
            }

            string userContent = isPeriodicPost
                ? userPromptText
                : isGreeting
                    ? userPromptText
                    : $"ผู้ชมชื่อ @{viewerName} คอมเมนต์ว่า: \"{userPromptText}\" ช่วยตอบกลับคอมเมนต์นี้สั้นๆ";

            var requestBody = new
            {
                model = model,
                messages = new object[]
                {
                    new { role = "system", content = sysBuilder.ToString() },
                    new { role = "user", content = userContent }
                },
                max_tokens = 120,
                temperature = 0.7
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AiApiKey.Trim());
            req.Headers.TryAddWithoutValidation("HTTP-Referer", "https://flowultratech.app");
            req.Headers.TryAddWithoutValidation("X-Title", "FlowUltraTech Live Studio");
            req.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            using var resp = await _httpClient.SendAsync(req, ct);
            string respJson = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                if (throwOnError)
                    throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {respJson}");
                return null;
            }

            using var doc = JsonDocument.Parse(respJson);
            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var msgEl) &&
                    msgEl.TryGetProperty("content", out var contentEl))
                {
                    string rawReply = (contentEl.GetString() ?? string.Empty).Trim().Trim('"', '“', '”', '\'');
                    rawReply = rawReply.Replace("\r", " ").Replace("\n", " ").Trim();
                    rawReply = FilterForbiddenWords(rawReply, config.AiForbiddenWords);

                    if (!isPeriodicPost && !isGreeting && config.RulePrefixWithMention &&
                        !rawReply.StartsWith("@") && !rawReply.Contains(viewerName, StringComparison.OrdinalIgnoreCase))
                    {
                        rawReply = $"@{viewerName} {rawReply}";
                    }

                    return TruncateForTikTok(rawReply);
                }
            }

            return null;
        }
        catch
        {
            if (throwOnError) throw;
            return null;
        }
    }

    public static bool IsLikelyQuestionOrInquiry(string comment)
    {
        if (string.IsNullOrWhiteSpace(comment)) return false;
        foreach (var indicator in ThaiQuestionIndicators)
        {
            if (comment.Contains(indicator, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static string FilterForbiddenWords(string input, string? forbiddenCsv)
    {
        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(forbiddenCsv))
            return input;

        var forbiddenList = forbiddenCsv
            .Split(new[] { ',', '，', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim())
            .Where(w => w.Length > 0)
            .ToList();

        string output = input;
        foreach (var badWord in forbiddenList)
        {
            if (output.Contains(badWord, StringComparison.OrdinalIgnoreCase))
            {
                output = output.Replace(badWord, "***", StringComparison.OrdinalIgnoreCase);
            }
        }
        return output;
    }

    private static bool CanPassGlobalRateLimit(InstanceRuntimeState state, InstanceAutoReplyConfig config, DateTime nowUtc)
    {
        lock (state.SyncRoot)
        {
            int minIntervalSec = Math.Max(1, config.RuleMinIntervalSeconds);
            if ((nowUtc - state.LastReplySentUtc).TotalSeconds < minIntervalSec)
                return false;

            while (state.ReplyTimestampsUtc.Count > 0 && (nowUtc - state.ReplyTimestampsUtc.Peek()).TotalSeconds > 60)
            {
                state.ReplyTimestampsUtc.Dequeue();
            }

            int maxPerMin = Math.Max(1, config.RuleMaxRepliesPerMinute);
            if (state.ReplyTimestampsUtc.Count >= maxPerMin)
                return false;

            return true;
        }
    }

    private static bool IsUserOnCooldown(ConcurrentDictionary<string, DateTime> dict, string userKey, int cooldownSeconds, DateTime nowUtc)
    {
        if (cooldownSeconds <= 0) return false;
        if (dict.TryGetValue(userKey, out var lastUtc))
        {
            if ((nowUtc - lastUtc).TotalSeconds < cooldownSeconds)
                return true;
        }
        return false;
    }

    private static void RecordReplySent(InstanceRuntimeState state, ConcurrentDictionary<string, DateTime> userDict, string userKey, DateTime nowUtc)
    {
        lock (state.SyncRoot)
        {
            state.LastReplySentUtc = nowUtc;
            state.ReplyTimestampsUtc.Enqueue(nowUtc);
        }
        userDict[userKey] = nowUtc;
    }

    private static string TruncateForTikTok(string message)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;
        string clean = message.Trim();
        return clean.Length <= 200 ? clean : clean.Substring(0, 200);
    }
}
