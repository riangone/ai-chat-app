using System.Diagnostics;
using System.Text;
using AiChatApp.Data;
using AiChatApp.Models;
using AiChatApp.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AiChatApp.Services;

public class AiService
{
    private readonly AppDbContext _db;
    private readonly SkillManagerService _skillManager;
    private readonly ICliExecutor _cliExecutor;
    private readonly IConfiguration _config;
    private readonly ILogger<AiService> _logger;

    private readonly AiPromptService _promptService;
    private readonly AiResponseProcessor _responseProcessor;
    private readonly AiCollaborationService _collaborationService;
    private readonly AssistantToolService _toolService;

    public AiService(
        AppDbContext db,
        SkillManagerService skillManager,
        ICliExecutor cliExecutor,
        IConfiguration config,
        ILogger<AiService> logger,
        AiPromptService promptService,
        AiResponseProcessor responseProcessor,
        AiCollaborationService collaborationService,
        AssistantToolService toolService)
    {
        _db = db;
        _skillManager = skillManager;
        _cliExecutor = cliExecutor;
        _config = config;
        _logger = logger;
        _promptService = promptService;
        _responseProcessor = responseProcessor;
        _collaborationService = collaborationService;
        _toolService = toolService;
    }

    public string DefaultProvider => _config["AiSettings:DefaultProvider"] ?? "gemini";
    public string FallbackProvider => _config["AiSettings:FallbackProvider"] ?? "gemini";

    public async Task<List<AgentDefinition>> GetAvailableAgentsAsync(int userId)
    {
        var skills = await _skillManager.GetAllSkillsAsync(userId);
        return skills.Select(s => new AgentDefinition(s.Name, s.DisplayName, s.Description, s.Prompt)).ToList();
    }

    public async Task<string> GetResponseAsync(string prompt, int userId, int? chatSessionId, string? provider = null, int? agentId = null)
    {
        var targetProvider = string.IsNullOrWhiteSpace(provider) ? DefaultProvider : provider;
        AgentProfile? agent = agentId.HasValue ? await _db.AgentProfiles.FindAsync(agentId.Value) : null;

        ChatSession? session = null;
        string? workingDir = null;
        if (chatSessionId.HasValue)
        {
            session = await _db.ChatSessions.Include(s => s.Project).ThenInclude(p => p!.Agents).FirstOrDefaultAsync(s => s.Id == chatSessionId.Value);
            workingDir = session?.Project?.RootPath;
        }

        var (history, messageId) = await _promptService.LoadHistoryAndMessageIdAsync(chatSessionId);
        string fullPrompt = string.IsNullOrEmpty(history) ? prompt : $"{history}\nUser: {prompt}";
        fullPrompt = _promptService.ResolveImageReferences(fullPrompt, workingDir);

        var systemPrompt = await _promptService.BuildSystemPromptAsync(fullPrompt, userId, chatSessionId, agent?.RoleName, agent, session);
        if (agent?.PreferredProvider != null) targetProvider = agent.PreferredProvider;

        var sw = Stopwatch.StartNew();
        var result = await _cliExecutor.ExecuteAsync(fullPrompt, targetProvider, systemPrompt, prompt, workingDir);
        sw.Stop();

        await LogAgentStepAsync(messageId, agent?.RoleName ?? "Assistant", result.Model, targetProvider, systemPrompt ?? "Default Assistant", fullPrompt, result.Output, (int)sw.ElapsedMilliseconds, result.PromptTokens, result.CompletionTokens, result.TotalTokens);

        var toolResults = await _toolService.ExecuteToolCallsAsync(result.Output, userId);
        var cleanResponse = AssistantToolService.StripToolCalls(result.Output);
        var toolHtml = AssistantToolService.BuildResultsHtml(toolResults);
        return cleanResponse + (string.IsNullOrEmpty(toolHtml) ? "" : "\n" + toolHtml);
    }

    public Task<(string Html, List<AgentStep> Steps)> CooperateAsync(string task, int userId, int messageId, int? chatSessionId, string? provider = null, List<string>? selectedAgentNames = null, Func<string, string, Task>? onStepComplete = null)
    {
        return _collaborationService.CooperateAsync(task, userId, messageId, chatSessionId, provider, selectedAgentNames, onStepComplete);
    }

