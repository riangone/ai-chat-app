using System.Collections.Generic;

namespace AiChatApp.Models
{
    /// <summary>
    /// 定义主动式代理角色的配置，用于实现分层分析。
    /// 这里的命名是为了区别于数据库中的 AgentProfile 实体。
    /// </summary>
    public class ProactiveAgentProfile
    {
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty; // Sentinel, Summarizer, Brain
        public string SystemPrompt { get; set; } = string.Empty;
        public string? PreferredProvider { get; set; }
        public double Temperature { get; set; } = 0.7;
        public int? MaxTokens { get; set; }
        public bool UseMemory { get; set; } = true;
    }

    public static class ProactiveAgentProfiles
    {
        public static ProactiveAgentProfile Summarizer = new ProactiveAgentProfile
        {
            Name = "Summarizer",
            Role = "Summarizer",
            SystemPrompt = "你是一个高效的信息记录员。你的任务是从原始的 Git Diff、Todo 列表或文件变动中提取关键信息，并压缩成简短的摘要。不要进行推理，只记录事实。",
            Temperature = 0.3,
            MaxTokens = 500,
            UseMemory = false
        };

        public static ProactiveAgentProfile HyperionBrain = new ProactiveAgentProfile
        {
            Name = "Hyperion-Brain",
            Role = "Brain",
            SystemPrompt = "你是 Hyperion，一个高度进化的自主 AI 代理。基于提供的项目上下文摘要，你的任务是发现潜在的架构风险、提供优化建议或规划下一步行动。你的回答应该是专业、深入且具有前瞻性的。",
            Temperature = 0.8,
            MaxTokens = 2000,
            UseMemory = true
        };
    }
}
