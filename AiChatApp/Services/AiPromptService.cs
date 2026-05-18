using System.Text;
using System.Text.RegularExpressions;
using AiChatApp.Data;
using AiChatApp.Models;
using AiChatApp.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AiChatApp.Services;

public class AiPromptService
{
    private readonly AppDbContext _db;
    private readonly MemorySearchService _memorySearch;
    private readonly SessionMemoryService _sessionMemory;
    private readonly IConfiguration _config;
    private readonly ILogger<AiPromptService> _logger;

    private static string? _cachedPolicies;
    private static readonly SemaphoreSlim _policiesCacheLock = new(1, 1);

    public static readonly string[] PromptPrefixes = {
        "System:", "User:", "Assistant:", "Context:", "History:",
        "Thought:", "Thinking:", "[会話履歴]:", "[ユーザーの既知情報・長期記憶]:",
        "[相关的长期记忆]:", "[当前会话上下文]:", "[会话上下文]:",
        "[追加スキル指示]:", "[MEMORY INSTRUCTION]:", "[ENVIRONMENTAL POLICIES & CONSTRAINTS]:",
        "--- Policy:", "Role:", "Persona:", "Input:", "Output:", "Prompt:", "Instructions:", "Response:", "Result:",
        "役割:", "指示:", "角色:", "任务:", "项目名:", "项目路径:", "プロジェクト名:", "ルートパス:",
        "[現在のアクティブエージェント]:", "[当前活跃代理]:", "[プロジェクト文脈]:", "[项目文脉]:",
        "[利用可能なエージェント役割]:", "[可用代理角色]:", "[有効なスキル指示]:", "[有效技能指示]:",
        "[System]:", "[User]:", "[Assistant]:", "[Context]:", "[History]:", "[Prompt]:", "[Instructions]:"
    };

    public static readonly string[] SystemPromptFragments = LocalizationRegistry.UniversalFragments
        .Concat(LocalizationRegistry.GetFragments("zh-CN"))
        .Concat(LocalizationRegistry.GetFragments("ja"))
        .ToArray();

    public AiPromptService(AppDbContext db, MemorySearchService memorySearch, 
        SessionMemoryService sessionMemory, IConfiguration config, ILogger<AiPromptService> logger)
    {
        _db = db;
        _memorySearch = memorySearch;
        _sessionMemory = sessionMemory;
        _config = config;
        _logger = logger;
    }

    public static void InvalidatePolicyCache() => _cachedPolicies = null;

    public string GetSystemPromptTemplate(string key, string fallback) => 
        _config[$"AiSettings:SystemPrompts:{key}"] ?? fallback;

    public string Truncate(string? text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return text ?? "";
        return text[..maxChars] + "... [truncated]";
    }

    public string TruncateMessage(string content, int maxLen = 1000) => 
        content.Length <= maxLen ? content : content[..maxLen] + "...";

