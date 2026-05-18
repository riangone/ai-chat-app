namespace AiChatApp.Services.Infrastructure;

public static class LocalizationRegistry
{
    private static readonly Dictionary<string, string[]> SystemPromptFragments = new()
    {
        ["en"] = new[] {
            "You are a highly capable AI assistant",
            "You are currently operating in the context of a software development project",
            "Always act as Hyperion",
            "Focus on software engineering, system architecture, and automated development tasks",
            "List the actions you take clearly"
        },
        ["zh-CN"] = new[] {
            "你是高度进化的自主 AI 代理",
            "你目前运行在 AiChatApp 项目的上下文中",
            "你是 Hyperion",
            "专注于软件工程、系统架构 and 自动化开发任务",
            "根据需要自主规划 and 执行文件操作",
            "明确列出你执行的动作"
        },
        ["zh-TW"] = new[] {
            "你是高度進化的自主 AI 代理",
            "你目前運行在 AiChatApp 專案的上下文中",
            "你是 Hyperion",
            "專注於軟體工程、系統架構 and 自動化開發任務",
            "根據需要自主規劃 and 執行文件操作",
            "明確列出你執行的動作"
        },
        ["ja"] = new[] {
            "あなたは高度なAIアシスタントです",
            "現在はソフトウェア開発プロジェクトのコンテキストで動作しています",
            "あなたはタスク分解の専門家",
            "あなたは実装の専門家",
            "あなたは評審の専門家",
            "あなたは評価の専門家"
        }
    };

    public static IEnumerable<string> GetFragments(string? lang = null)
    {
        var english = SystemPromptFragments["en"];
        if (string.IsNullOrEmpty(lang) || !SystemPromptFragments.ContainsKey(lang))
            return english;

        return english.Concat(SystemPromptFragments[lang]);
    }

    public static readonly string[] UniversalFragments = {
        "[MEMORY INSTRUCTION]",
        "[会話履歴]:",
        "[ユーザーの既知情報・長期記憶]:",
        "[相关的长期记忆]:",
        "MEMORY: key=value",
        "[ENVIRONMENTAL POLICIES & CONSTRAINTS]:",
        "[当前会话上下文]:",
        "[プロジェクト文脈]:",
        "[利用可能なエージェント役割]:",
        "[可用代理角色]:",
        "[有効なスキル指示]:",
        "[有效技能指示]:",
        "[追加スキル指示]:"
    };
}
