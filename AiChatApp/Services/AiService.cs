using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
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
    private readonly HeadroomCompressionService _headroom;

    public AiService(
        AppDbContext db,
        SkillManagerService skillManager,
        ICliExecutor cliExecutor,
        IConfiguration config,
        ILogger<AiService> logger,
        AiPromptService promptService,
        AiResponseProcessor responseProcessor,
        AiCollaborationService collaborationService,
        AssistantToolService toolService,
        HeadroomCompressionService headroom)
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
        _headroom = headroom;
    }

    public string DefaultProvider => _config["AiSettings:DefaultProvider"] ?? "antigravity";
    public string FallbackProvider => _config["AiSettings:FallbackProvider"] ?? "antigravity";

    public async Task<List<AgentDefinition>> GetAvailableAgentsAsync(int userId)
    {
        var skills = await _skillManager.GetAllSkillsAsync(userId);
        return skills.Select(s => new AgentDefinition(s.Name, s.DisplayName, s.Description, s.Prompt)).ToList();
    }

    public async Task<string> GetResponseAsync(string prompt, int userId, int? chatSessionId, string? provider = null, int? agentId = null)
    {
        var user = await _db.Users.FindAsync(userId);
        bool isAdmin = user?.IsAdmin ?? false;
        if (!isAdmin && IsCodeModificationRequest(prompt))
        {
            return "拒绝执行：检测到代码修改指示。由于您没有管理员权限，无法执行涉及代码修改的操作。\nAccess Denied: Code modification request detected. Since you do not have administrator permissions, operations involving code modifications cannot be executed.";
        }

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

        var compressed = await _headroom.CompressAsync(systemPrompt, history, targetProvider);
        var effectiveSystem = compressed?.CompressedSystemPrompt ?? systemPrompt;
        var effectiveHistory = compressed?.CompressedHistory;
        string effectivePrompt = string.IsNullOrEmpty(effectiveHistory) ? prompt : $"{effectiveHistory}\nUser: {prompt}";
        effectivePrompt = _promptService.ResolveImageReferences(effectivePrompt, workingDir);

        var sw = Stopwatch.StartNew();
        var result = await _cliExecutor.ExecuteAsync(effectivePrompt, targetProvider, effectiveSystem, prompt, workingDir);
        sw.Stop();

        await LogAgentStepAsync(messageId, agent?.RoleName ?? "Assistant", result.Model, targetProvider, systemPrompt ?? "Default Assistant", fullPrompt, result.Output, (int)sw.ElapsedMilliseconds, result.PromptTokens, result.CompletionTokens, result.TotalTokens);

        var toolResults = await _toolService.ExecuteToolCallsAsync(result.Output, userId);
        var cleanResponse = AssistantToolService.StripToolCalls(result.Output);
        var toolHtml = AssistantToolService.BuildResultsHtml(toolResults);
        return cleanResponse + (string.IsNullOrEmpty(toolHtml) ? "" : "\n" + toolHtml);
    }

    public async Task<(string Html, List<AgentStep> Steps)> CooperateAsync(string task, int userId, int messageId, int? chatSessionId, string? provider = null, List<string>? selectedAgentNames = null, Func<string, string, Task>? onStepComplete = null, CrewProcessType processType = CrewProcessType.Hierarchical)
    {
        var user = await _db.Users.FindAsync(userId);
        bool isAdmin = user?.IsAdmin ?? false;
        if (!isAdmin && IsCodeModificationRequest(task))
        {
            return ("拒绝执行：检测到代码修改指示。由于您没有管理员权限，无法执行涉及代码修改的操作。\nAccess Denied: Code modification request detected. Since you do not have administrator permissions, operations involving code modifications cannot be executed.", new List<AgentStep>());
        }

        var result = await _collaborationService.CooperateAsync(task, userId, messageId, chatSessionId, provider, selectedAgentNames, onStepComplete, processType);

        return result;
    }

    public async IAsyncEnumerable<string> GetResponseStreamAsync(string prompt, int userId, int? chatSessionId, string? provider = null, int? agentId = null)
    {
        var user = await _db.Users.FindAsync(userId);
        bool isAdmin = user?.IsAdmin ?? false;
        if (!isAdmin && IsCodeModificationRequest(prompt))
        {
            yield return "拒绝执行：检测到代码修改指示。由于您没有管理员权限，无法执行涉及代码修改的操作。\nAccess Denied: Code modification request detected. Since you do not have administrator permissions, operations involving code modifications cannot be executed.";
            yield break;
        }

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

        var compressed = await _headroom.CompressAsync(systemPrompt, history, targetProvider);
        var effectiveSystem = compressed?.CompressedSystemPrompt ?? systemPrompt;
        var effectiveHistory = compressed?.CompressedHistory;
        string effectivePrompt = string.IsNullOrEmpty(effectiveHistory) ? prompt : $"{effectiveHistory}\nUser: {prompt}";
        effectivePrompt = _promptService.ResolveImageReferences(effectivePrompt, workingDir);

        var sw = Stopwatch.StartNew();
        var fullResponse = new StringBuilder();
        string? extractedModel = null;
        int pt = 0, ct = 0, tt = 0;

        var prefixBuffer = new StringBuilder();
        bool prefixHandled = false;
        const int maxPrefixBuffer = 16384;
        var dynamicFrags = _responseProcessor.BuildDynamicFragments(effectiveSystem, effectivePrompt);

        var filter = new WorkSummaryStreamFilter();

        await foreach (var chunk in _cliExecutor.ExecuteStreamAsync(effectivePrompt, targetProvider, effectiveSystem, prompt, workingDir))
        {
            if (chunk.Text != null)
            {
                fullResponse.Append(chunk.Text);
                var (toYield, handled) = _responseProcessor.HandlePrefixBuffer(prefixBuffer, chunk.Text, prefixHandled, maxPrefixBuffer, dynamicFrags);
                prefixHandled = handled;
                if (toYield != null)
                {
                    var filtered = filter.ProcessChunk(toYield);
                    if (filtered != null) yield return filtered;
                }
            }
            if (chunk.Model != null) extractedModel = chunk.Model;
            if (chunk.PromptTokens > 0) pt = chunk.PromptTokens;
            if (chunk.CompletionTokens > 0) ct = chunk.CompletionTokens;
            if (chunk.TotalTokens > 0) tt = chunk.TotalTokens;
        }

        if (!prefixHandled && prefixBuffer.Length > 0)
        {
            var stripped = _responseProcessor.StripEchoedPromptPrefix(prefixBuffer.ToString(), dynamicFrags);
            if (!string.IsNullOrEmpty(stripped))
            {
                var filtered = filter.ProcessChunk(stripped);
                if (filtered != null) yield return filtered;
            }
        }

        var flushed = filter.Flush();
        if (flushed != null) yield return flushed;

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
        if (userId.HasValue)
        {
            var user = await _db.Users.FindAsync(userId.Value);
            bool isAdmin = user?.IsAdmin ?? false;
            if (!isAdmin && IsCodeModificationRequest(prompt))
            {
                return "拒绝执行：检测到代码修改指示。由于您没有管理员权限，无法执行涉及代码修改的操作。\nAccess Denied: Code modification request detected. Since you do not have administrator permissions, operations involving code modifications cannot be executed.";
            }
        }

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

    public async Task<string> GetPlanAsync(string userMessage, string systemPrompt, int userId, int? chatSessionId, string? provider = null)
    {
        var targetProvider = provider ?? DefaultProvider;
        var planPrompt = $@"You are a planning agent. Your task is to analyze the user's request and output a structured plan in JSON format ONLY. Do NOT execute any changes. Do NOT output anything except the JSON plan.

User request: {userMessage}

Output a plan in this exact JSON format:
{{
  ""title"": ""Brief description of the task"",
  ""steps"": [
    {{""action"": ""Read"", ""target"": ""file path"", ""reason"": ""why this step is needed""}},
    {{""action"": ""Edit"", ""target"": ""file path"", ""lines"": ""line range"", ""change"": ""description of change""}},
    {{""action"": ""Bash"", ""command"": ""command to run"", ""reason"": ""why this command is needed""}}
  ],
  ""estimated_changes"": ""~X lines"",
  ""risk"": ""low|medium|high""
}}";
        var result = await _cliExecutor.ExecuteAsync(planPrompt, targetProvider, systemPrompt: systemPrompt, userPrompt: userMessage);
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

    private static readonly Regex CodeModRegex = new(
        @"\b(write|modify|edit|save|update|change|create|rewrite|overwrite|delete|remove|patch|fix|refactor|add|inject|insert)\b.*\b(code|file|script|program|class|function|struct|method|repo|repository|database|schema|project)\b" +
        @"|修改(代码|文件|配置|脚本|项目)|编辑(代码|文件|脚本)|写入(代码|文件|脚本)|更新(代码|文件|配置)|创建(代码|文件|脚本)|保存到(代码|文件)|替换(代码|文件)|删除(代码|文件)|重构(代码|文件)|修复(代码|文件|bug|Bug)" +
        @"|(コード|ファイル|スクリプト|クラス|関数|メソッド|レポ|リポジトリ|データベース|スキーマ|プロジェクト).*(書き込み|変更|修正|編集|作成|保存|削除|上書き|パッチ|リファクタリング)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex FileExtensionRegex = new(
        @"\b\w+\.(cs|js|ts|html|css|json|py|sh|sql|yml|yaml|md|csproj|sln)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static bool IsCodeModificationRequest(string prompt)
    {
        if (string.IsNullOrEmpty(prompt)) return false;

        // 1. 直接触发核心代码修改正则
        if (CodeModRegex.IsMatch(prompt)) return true;

        // 2. 如果包含文件后缀，且同时包含修改意图词
        if (FileExtensionRegex.IsMatch(prompt))
        {
            var lowercasePrompt = prompt.ToLower();
            string[] modificationKeywords = { 
                "write", "modify", "edit", "save", "update", "change", "create", "delete", "remove", "add", "patch", "fix", "refactor",
                "改", "写", "设", "增", "删", "修", "换", "替换", "更新", "保存", "添加",
                "書き", "変更", "修正", "編集", "作成", "保存", "削除", "追加" 
            };
            if (modificationKeywords.Any(keyword => lowercasePrompt.Contains(keyword)))
            {
                return true;
            }
        }

        return false;
    }
}

public class WorkSummaryStreamFilter
{
    private readonly StringBuilder _buffer = new();
    private bool _inFilterMode = false;
    private bool _summaryDetected = false;

    private string GetCleanBuffer()
    {
        var sb = new StringBuilder();
        int dashCount = 0;
        foreach (char c in _buffer.ToString())
        {
            if (char.IsWhiteSpace(c)) continue;
            if (c == '-')
            {
                dashCount++;
                if (dashCount <= 3) sb.Append('-');
            }
            else
            {
                dashCount = 0;
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static readonly string[] CleanSummaryHeaders = {
        "---**工作总结**", "---**工作サマリー**", "---**工作概要**",
        "---**SummaryofWork**", "---**TaskSummary**", "---**WorkSummary**", "---**Summary**",
        "---**工作总结", "---**工作サマリー", "---**工作概要",
        "---**SummaryofWork", "---**TaskSummary", "---**WorkSummary", "---**Summary",
        // Heading format (### 工作总结 without --- separator)
        "###工作总结", "###工作サマリー", "###工作概要",
        "###SummaryofWork", "###TaskSummary", "###WorkSummary", "###Summary",
        "##工作总结", "##工作サマリー", "##工作概要",
        "##SummaryofWork", "##TaskSummary", "##WorkSummary", "##Summary"
    };

    public string? ProcessChunk(string chunk)
    {
        if (_summaryDetected) return null;

        _buffer.Append(chunk);
        var bufStr = _buffer.ToString();

        if (!_inFilterMode)
        {
            int dashIdx = bufStr.IndexOf("---");
            if (dashIdx >= 0)
            {
                bool isStartingDashes = dashIdx == 0 || bufStr[dashIdx - 1] == '\n' || bufStr[dashIdx - 1] == '\r';
                if (isStartingDashes)
                {
                    _inFilterMode = true;
                    var toYield = bufStr[..dashIdx];
                    _buffer.Remove(0, dashIdx);
                    return toYield + ProcessChunk(string.Empty);
                }
            }

            // Also trigger on ## heading at line start (e.g., "### 工作总结")
            for (int i = 0; i < bufStr.Length; i++)
            {
                if (bufStr[i] == '#' && (i == 0 || bufStr[i - 1] == '\n' || bufStr[i - 1] == '\r'))
                {
                    _inFilterMode = true;
                    var toYield = bufStr[..i];
                    _buffer.Remove(0, i);
                    return toYield + ProcessChunk(string.Empty);
                }
            }

            int keepLen = 0;
            if (bufStr.EndsWith("---")) keepLen = 3;
            else if (bufStr.EndsWith("--")) keepLen = 2;
            else if (bufStr.EndsWith("-")) keepLen = 1;
            // Trailing # at line boundary — may be start of a heading
            else if (bufStr.Length >= 1 && bufStr[^1] == '#' && (bufStr.Length == 1 || bufStr[^2] == '\n' || bufStr[^2] == '\r'))
                keepLen = 1;
            else if (bufStr.Length >= 2 && bufStr[^1] == '#' && bufStr[^2] == '#' && (bufStr.Length == 2 || bufStr[^3] == '\n' || bufStr[^3] == '\r'))
                keepLen = 2;
            else if (bufStr.Length >= 3 && bufStr[^1] == '#' && bufStr[^2] == '#' && bufStr[^3] == '#' && (bufStr.Length == 3 || bufStr[^4] == '\n' || bufStr[^4] == '\r'))
                keepLen = 3;

            if (keepLen > 0)
            {
                int fragStart = bufStr.Length - keepLen;
                bool isStarting = fragStart == 0 || bufStr[fragStart - 1] == '\n' || bufStr[fragStart - 1] == '\r';
                if (isStarting)
                {
                    var toYield = bufStr[..fragStart];
                    _buffer.Remove(0, fragStart);
                    return toYield;
                }
            }

            _buffer.Clear();
            return bufStr;
        }
        else
        {
            var cleanBuf = GetCleanBuffer();

            bool isConfirmed = CleanSummaryHeaders.Any(h => cleanBuf.StartsWith(h, StringComparison.OrdinalIgnoreCase));
            if (isConfirmed)
            {
                _summaryDetected = true;
                _buffer.Clear();
                return null;
            }

            bool isPossible = cleanBuf == "-" || cleanBuf == "--" || cleanBuf == "---" || cleanBuf == "---*" || cleanBuf == "---**";
            if (!isPossible)
            {
                isPossible = CleanSummaryHeaders.Any(h => h.StartsWith(cleanBuf, StringComparison.OrdinalIgnoreCase));
            }

            if (isPossible)
            {
                if (_buffer.Length > 300)
                {
                    _inFilterMode = false;
                    var toYield = _buffer.ToString();
                    _buffer.Clear();
                    return toYield;
                }
                return null;
            }
            else
            {
                _inFilterMode = false;
                var toYield = _buffer.ToString();
                _buffer.Clear();
                return toYield;
            }
        }
    }

    public string? Flush()
    {
        if (_summaryDetected) return null;
        var toYield = _buffer.ToString();
        _buffer.Clear();
        return string.IsNullOrEmpty(toYield) ? null : toYield;
    }
}