    public async IAsyncEnumerable<string> GetResponseStreamAsync(string prompt, int userId, int? chatSessionId, string? provider = null, int? agentId = null)
    {
        var targetProvider = provider ?? DefaultProvider;
        AgentProfile? agent = agentId.HasValue ? await _db.AgentProfiles.FindAsync(agentId.Value) : null;

        ChatSession? session = null;
        string? workingDir = null;
        if (chatSessionId.HasValue)
        {
            session = await _db.ChatSessions.Include(s => s.Project).ThenInclude(p => p!.Agents).FirstOrDefaultAsync(s => s.Id == chatSessionId.Value);
            workingDir = session?.Project?.RootPath;
        }

        var (history, messageId) = await _promptService.LoadHistoryAndMessageIdAsync(chatSessionId);
        var systemPrompt = await _promptService.BuildSystemPromptAsync(prompt, userId, chatSessionId, agent?.RoleName, agent, session);
        string fullPrompt = string.IsNullOrEmpty(history) ? prompt : $"{history}\nUser: {prompt}";
        string processedPrompt = _promptService.ResolveImageReferences(fullPrompt, workingDir);

        if (agent?.PreferredProvider != null) targetProvider = agent.PreferredProvider;

        var sw = Stopwatch.StartNew();
        var fullResponse = new StringBuilder();
        string? extractedModel = null;
        int pt = 0, ct = 0, tt = 0;

        var prefixBuffer = new StringBuilder();
        bool prefixHandled = false;
        const int maxPrefixBuffer = 16384;
        var dynamicFrags = _responseProcessor.BuildDynamicFragments(systemPrompt, fullPrompt);

        await foreach (var chunk in _cliExecutor.ExecuteStreamAsync(processedPrompt, targetProvider, systemPrompt, prompt, workingDir))
        {
            if (chunk.Text != null)
            {
                fullResponse.Append(chunk.Text);
                var (toYield, handled) = _responseProcessor.HandlePrefixBuffer(prefixBuffer, chunk.Text, prefixHandled, maxPrefixBuffer, dynamicFrags);
                prefixHandled = handled;
                if (toYield != null) yield return toYield;
            }
            if (chunk.Model != null) extractedModel = chunk.Model;
            if (chunk.PromptTokens > 0) pt = chunk.PromptTokens;
            if (chunk.CompletionTokens > 0) ct = chunk.CompletionTokens;
            if (chunk.TotalTokens > 0) tt = chunk.TotalTokens;
        }

        if (!prefixHandled && prefixBuffer.Length > 0)
        {
            var stripped = _responseProcessor.StripEchoedPromptPrefix(prefixBuffer.ToString(), dynamicFrags);
            if (!string.IsNullOrEmpty(stripped)) yield return stripped;
        }
        sw.Stop();

        if (fullResponse.Length == 0 && targetProvider != FallbackProvider)
        {
            await foreach (var chunk in GetResponseStreamAsync(prompt, userId, chatSessionId, FallbackProvider, agentId)) yield return chunk;
            yield break;
        }

        await LogAgentStepAsync(messageId, agent?.RoleName ?? "Assistant", extractedModel ?? targetProvider, targetProvider, systemPrompt ?? "Default Assistant", fullPrompt, fullResponse.ToString(), (int)sw.ElapsedMilliseconds, pt, ct, tt);

        var toolResults = await _toolService.ExecuteToolCallsAsync(fullResponse.ToString(), userId);
        var toolHtml = AssistantToolService.BuildResultsHtml(toolResults);
        if (!string.IsNullOrEmpty(toolHtml)) yield return toolHtml;
    }

    public async Task<string> ExecuteCliDirectAsync(string prompt, string provider, string? systemPrompt = null, string? workingDir = null, string? outputFormat = null)
    {
        var result = await _cliExecutor.ExecuteAsync(prompt, provider, systemPrompt, prompt, workingDir, outputFormat: outputFormat);
        return result.Output;
    }