    public string ResolveImageReferences(string prompt, string? workingDirectory)
    {
        try
        {
            var workingDir = workingDirectory ?? Directory.GetCurrentDirectory();
            var matches = Regex.Matches(prompt, @"(\b[\w\-\.]+\.(png|jpg|jpeg|webp|gif|bmp)\b)", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            var result = prompt;
            foreach (Match match in matches)
            {
                var fileName = match.Value;
                if (!fileName.StartsWith("@"))
                {
                    var fullPath = Path.Combine(workingDir, fileName);
                    if (File.Exists(fullPath))
                    {
                        result = result.Replace(fileName, "@" + fileName);
                    }
                }
            }
            return result;
        }
        catch { return prompt; }
    }

    public async Task<string> LoadPoliciesAsync(string? category = null)
    {
        if (_cachedPolicies != null && category == null) return _cachedPolicies;
        await _policiesCacheLock.WaitAsync();
        try {
            if (_cachedPolicies != null && category == null) return _cachedPolicies;
            var path = Path.Combine(AppContext.BaseDirectory, "pipelines", "policies");
            if (!Directory.Exists(path)) return _cachedPolicies = "";
            var files = Directory.GetFiles(path, "*.md");
            if (files.Length == 0) return _cachedPolicies = "";
            var sb = new StringBuilder("\n\n[ENVIRONMENTAL POLICIES & CONSTRAINTS]:\n");
            foreach (var file in files) { 
                var fileName = Path.GetFileNameWithoutExtension(file).ToLowerInvariant(); 
                if (category != null && !fileName.Contains(category.ToLowerInvariant())) continue; 
                var content = await File.ReadAllTextAsync(file); 
                sb.Append($"--- Policy: {Path.GetFileNameWithoutExtension(file)} ---\n{content}\n"); 
            }
            var result = sb.ToString(); if (category == null) _cachedPolicies = result; return result;
        } finally { _policiesCacheLock.Release(); }
    }

    public async Task<string> BuildSystemPromptAsync(string prompt, int userId, int? chatSessionId, string? agentRole, AgentProfile? selectedAgent = null, ChatSession? preloadedSession = null, bool lightDepth = false)
    {
        if (lightDepth) return GetSystemPromptTemplate(agentRole ?? "Assistant", "You are a helpful AI assistant.");
        
        var memoriesTask = _memorySearch.SearchAsync(prompt, userId);
        var policiesTask = LoadPoliciesAsync();

        async Task<(List<Skill> Skills, ChatSession? Session, string SessionMemoryContext)> LoadDbDataAsync()
        {
            var skillsResult = await _memorySearch.SearchSkillsAsync(prompt, userId, agentRole);
            ChatSession? sessionResult = preloadedSession;
            string sessionMemoryContextResult = "";
            if (chatSessionId.HasValue)
            {
                if (sessionResult == null) sessionResult = await _db.ChatSessions.Include(s => s.Project).ThenInclude(p => p!.Agents).FirstOrDefaultAsync(s => s.Id == chatSessionId.Value);
                sessionMemoryContextResult = await _sessionMemory.ReadAllAsContextAsync(chatSessionId.Value);
            }
            return (skillsResult, sessionResult, sessionMemoryContextResult);
        }

        var dbTask = LoadDbDataAsync();
        await Task.WhenAll(memoriesTask, policiesTask, dbTask);

        var (skills, session, sessionMemoryContext) = await dbTask;
        var memories = (await memoriesTask).Take(5);
        var policies = await policiesTask;
        var sb = new StringBuilder(GetSystemPromptTemplate("Default", "あなたは高度なAIアシスタントです。現在はソフトウェア開発プロジェクトのコンテキストで動作しています。"));
        sb.Append(policies);
        if (selectedAgent != null) sb.Append($"\n\n[現在のアクティブエージェント]:\n役割: {selectedAgent.RoleName}\n指示: {selectedAgent.SystemPrompt}");
        if (session?.Project != null) { 
            sb.Append($"\n\n[プロジェクト文脈]:\nプロジェクト名: {session.Project.Name}\nルートパス: {session.Project.RootPath}"); 
            if (session.Project.Agents.Any(a => a.IsActive)) { 
                sb.Append("\n\n[利用可能なエージェント役割]:\n"); 
                foreach (var agent in session.Project.Agents.Where(a => a.IsActive).Take(3)) { 
                    if (selectedAgent != null && agent.Id == selectedAgent.Id) continue; 
                    sb.Append($"- {agent.RoleName}: {TruncateMessage(agent.SystemPrompt, 100)}\n"); 
                } 
            } 
        }
        if (!string.IsNullOrEmpty(sessionMemoryContext)) sb.Append("\n\n" + sessionMemoryContext);
        if (memories.Any()) { sb.Append("\n\n[ユーザーの既知情報・長期記憶]:\n"); foreach (var m in memories) sb.Append($"- {m.Content}\n"); }
        if (skills.Any()) { sb.Append("\n\n[有效技能指示]:\n"); foreach (var s in skills) sb.Append($"- {s.Description}\n"); }
        if (chatSessionId.HasValue) sb.Append(GetSystemPromptTemplate("MemoryInstruction", "\n\n[MEMORY INSTRUCTION]: 重要な発見があれば \"MEMORY: key=value\" 形式で行末に出力してください。"));
        return sb.ToString();
    }

    public async Task<(string History, int MessageId)> LoadHistoryAndMessageIdAsync(int? chatSessionId, int limit = 5)
    {
        if (!chatSessionId.HasValue) return ("", 0);
        var msgs = await _db.Messages.Where(m => m.ChatSessionId == chatSessionId.Value).OrderByDescending(m => m.Timestamp).Take(limit).OrderBy(m => m.Timestamp).ToListAsync();
        if (!msgs.Any()) return ("", 0);
        var sb = new StringBuilder("[会話履歴]:\n"); foreach (var m in msgs) sb.Append($"{(m.IsAi ? "Assistant" : "User")}: {TruncateMessage(m.Content, 500)}\n");
        var latestUserMsgId = msgs.LastOrDefault(m => !m.IsAi)?.Id ?? 0; return (sb.ToString(), latestUserMsgId);
    }
}
