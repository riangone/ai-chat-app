using System.Text;
using AiChatApp.Models;
using AiChatApp.Services.Infrastructure;

namespace AiChatApp.Services;

public class PromptBuilder
{
    private readonly MemorySearchService _memorySearch;
    private readonly IConfiguration _config;

    public PromptBuilder(MemorySearchService memorySearch, IConfiguration config)
    {
        _memorySearch = memorySearch;
        _config = config;
    }

    private string GetSystemPromptTemplate(string key, string fallback) => 
        _config[$"AiSettings:SystemPrompts:{key}"] ?? fallback;

    public async Task<string> BuildSystemPromptAsync(string prompt, int userId, int? chatSessionId, string? agentRole, AgentProfile? selectedAgent = null, ChatSession? preloadedSession = null, AiService.ContextDepth depth = AiService.ContextDepth.Full, string? policies = "")
    {
        if (depth == AiService.ContextDepth.Light)
        {
            return GetSystemPromptTemplate(agentRole ?? "Assistant", "You are a helpful AI assistant.");
        }

        // Fire file-based tasks immediately
        var memoriesTask = depth == AiService.ContextDepth.Full ? _memorySearch.SearchAsync(prompt, userId) : Task.FromResult(new List<LongTermMemory>());
        var skillsTask = depth == AiService.ContextDepth.Full ? _memorySearch.SearchSkillsAsync(prompt, userId, agentRole) : Task.FromResult(new List<Skill>());

        ChatSession? session = preloadedSession;
        string sessionMemoryContext = "";
        if (chatSessionId.HasValue && session == null)
        {
            // In case not preloaded
            // Note: This still requires DB access, maybe pass DB or session memory service
        }

        var memories = await memoriesTask;
        var skills = await skillsTask;

        var sb = new StringBuilder(GetSystemPromptTemplate(agentRole ?? "Assistant", "You are a highly capable AI assistant."));

        if (!string.IsNullOrEmpty(policies))
        {
            sb.Append(policies);
        }

        if (depth == AiService.ContextDepth.Full)
        {
            if (memories.Any())
            {
                sb.Append("\n\n[相关的长期记忆]:\n");
                foreach (var m in memories) sb.Append($"- {m.Content}\n");
            }

            if (skills.Any())
            {
                sb.Append("\n\n[有效技能指示]:\n");
                foreach (var s in skills) sb.Append($"- {s.Description}\n");
            }
        }

        if (chatSessionId.HasValue)
            sb.Append(GetSystemPromptTemplate("MemoryInstruction", "\n\n[MEMORY INSTRUCTION]: 重要な発見があれば \"MEMORY: key=value\" 形式で行末に出力してください。"));

        return sb.ToString();
    }
}