    public async Task<string> ExecuteProactiveAgentAsync(ProactiveAgentProfile profile, string prompt, int? userId = null, int? chatSessionId = null, string? provider = null)
    {
        var targetProvider = provider ?? profile.PreferredProvider ?? DefaultProvider;
        var workingDir = await GetProjectRootAsync(chatSessionId);
        var sb = new StringBuilder(profile.SystemPrompt);

        if (profile.UseMemory && userId.HasValue)
        {
            // Note: This logic could be moved to AiPromptService if reused
            var memories = await _promptService.BuildSystemPromptAsync(prompt, userId.Value, chatSessionId, null, lightDepth: false);
            sb.AppendLine(memories);
        }

        var sw = Stopwatch.StartNew();
        var result = await _cliExecutor.ExecuteAsync(prompt, targetProvider, sb.ToString(), prompt, workingDir, agentMode: true);
        sw.Stop();

        if (userId.HasValue && chatSessionId.HasValue)
        {
            int messageId = await GetLatestUserMessageIdAsync(chatSessionId);
            if (messageId > 0) await LogAgentStepAsync(messageId, profile.Role, result.Model, targetProvider, sb.ToString(), prompt, result.Output, (int)sw.ElapsedMilliseconds, result.PromptTokens, result.CompletionTokens, result.TotalTokens);
        }
        return result.Output;
    }

    public async Task<string> GenerateTitleAsync(string userPrompt, string aiResponse, string? provider = null)
    {
        var targetProvider = provider ?? DefaultProvider;
        string prompt = $"以下のやり取りに基づいて、チャットセッションの短いタイトルを生成してください。タイトルは5語以内、または15文字程度で、装飾なしのプレーンテキストのみを返してください。\n\nユーザー: {userPrompt}\nAI: {(aiResponse.Length > 200 ? aiResponse[..200] + "..." : aiResponse)}";
        var result = await _cliExecutor.ExecuteAsync(prompt, targetProvider, systemPrompt: _promptService.GetSystemPromptTemplate("TitleGenerator", "あなたはチャットタイトルの命名者です。簡潔で適切なタイトルのみを返します。"), userPrompt: prompt, outputFormat: "text");
        var title = result.Output.Trim().Trim('"', '\'').Replace("\n", " ");
        // Strip any JSON wrapping the AI might have returned despite instructions
        if (title.StartsWith("{") && title.EndsWith("}"))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(title);
                var root = doc.RootElement;
                if (root.TryGetProperty("title", out var t) || root.TryGetProperty("content", out t) || root.TryGetProperty("text", out t))
                    title = t.GetString()?.Trim().Trim('"', '\'') ?? title;
            }
            catch { }
        }
        return title;
    }

    private async Task<string?> GetProjectRootAsync(int? chatSessionId)
    {
        if (!chatSessionId.HasValue) return null;
        var session = await _db.ChatSessions.Include(s => s.Project).FirstOrDefaultAsync(s => s.Id == chatSessionId.Value);
        return session?.Project?.RootPath;
    }

    private async Task<int> GetLatestUserMessageIdAsync(int? chatSessionId)
    {
        if (!chatSessionId.HasValue) return 0;
        var lastMsg = await _db.Messages.Where(m => m.ChatSessionId == chatSessionId.Value && !m.IsAi).OrderByDescending(m => m.Id).FirstOrDefaultAsync();
        return lastMsg?.Id ?? 0;
    }

    private async Task LogAgentStepAsync(int messageId, string role, string model, string provider, string persona, string input, string output, int durationMs, int promptTokens = 0, int completionTokens = 0, int totalTokens = 0)
    {
        if (messageId <= 0) return;
        var step = new AgentStep { 
            MessageId = messageId, 
            Role = role, 
            Model = model, 
            Provider = provider.ToLower(), 
            PromptTokens = promptTokens, 
            CompletionTokens = completionTokens, 
            TotalTokens = totalTokens, 
            Persona = _promptService.TruncateMessage(persona ?? "Default Assistant", 1000), 
            Input = _promptService.TruncateMessage(input, 2000), 
            Output = output, 
            DurationMs = durationMs, 
            WasAccepted = true, 
            CreatedAt = DateTime.UtcNow 
        };
        _db.AgentSteps.Add(step);
        await _db.SaveChangesAsync();
    }
}
