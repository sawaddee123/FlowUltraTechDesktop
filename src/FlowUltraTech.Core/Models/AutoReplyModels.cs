using System;
using System.Collections.Generic;

namespace FlowUltraTech.Core.Models;

public enum JoinGreetingMode
{
    RandomMessage = 0, // สุ่มข้อความจากรายการ
    AiGenerated = 1    // ให้ AI สร้างคำทักทาย
}

public enum JoinGreetingFrequency
{
    OncePerViewer = 0, // ทักครั้งเดียว/คน ตลอดไลฟ์
    EveryJoin = 1      // ทักทุกครั้งที่เข้า
}

public enum AiOperatingMode
{
    ReplyToComments = 0, // ตอบคอมเมนต์ผู้ชม
    PeriodicPost = 1     // โพสต์เองเป็นระยะ
}

public enum AiReplyFilterMode
{
    AllComments = 0,   // ทุกคอมเมนต์
    QuestionsOnly = 1  // เฉพาะคำถาม
}

public class ParsedKeywordRule
{
    public List<string> Keywords { get; set; } = new();
    public List<string> Responses { get; set; } = new();
}

public class InstanceAutoReplyConfig
{
    // Mode 2: วนส่ง (Periodic Loop Post)
    public bool EnableLoopPost { get; set; } = false;
    public string LoopMessagesText { get; set; } =
        "พิมพ์ 'สนใจ' หรือสอบถามไซส์/สีในแชทได้เลย\nกดดูสินค้าโปรโมชันพิเศษที่ตะกร้าซ้ายล่างได้เลย\nฝากกดหัวใจ กดติดตามร้านค้าเพื่อรับแจ้งเตือนโปรนาทีทอง";
    public int LoopIntervalSeconds { get; set; } = 60;
    public bool LoopRandomOrder { get; set; } = false;

    // Mode 3: กฎคำ (Shorthand Keyword Rules + Anti-Spam / Anti-Ban)
    public bool EnableKeywordRules { get; set; } = true;
    public string KeywordRulesText { get; set; } =
        "ราคา, เท่าไหร่, กี่บาท = ตอนนี้จัดโปรพิเศษ กดดูราคาที่ตะกร้าซ้ายล่างได้เลย | ราคาโปรวันนี้คุ้มมาก กดที่ตะกร้าได้เลย\n" +
        "ส่งฟรี, ค่าส่ง = ตอนนี้มีคูปองส่งฟรี กดเก็บคูปองที่ตะกร้าได้เลย\n" +
        "มีของไหม, พร้อมส่ง = สินค้าพร้อมส่งทุกชิ้น กดสั่งในตะกร้าได้เลย {user}";
    public int RuleMinIntervalSeconds { get; set; } = 4;
    public int RuleUserCooldownSeconds { get; set; } = 30;
    public int RuleMaxRepliesPerMinute { get; set; } = 12;
    public bool RulePrefixWithMention { get; set; } = true;

    // Mode 4: ทักทาย (Viewer Join Greeting)
    public bool EnableJoinGreeting { get; set; } = false;
    public string JoinGreetingMessagesText { get; set; } =
        "สวัสดี {user} ยินดีต้อนรับเข้าสู่ไลฟ์สด\nยินดีต้อนรับ {user} แวะชมสินค้าในตะกร้าก่อนได้เลย";
    public JoinGreetingMode GreetingMode { get; set; } = JoinGreetingMode.RandomMessage;
    public JoinGreetingFrequency GreetingFrequency { get; set; } = JoinGreetingFrequency.OncePerViewer;
    public int GreetingMinIntervalSeconds { get; set; } = 8;

    // Mode 5: AI ตอบ (OpenRouter / OpenAI Integration)
    public bool EnableAiReply { get; set; } = false;
    public string AiApiKey { get; set; } = string.Empty;
    public string AiBaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public string AiModel { get; set; } = "openai/gpt-4o-mini";
    public string AiSystemPrompt { get; set; } =
        "คุณคือแอดมินไลฟ์สดขายสินค้าบน TikTok ตอบคำถามลูกค้าอย่างสุภาพ เป็นกันเอง กระชับ ได้ใจความ และชวนลูกค้ากดดูสินค้าที่ตะกร้าซ้ายล่าง ห้ามพูดเรื่องโอนเงินนอกแอป";
    public AiOperatingMode AiMode { get; set; } = AiOperatingMode.ReplyToComments;
    public AiReplyFilterMode AiFilterMode { get; set; } = AiReplyFilterMode.QuestionsOnly;
    public int AiMinWords { get; set; } = 0;
    public int AiMaxWords { get; set; } = 30;
    public int AiUserCooldownSeconds { get; set; } = 45;
    public int AiPeriodicIntervalSeconds { get; set; } = 90;
    public string AiForbiddenWords { get; set; } = "แอดไลน์, Line, โอนตรง, นอกแอป, พนัน, เว็บตรง";

    public InstanceAutoReplyConfig Clone()
    {
        return new InstanceAutoReplyConfig
        {
            EnableLoopPost = EnableLoopPost,
            LoopMessagesText = LoopMessagesText,
            LoopIntervalSeconds = LoopIntervalSeconds,
            LoopRandomOrder = LoopRandomOrder,
            EnableKeywordRules = EnableKeywordRules,
            KeywordRulesText = KeywordRulesText,
            RuleMinIntervalSeconds = RuleMinIntervalSeconds,
            RuleUserCooldownSeconds = RuleUserCooldownSeconds,
            RuleMaxRepliesPerMinute = RuleMaxRepliesPerMinute,
            RulePrefixWithMention = RulePrefixWithMention,
            EnableJoinGreeting = EnableJoinGreeting,
            JoinGreetingMessagesText = JoinGreetingMessagesText,
            GreetingMode = GreetingMode,
            GreetingFrequency = GreetingFrequency,
            GreetingMinIntervalSeconds = GreetingMinIntervalSeconds,
            EnableAiReply = EnableAiReply,
            AiApiKey = AiApiKey,
            AiBaseUrl = AiBaseUrl,
            AiModel = AiModel,
            AiSystemPrompt = AiSystemPrompt,
            AiMode = AiMode,
            AiFilterMode = AiFilterMode,
            AiMinWords = AiMinWords,
            AiMaxWords = AiMaxWords,
            AiUserCooldownSeconds = AiUserCooldownSeconds,
            AiPeriodicIntervalSeconds = AiPeriodicIntervalSeconds,
            AiForbiddenWords = AiForbiddenWords
        };
    }
}

public class ProxyTestResult
{
    public bool Success { get; set; }
    public string ExitIp { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Isp { get; set; } = string.Empty;
    public long LatencyMs { get; set; }
    public string Message { get; set; } = string.Empty;
}
