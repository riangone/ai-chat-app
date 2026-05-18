using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;
using AiChatApp.Data;
using AiChatApp.Models;
using AiChatApp.Services.Harness;
using Microsoft.EntityFrameworkCore;
using AiChatApp.Services.Infrastructure;

namespace AiChatApp.Services;

public class AiService
{
    public enum ContextDepth { Light, Standard, Full }

    private string Truncate(string? text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return text ?? "";
        return text[..maxChars] + "... [truncated]";
    }

    private string ResolveImageReferences(string prompt, string? workingDirectory)
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

    private readonly AppDbContext _db;
    private readonly MemorySearchService _memorySearch;
    private readonly SessionMemoryService _sessionMemory;
    private readonly IServiceProvider _serviceProvider;
    private readonly SkillManagerService _skillManager;
    private readonly PipelineLoaderService _pipelineLoader;
    private readonly SchemaValidationService _schemaValidator;
    private readonly ToolExecutorService _toolExecutor;
    private readonly EvalService _evalService;
    private readonly ICliExecutor _cliExecutor;
    private readonly IConfiguration _config;
    private readonly ILogger<AiService> _logger;

    private static string? _cachedPolicies;
    private static readonly SemaphoreSlim _policiesCacheLock = new(1, 1);

    public record AgentDefinition(string Name, string DisplayName, string Description, string SystemPrompt);

    private static readonly string[] PromptPrefixes = {
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

    private static readonly string[] SystemPromptFragments = LocalizationRegistry.UniversalFragments
        .Concat(LocalizationRegistry.GetFragments("zh-CN"))
        .Concat(LocalizationRegistry.GetFragments("ja"))
        .ToArray();

    private static readonly HashSet<string> PromptPrefixSet = new(PromptPrefixes, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> SystemPromptFragmentSet = new(SystemPromptFragments, StringComparer.OrdinalIgnoreCase);

    public static void InvalidatePolicyCache() => _cachedPolicies = null;

    public AiService(AppDbContext db, MemorySearchService memorySearch, 
        SessionMemoryService sessionMemory, IServiceProvider serviceProvider, 
        SkillManagerService skillManager, PipelineLoaderService pipelineLoader, 
        SchemaValidationService schemaValidator, ToolExecutorService toolExecutor,
        EvalService evalService, ICliExecutor cliExecutor, IConfiguration config, ILogger<AiService> logger)
    {
        _db = db;
        _memorySearch = memorySearch;
        _sessionMemory = sessionMemory;
        _serviceProvider = serviceProvider;
        _skillManager = skillManager;
        _pipelineLoader = pipelineLoader;
        _schemaValidator = schemaValidator;
        _toolExecutor = toolExecutor;
        _evalService = evalService;
        _cliExecutor = cliExecutor;
        _config = config;
        _logger = logger;
    }

    public string DefaultProvider => _config["AiSettings:DefaultProvider"] ?? "gemini";
    public string FallbackProvider => _config["AiSettings:FallbackProvider"] ?? "gemini";

    private string GetSystemPromptTemplate(string key, string fallback) => 
        _config[$"AiSettings:SystemPrompts:{key}"] ?? fallback;

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

        var (history, messageId) = await LoadHistoryAndMessageIdAsync(chatSessionId);
        string fullPrompt = string.IsNullOrEmpty(history) ? prompt : $"{history}\nUser: {prompt}";
        fullPrompt = ResolveImageReferences(fullPrompt, workingDir);

        var systemPrompt = await BuildSystemPromptAsync(fullPrompt, userId, chatSessionId, agent?.RoleName, agent, session);
        if (agent?.PreferredProvider != null) targetProvider = agent.PreferredProvider;

        var sw = Stopwatch.StartNew();
        var result = await _cliExecutor.ExecuteAsync(fullPrompt, targetProvider, systemPrompt, prompt, workingDir);
        sw.Stop();

        await LogAgentStepAsync(messageId, agent?.RoleName ?? "Assistant", result.Model, targetProvider, systemPrompt ?? "Default Assistant", fullPrompt, result.Output, (int)sw.ElapsedMilliseconds, result.PromptTokens, result.CompletionTokens, result.TotalTokens);
        return result.Output;
    }

    public async Task<(string Html, List<AgentStep> Steps)> CooperateAsync(string task, int userId, int messageId, int? chatSessionId, string? provider = null, List<string>? selectedAgentNames = null, Func<string, string, Task>? onStepComplete = null)
    {
        var targetProvider = provider ?? DefaultProvider;
        var workingDir = await GetProjectRootAsync(chatSessionId);
        task = ResolveImageReferences(task, workingDir);
        var steps = new List<AgentStep>();
        List<AgentDefinition> agentsToRun = new();
        ChatSession? session = null;

        if (chatSessionId.HasValue)
        {
            session = await _db.ChatSessions.Include(s => s.Project).ThenInclude(p => p!.Agents).FirstOrDefaultAsync(s => s.Id == chatSessionId.Value);
        }

        if (selectedAgentNames != null && selectedAgentNames.Any())
        {
            var allAvailable = await GetAvailableAgentsAsync(userId);
            foreach (var name in selectedAgentNames)
            {
                var def = allAvailable.FirstOrDefault(a => a.Name == name);
                if (def != null) agentsToRun.Add(def);
            }
        }
        else if (session?.Project?.Agents != null && session.Project.Agents.Any())
        {
            var activeAgents = session.Project.Agents.Where(a => a.IsActive).OrderBy(a => a.Id).ToList();
            agentsToRun = activeAgents.Select(a => new AgentDefinition(a.RoleName, a.RoleName, "DB Agent", a.SystemPrompt)).ToList();
        }

        if (agentsToRun.Any())
        {
            var sharedProjectRoot = await GetProjectRootAsync(chatSessionId);
            var sharedPolicies = await LoadPoliciesAsync();
            string lastOutput = "";
            foreach (var agent in agentsToRun)
            {
                string input = string.IsNullOrEmpty(lastOutput) ? task : $"Task: {task}\n\nPrevious Agent Output:\n{lastOutput}";
                var step = await RunAgentStepAsync(agent.Name, agent.SystemPrompt, input, messageId, targetProvider, userId, chatSessionId, workingDir: sharedProjectRoot, policies: sharedPolicies, session: session);
                var toolOutput = await _toolExecutor.ExecuteToolsAsync(step.Output, sharedProjectRoot);
                if (toolOutput != step.Output) { step.Output = toolOutput; await _db.SaveChangesAsync(); }

                _ = Task.Run(async () => {
                    try { await _evalService.EvaluateStepAsync(step.Id, task, step.Output, targetProvider); }
                    catch (Exception ex) { _logger.LogError(ex, "EvaluateStepAsync failed for step {StepId}", step.Id); }
                });

                steps.Add(step);
                lastOutput = step.Output;
                if (onStepComplete != null) await onStepComplete(step.Role, BuildStepHtml(step));
            }

            if (chatSessionId.HasValue)
            {
                _ = Task.Run(async () => {
                    try { await _sessionMemory.PromoteToLongTermAsync(chatSessionId.Value, userId); } 
                    catch (Exception ex) { _logger.LogError(ex, "PromoteToLongTerm failed for session {SessionId}", chatSessionId.Value); }
                });
            }
            return (BuildCooperativeHtml(steps, lastOutput), steps);
        }

        // Harness Engineering (Default Pipeline)
        var pipeline = _pipelineLoader.Get("default") ?? throw new Exception("Default pipeline not found.");
        string currentInput = task;
        string contextFromPreviousStages = "";
        OrchestratorPlan? activePlan = null;
        TaskBlackboard? activeBoard = null;
        var pipelineProjectRoot = await GetProjectRootAsync(chatSessionId);
        var pipelinePolicies = await LoadPoliciesAsync();
        List<AgentDefinition>? pipelineAgents = null;

        foreach (var stage in pipeline.Stages)
        {
            if (stage.IsOptional && string.IsNullOrEmpty(currentInput)) continue;
            if (activePlan != null && stage.Role != "reviewer") continue;

            AgentStep? stageStep = null;
            string stagePersona = !string.IsNullOrEmpty(stage.SystemPromptTemplate) ? await _pipelineLoader.GetPromptTemplateAsync(stage.SystemPromptTemplate) : stage.SystemPromptInline ?? "You are a helpful AI assistant.";
            if (stage.RetryOnQualityFail) stagePersona += "\n\nAfter your response, on a new line, include exactly [QUALITY_OK] if your response fully satisfies the task, or [QUALITY_FAIL] if it is incomplete or needs revision.";

            for (int attempt = 1; attempt <= stage.MaxAttempts; attempt++)
            {
                string combinedInput = string.IsNullOrEmpty(contextFromPreviousStages) ? currentInput : $"Task: {task}\n\nContext from previous stages:\n{contextFromPreviousStages}\n\nCurrent stage input: {currentInput}";
                stageStep = await RunAgentStepAsync(stage.Name, stagePersona, combinedInput, messageId, stage.Provider ?? targetProvider, userId, chatSessionId, attempt, pipelineProjectRoot, pipelinePolicies, session);
                var toolOutput = await _toolExecutor.ExecuteToolsAsync(stageStep.Output, pipelineProjectRoot);
                if (toolOutput != stageStep.Output) stageStep.Output = toolOutput;

                if (!string.IsNullOrEmpty(stage.OutputSchema))
                {
                    var validationResult = _schemaValidator.Validate($"{stage.OutputSchema}.json", stageStep.Output);
                    if (!validationResult.IsValid)
                    {
                        stageStep.WasAccepted = false;
                        await _db.SaveChangesAsync();
                        if (attempt < stage.MaxAttempts) { currentInput = _schemaValidator.GenerateCorrectivePrompt(validationResult.Errors, stageStep.Output); continue; }
                    }
                }

                if (stage.RetryOnQualityFail && attempt < stage.MaxAttempts)
                {
                    var qaMatch = Regex.Match(stageStep.Output, @"\[QUALITY_(OK|FAIL)\]", RegexOptions.None, TimeSpan.FromSeconds(1));
                    bool qualityOk = qaMatch.Success && qaMatch.Groups[1].Value == "OK";
                    stageStep.Output = Regex.Replace(stageStep.Output, @"\s*\[QUALITY_(OK|FAIL)\]\s*", " ", RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();
                    if (!qualityOk) { stageStep.WasAccepted = false; await _db.SaveChangesAsync(); continue; }
                }

                stageStep.WasAccepted = true;
                await _db.SaveChangesAsync();

                _ = Task.Run(async () => {
                    try { await _evalService.EvaluateStepAsync(stageStep.Id, task, stageStep.Output, stage.Provider ?? provider); }
                    catch (Exception ex) { _logger.LogError(ex, "EvaluateStepAsync failed for step {StepId}", stageStep.Id); }
                });
                break;
            }

            if (stageStep is null) continue;
            steps.Add(stageStep);
            if (onStepComplete != null) await onStepComplete(stageStep.Role, BuildStepHtml(stageStep));

            if (stage.Role == "orchestrator" && stageStep.WasAccepted)
            {
                var plan = TryParseOrchestratorPlan(stageStep.Output);
                if (plan != null)
                {
                    activePlan = plan;
                    pipelineAgents ??= await GetAvailableAgentsAsync(userId);
                    activeBoard = await ExecuteTaskGraphAsync(plan, task, messageId, targetProvider, userId, chatSessionId, onStepComplete, steps, pipelineAgents, session);
                    currentInput = BuildTaskGraphReviewInput(plan, activeBoard, task);
                    contextFromPreviousStages = $"Orchestrator plan:\n{stageStep.Output}";
                    continue;
                }
            }

            contextFromPreviousStages += $"\n--- Stage: {stage.Name} ---\n{Truncate(stageStep.Output, 2000)}\n";
            currentInput = stageStep.Output;
            if (stage.IsFinalStage) break;
        }

        if (!steps.Any()) return (BuildCooperativeHtml(new List<AgentStep>(), task), steps);

        // Revision logic
        if (activePlan != null && activeBoard != null)
        {
            var reviewerStep = steps.LastOrDefault(s => s.Role.Equals("Reviewer", StringComparison.OrdinalIgnoreCase));
            if (reviewerStep != null)
            {
                var feedback = TryParseReviewerFeedback(reviewerStep.Output);
                if (feedback?.OverallVerdict is "revision_needed" or "failed")
                {
                    var revised = await ReviseFailedSubtasksAsync(activePlan, feedback, activeBoard, messageId, targetProvider, userId, chatSessionId, onStepComplete, steps, pipelineAgents!, session);
                    if (revised)
                    {
                        var reviewPersona = await _pipelineLoader.GetPromptTemplateAsync("stage_reviewer.md");
                        var reviewInput = BuildTaskGraphReviewInput(activePlan, activeBoard, task);
                        var newReviewStep = await RunAgentStepAsync("Reviewer", reviewPersona, reviewInput, messageId, targetProvider, userId, chatSessionId, 2, pipelineProjectRoot, pipelinePolicies);
                        newReviewStep.WasAccepted = true;
                        await _db.SaveChangesAsync();
                        steps.Add(newReviewStep);
                        if (onStepComplete != null) await onStepComplete(newReviewStep.Role, BuildStepHtml(newReviewStep));
                    }
                }
            }
        }

        if (chatSessionId.HasValue)
        {
            _ = Task.Run(async () => {
                try { await _sessionMemory.PromoteToLongTermAsync(chatSessionId.Value, userId); } 
                catch (Exception ex) { _logger.LogError(ex, "PromoteToLongTermAsync failed for session {SessionId}", chatSessionId.Value); }
            });
        }
        return (BuildCooperativeHtml(steps, steps.Last().Output), steps);
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

        var (history, messageId) = await LoadHistoryAndMessageIdAsync(chatSessionId);
        var systemPrompt = await BuildSystemPromptAsync(prompt, userId, chatSessionId, agent?.RoleName, agent, session);
        string fullPrompt = string.IsNullOrEmpty(history) ? prompt : $"{history}\nUser: {prompt}";
        string processedPrompt = ResolveImageReferences(fullPrompt, workingDir);

        if (agent?.PreferredProvider != null) targetProvider = agent.PreferredProvider;

        var sw = Stopwatch.StartNew();
        var fullResponse = new StringBuilder();
        string? extractedModel = null;
        int pt = 0, ct = 0, tt = 0;

        var prefixBuffer = new StringBuilder();
        bool prefixHandled = false;
        const int maxPrefixBuffer = 16384;
        var dynamicFrags = BuildDynamicFragments(systemPrompt, fullPrompt);

        await foreach (var chunk in _cliExecutor.ExecuteStreamAsync(processedPrompt, targetProvider, systemPrompt, prompt, workingDir))
        {
            if (chunk.Text != null)
            {
                fullResponse.Append(chunk.Text);
                var (toYield, handled) = HandlePrefixBuffer(prefixBuffer, chunk.Text, prefixHandled, maxPrefixBuffer, dynamicFrags);
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
            var stripped = StripEchoedPromptPrefix(prefixBuffer.ToString(), dynamicFrags);
            if (!string.IsNullOrEmpty(stripped)) yield return stripped;
        }
        sw.Stop();

        if (fullResponse.Length == 0 && targetProvider != FallbackProvider)
        {
            await foreach (var chunk in GetResponseStreamAsync(prompt, userId, chatSessionId, FallbackProvider, agentId)) yield return chunk;
            yield break;
        }

        await LogAgentStepAsync(messageId, agent?.RoleName ?? "Assistant", extractedModel ?? targetProvider, targetProvider, systemPrompt ?? "Default Assistant", fullPrompt, fullResponse.ToString(), (int)sw.ElapsedMilliseconds, pt, ct, tt);
    }

    public async Task<string> ExecuteCliDirectAsync(string prompt, string provider, string? systemPrompt = null, string? workingDir = null)
    {
        var result = await _cliExecutor.ExecuteAsync(prompt, provider, systemPrompt, prompt, workingDir);
        return result.Output;
    }

    public async Task<string> ExecuteProactiveAgentAsync(ProactiveAgentProfile profile, string prompt, int? userId = null, int? chatSessionId = null, string? provider = null)
    {
        var targetProvider = provider ?? profile.PreferredProvider ?? DefaultProvider;
        var workingDir = await GetProjectRootAsync(chatSessionId);
        var sb = new StringBuilder(profile.SystemPrompt);

        if (profile.UseMemory && userId.HasValue)
        {
            var memoriesTask = _memorySearch.SearchAsync(prompt, userId.Value);
            var sessionMemTask = chatSessionId.HasValue ? _sessionMemory.ReadAllAsContextAsync(chatSessionId.Value) : Task.FromResult("");
            await Task.WhenAll(memoriesTask, sessionMemTask);

            var memories = memoriesTask.Result;
            if (memories.Any()) { sb.AppendLine("\n[相关的长期记忆]:"); foreach (var m in memories) sb.AppendLine($"- {m.Content}"); }
            var sessionMemoryContext = sessionMemTask.Result;
            if (!string.IsNullOrEmpty(sessionMemoryContext)) { sb.AppendLine("\n[当前会话上下文]:"); sb.AppendLine(sessionMemoryContext); }
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
        var result = await _cliExecutor.ExecuteAsync(prompt, targetProvider, systemPrompt: GetSystemPromptTemplate("TitleGenerator", "あなたはチャットタイトルの命名者です。簡潔で適切なタイトルのみを返します。"), userPrompt: prompt);
        return result.Output.Trim().Trim('"', '\'').Replace("\n", " ");
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

    private const int MaxStoredPersonaLength = 1000;
    private const int MaxStoredInputLength  = 2000;

    private async Task LogAgentStepAsync(int messageId, string role, string model, string provider, string persona, string input, string output, int durationMs, int promptTokens = 0, int completionTokens = 0, int totalTokens = 0)
    {
        if (messageId <= 0) return;
        var step = new AgentStep { MessageId = messageId, Role = role, Model = model, Provider = provider.ToLower(), PromptTokens = promptTokens, CompletionTokens = completionTokens, TotalTokens = totalTokens, Persona = TruncateMessage(persona ?? "Default Assistant", MaxStoredPersonaLength), Input = TruncateMessage(input, MaxStoredInputLength), Output = output, DurationMs = durationMs, WasAccepted = true, CreatedAt = DateTime.UtcNow };
        _db.AgentSteps.Add(step);
        await _db.SaveChangesAsync();
    }

    private async Task<AgentStep> RunAgentStepAsync(string role, string persona, string input, int messageId, string provider, int userId, int? chatSessionId = null, int attemptNumber = 1, string? workingDir = null, string? policies = null, ChatSession? session = null, IEnumerable<LongTermMemory>? sharedMemories = null, IEnumerable<Skill>? sharedSkills = null)
    {
        var roleSkillsTask = sharedSkills != null ? Task.FromResult(sharedSkills.ToList()) : _memorySearch.SearchSkillsAsync(input, userId, agentRole: role);
        var memoriesTask = sharedMemories != null ? Task.FromResult(sharedMemories.ToList()) : _memorySearch.SearchAsync(input, userId);
        workingDir ??= await GetProjectRootAsync(chatSessionId);
        policies ??= await LoadPoliciesAsync();
        var sb = new StringBuilder();
        if (session == null && chatSessionId.HasValue) session = await _db.ChatSessions.Include(s => s.Project).ThenInclude(p => p!.Agents).FirstOrDefaultAsync(s => s.Id == chatSessionId.Value);
        if (session != null) { var projectAgent = session.Project?.Agents.FirstOrDefault(a => a.RoleName.Equals(role, StringComparison.OrdinalIgnoreCase)); if (projectAgent != null) { sb.AppendLine(projectAgent.SystemPrompt); sb.AppendLine(); } }
        sb.AppendLine(persona); sb.AppendLine(policies);
        var memories = (await memoriesTask).Take(5);
        if (memories.Any()) { sb.AppendLine("\n[ユーザーの既知情報・長期記憶]:"); foreach (var m in memories) sb.AppendLine($"- {m.Content}"); }
        if (chatSessionId.HasValue) { var sessionMemoryContext = await _sessionMemory.ReadAllAsContextAsync(chatSessionId.Value); if (!string.IsNullOrEmpty(sessionMemoryContext)) { sb.AppendLine(); sb.AppendLine(sessionMemoryContext); } }
        if (attemptNumber == 1 && chatSessionId.HasValue) sb.AppendLine(GetSystemPromptTemplate("MemoryInstruction", "\n[MEMORY INSTRUCTION]: 重要な発見があれば \"MEMORY: key=value\" 形式で行末に出力してください。"));
        var roleSkills = await roleSkillsTask;
        if (roleSkills.Any()) { sb.AppendLine("\n[追加スキル指示]:"); sb.Append(string.Join("\n", roleSkills.Select(s => $"- {s.Description}"))); }
        string fullPersona = sb.ToString();
        var sw = Stopwatch.StartNew();
        var result = await _cliExecutor.ExecuteAsync(input, provider, fullPersona, input, workingDir, agentMode: true);
        sw.Stop();
        if (chatSessionId.HasValue) await ParseAndSaveMemoryAsync(chatSessionId.Value, role, result.Output);
        var step = new AgentStep { MessageId = messageId, Role = role, Model = string.IsNullOrEmpty(result.Model) ? provider : result.Model, Provider = provider.ToLower(), PromptTokens = result.PromptTokens, CompletionTokens = result.CompletionTokens, TotalTokens = result.TotalTokens, Persona = TruncateMessage(fullPersona, MaxStoredPersonaLength), Input = TruncateMessage(input, MaxStoredInputLength), Output = result.Output, AttemptNumber = attemptNumber, WasAccepted = true, DurationMs = (int)sw.ElapsedMilliseconds, CreatedAt = DateTime.UtcNow };
        _db.AgentSteps.Add(step);
        await _db.SaveChangesAsync();
        return step;
    }

    private async Task ParseAndSaveMemoryAsync(int sessionId, string role, string output)
    {
        if (!output.Contains("MEMORY:", StringComparison.OrdinalIgnoreCase)) return;
        foreach (var line in output.Split('\n')) { if (!line.Contains("MEMORY:", StringComparison.OrdinalIgnoreCase)) continue; var content = line[(line.IndexOf("MEMORY:", StringComparison.OrdinalIgnoreCase) + 7)..].Trim(); var parts = content.Split('=', 2); if (parts.Length == 2) await _sessionMemory.WriteAsync(sessionId, role, parts[0].Trim(), parts[1].Trim()); }
    }

    private async Task<string> LoadPoliciesAsync(string? category = null)
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
            foreach (var file in files) { var fileName = Path.GetFileNameWithoutExtension(file).ToLowerInvariant(); if (category != null && !fileName.Contains(category.ToLowerInvariant())) continue; var content = await File.ReadAllTextAsync(file); sb.Append($"--- Policy: {Path.GetFileNameWithoutExtension(file)} ---\n{content}\n"); }
            var result = sb.ToString(); if (category == null) _cachedPolicies = result; return result;
        } finally { _policiesCacheLock.Release(); }
    }

    private async Task<string> BuildSystemPromptAsync(string prompt, int userId, int? chatSessionId, string? agentRole, AgentProfile? selectedAgent = null, ChatSession? preloadedSession = null, ContextDepth depth = ContextDepth.Full)
    {
        if (depth == ContextDepth.Light) return GetSystemPromptTemplate(agentRole ?? "Assistant", "You are a helpful AI assistant.");
        var memoriesTask = depth == ContextDepth.Full ? _memorySearch.SearchAsync(prompt, userId) : Task.FromResult(new List<LongTermMemory>());
        var policiesTask = depth == ContextDepth.Full ? LoadPoliciesAsync() : Task.FromResult("");

        async Task<(List<Skill> Skills, ChatSession? Session, string SessionMemoryContext)> LoadDbDataAsync()
        {
            var skillsResult = depth == ContextDepth.Full ? await _memorySearch.SearchSkillsAsync(prompt, userId, agentRole) : new List<Skill>();
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
        if (session?.Project != null) { sb.Append($"\n\n[プロジェクト文脈]:\nプロジェクト名: {session.Project.Name}\nルートパス: {session.Project.RootPath}"); if (session.Project.Agents.Any(a => a.IsActive)) { sb.Append("\n\n[利用可能なエージェント役割]:\n"); foreach (var agent in session.Project.Agents.Where(a => a.IsActive).Take(3)) { if (selectedAgent != null && agent.Id == selectedAgent.Id) continue; sb.Append($"- {agent.RoleName}: {TruncateMessage(agent.SystemPrompt, 100)}\n"); } } }
        if (!string.IsNullOrEmpty(sessionMemoryContext)) sb.Append("\n\n" + sessionMemoryContext);
        if (memories.Any()) { sb.Append("\n\n[ユーザーの既知情報・長期記憶]:\n"); foreach (var m in memories) sb.Append($"- {m.Content}\n"); }
        if (skills.Any()) { sb.Append("\n\n[有效技能指示]:\n"); foreach (var s in skills) sb.Append($"- {s.Description}\n"); }
        if (chatSessionId.HasValue) sb.Append(GetSystemPromptTemplate("MemoryInstruction", "\n\n[MEMORY INSTRUCTION]: 重要な発見があれば \"MEMORY: key=value\" 形式で行末に出力してください。"));
        return sb.ToString();
    }

    private static string TruncateMessage(string content, int maxLen = 1000) => content.Length <= maxLen ? content : content[..maxLen] + "...";

    private async Task<(string History, int MessageId)> LoadHistoryAndMessageIdAsync(int? chatSessionId, int limit = 5)
    {
        if (!chatSessionId.HasValue) return ("", 0);
        var msgs = await _db.Messages.Where(m => m.ChatSessionId == chatSessionId.Value).OrderByDescending(m => m.Timestamp).Take(limit).OrderBy(m => m.Timestamp).ToListAsync();
        if (!msgs.Any()) return ("", 0);
        var sb = new StringBuilder("[会話履歴]:\n"); foreach (var m in msgs) sb.Append($"{(m.IsAi ? "Assistant" : "User")}: {TruncateMessage(m.Content, 500)}\n");
        var latestUserMsgId = msgs.LastOrDefault(m => !m.IsAi)?.Id ?? 0; return (sb.ToString(), latestUserMsgId);
    }

    private static string BuildStepHtml(AgentStep step)
    {
        string badgeClass = step.Role switch { "Orchestrator" => "badge-info", "Executor" => "badge-success", "Reviewer" => "badge-secondary", _ => "badge-ghost" };
        string retryBadge = step.AttemptNumber > 1 ? $"<span class='badge badge-warning badge-xs ml-1'>Retry #{step.AttemptNumber}</span>" : "";
        string acceptedIcon = step.WasAccepted ? "" : "<span class='badge badge-error badge-xs ml-1'>Rejected</span>";
        return $"<div class='collapse collapse-arrow bg-base-300/30 border border-base-content/10 mb-2'><input type='checkbox' /><div class='collapse-title text-sm font-medium flex items-center gap-2'><span class='badge {badgeClass} badge-sm'>{step.Role}</span>{retryBadge}{acceptedIcon}<span class='opacity-50 text-xs ml-auto'>{step.DurationMs}ms</span></div><div class='collapse-content text-xs opacity-80'><pre class='whitespace-pre-wrap font-sans'>{System.Net.WebUtility.HtmlEncode(step.Output)}</pre></div></div>";
    }

    private static string BuildCooperativeHtml(List<AgentStep> steps, string finalOutput) => System.Net.WebUtility.HtmlEncode(finalOutput);

    private static HashSet<string> BuildDynamicFragments(string? systemPrompt, string? userPrompt = null)
    {
        var dynamicFragments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(systemPrompt)) foreach (var line in systemPrompt.Split('\n')) { var tl = line.Trim(); if (tl.Length > 3) dynamicFragments.Add(tl); }
        if (!string.IsNullOrEmpty(userPrompt)) foreach (var line in userPrompt.Split('\n')) { var tl = line.Trim(); if (tl.Length > 3) dynamicFragments.Add(tl); }
        return dynamicFragments;
    }

    private static bool BufStartsWithAny(string buf, HashSet<string> set) => set.Any(p => buf.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    private static bool AnyStartsWithBuf(string buf, HashSet<string> set) => set.Any(p => p.StartsWith(buf, StringComparison.OrdinalIgnoreCase));

    private static (string? toYield, bool handled) HandlePrefixBuffer(StringBuilder prefixBuffer, string chunk, bool prefixHandled, int maxBuffer, HashSet<string>? dynamicFrags = null)
    {
        if (prefixHandled) return (chunk, true);
        prefixBuffer.Append(chunk); var buf = prefixBuffer.ToString();
        bool startsWithPrefix = BufStartsWithAny(buf, PromptPrefixSet) || BufStartsWithAny(buf, SystemPromptFragmentSet) || (dynamicFrags != null && dynamicFrags.Any(f => buf.StartsWith(f, StringComparison.OrdinalIgnoreCase)));
        bool couldBeginPrefix = AnyStartsWithBuf(buf, PromptPrefixSet) || AnyStartsWithBuf(buf, SystemPromptFragmentSet) || (dynamicFrags != null && dynamicFrags.Any(f => f.StartsWith(buf, StringComparison.OrdinalIgnoreCase)));
        if (!startsWithPrefix && !couldBeginPrefix) { var stripped = StripEchoedPromptPrefix(buf, dynamicFrags); prefixBuffer.Clear(); return (string.IsNullOrEmpty(stripped) ? null : stripped, true); }
        if (startsWithPrefix && (buf.Contains("\nUser:") || buf.Contains("\nAssistant:") || buf.Contains("\nAssistant ") || buf.Contains("\n["))) { var stripped = StripEchoedPromptPrefix(buf, dynamicFrags); prefixBuffer.Clear(); return (string.IsNullOrEmpty(stripped) ? null : stripped, true); }
        if (buf.Length >= maxBuffer) { var stripped = StripEchoedPromptPrefix(buf, dynamicFrags); prefixBuffer.Clear(); return (string.IsNullOrEmpty(stripped) ? null : stripped, true); }
        return (null, false);
    }

    private static string StripEchoedPromptPrefix(string text, HashSet<string>? dynamicFragments = null)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var lines = text.Split('\n'); int firstContentLine = -1; string? firstLineContent = null;
        for (int i = 0; i < Math.Min(lines.Length, 50); i++) {
            var trimmedLine = lines[i].Trim(); if (string.IsNullOrWhiteSpace(trimmedLine)) continue;
            string? matchedPrefix = PromptPrefixSet.FirstOrDefault(p => trimmedLine.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            bool isSystemFragment = SystemPromptFragmentSet.Any(f => trimmedLine.Contains(f, StringComparison.OrdinalIgnoreCase)) || (dynamicFragments != null && dynamicFragments.Contains(trimmedLine));
            if (matchedPrefix != null) { if (dynamicFragments != null && dynamicFragments.Contains(trimmedLine)) continue; var contentAfter = trimmedLine[matchedPrefix!.Length..].Trim(); if (!string.IsNullOrEmpty(contentAfter)) { if (SystemPromptFragmentSet.Any(f => contentAfter.Contains(f, StringComparison.OrdinalIgnoreCase)) || (dynamicFragments != null && dynamicFragments.Any(f => contentAfter.Contains(f, StringComparison.OrdinalIgnoreCase)))) continue; firstContentLine = i; firstLineContent = contentAfter; break; } continue; }
            if (isSystemFragment) continue;
            firstContentLine = i; firstLineContent = lines[i]; break;
        }
        if (firstContentLine != -1) { var remainingLines = lines.Skip(firstContentLine + 1).ToList(); return (firstLineContent + (remainingLines.Any() ? "\n" + string.Join("\n", remainingLines) : "")).Trim(); }
        return string.Empty;
    }

    private record SubtaskDef(string Id, string Title, string Agent, string Task, string ExpectedOutput, List<string> Deps);
    private record OrchestratorPlan(string Goal, List<SubtaskDef> Subtasks, string ExecutionNote);
    private record BlackboardArtifact(string SubtaskId, string AgentRole, string Content, string ArtifactType, int RevisionNumber, DateTime CreatedAt);
    private sealed class TaskBlackboard { private readonly Dictionary<string, BlackboardArtifact> _store = new(); public void Write(string subtaskId, string agentRole, string content, string artifactType = "text", int revision = 0) => _store[subtaskId] = new BlackboardArtifact(subtaskId, agentRole, content, artifactType, revision, DateTime.UtcNow); public BlackboardArtifact? Read(string subtaskId) => _store.TryGetValue(subtaskId, out var a) ? a : null; public string BuildDepContext(List<string> deps, List<SubtaskDef> allSubtasks) { if (!deps.Any()) return ""; var sb = new StringBuilder("\n## Context from upstream tasks:"); foreach (var dep in deps) { var artifact = Read(dep); if (artifact == null) continue; var def = allSubtasks.FirstOrDefault(s => s.Id == dep); sb.AppendLine($"\n### [{dep}] {def?.Title ?? dep} (by {artifact.AgentRole}):"); sb.AppendLine(artifact.Content); } return sb.ToString(); } }
    private record ReviewIssue(string Severity, string Description, string Suggestion);
    private record SubtaskReview(string SubtaskId, string Verdict, double Score, List<ReviewIssue> Issues);
    private record ReviewerFeedback(string OverallVerdict, double FinalScore, List<SubtaskReview> SubtaskReviews, string Summary);

    private OrchestratorPlan? TryParseOrchestratorPlan(string output) {
        var json = JsonUtils.ExtractJson(output); if (string.IsNullOrEmpty(json)) return null;
        try {
            using var doc = JsonDocument.Parse(json); var root = doc.RootElement; if (!root.TryGetProperty("subtasks", out var stEl)) return null;
            var subtasks = new List<SubtaskDef>(); foreach (var st in stEl.EnumerateArray()) { var id = st.TryGetProperty("id", out var v) ? v.GetString() ?? "" : ""; var title = st.TryGetProperty("title", out v) ? v.GetString() ?? id : id; var agent = st.TryGetProperty("agent", out v) ? v.GetString() ?? "Executor" : "Executor"; var task = st.TryGetProperty("task", out v) ? v.GetString() ?? "" : ""; var exp = st.TryGetProperty("expectedOutput", out v) ? v.GetString() ?? "" : ""; var deps = new List<string>(); if (st.TryGetProperty("deps", out var depsEl)) foreach (var d in depsEl.EnumerateArray()) deps.Add(d.GetString() ?? ""); if (!string.IsNullOrEmpty(id)) subtasks.Add(new SubtaskDef(id, title, agent, task, exp, deps)); }
            if (!subtasks.Any()) return null; var goal = root.TryGetProperty("goal", out var gv) ? gv.GetString() ?? "" : ""; var note = root.TryGetProperty("executionNote", out var nv) ? nv.GetString() ?? "" : ""; return new OrchestratorPlan(goal, subtasks, note);
        } catch { return null; }
    }

    private ReviewerFeedback? TryParseReviewerFeedback(string output) {
        var json = JsonUtils.ExtractJson(output); if (string.IsNullOrEmpty(json)) return null;
        try {
            using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
            var verdict = root.TryGetProperty("overallVerdict", out var vv) ? vv.GetString() ?? "" : ""; var score = root.TryGetProperty("finalScore", out var sv) ? sv.GetDouble() : 0.0; var summary = root.TryGetProperty("summary", out var sumv) ? sumv.GetString() ?? "" : ""; var reviews = new List<SubtaskReview>();
            if (root.TryGetProperty("subtaskReviews", out var revEl)) { foreach (var r in revEl.EnumerateArray()) { var stId = r.TryGetProperty("subtaskId", out var idv) ? idv.GetString() ?? "" : ""; var stVerdict = r.TryGetProperty("verdict", out var rdv) ? rdv.GetString() ?? "" : ""; var stScore = r.TryGetProperty("score", out var scv) ? scv.GetDouble() : 0.0; var issues = new List<ReviewIssue>(); if (r.TryGetProperty("issues", out var issEl)) foreach (var iss in issEl.EnumerateArray()) issues.Add(new ReviewIssue(iss.TryGetProperty("severity", out var sev) ? sev.GetString() ?? "" : "", iss.TryGetProperty("description", out var dsc) ? dsc.GetString() ?? "" : "", iss.TryGetProperty("suggestion", out var sug) ? sug.GetString() ?? "" : "")); if (!string.IsNullOrEmpty(stId)) reviews.Add(new SubtaskReview(stId, stVerdict, stScore, issues)); } }
            return new ReviewerFeedback(verdict, score, reviews, summary);
        } catch { return null; }
    }

    private static List<List<SubtaskDef>> TopologicalLayers(List<SubtaskDef> subtasks) { var remaining = subtasks.ToList(); var completed = new HashSet<string>(); var layers = new List<List<SubtaskDef>>(); while (remaining.Count > 0) { var ready = remaining.Where(s => s.Deps.All(d => completed.Contains(d))).ToList(); if (!ready.Any()) break; layers.Add(ready); foreach (var s in ready) { remaining.Remove(s); completed.Add(s.Id); } } return layers; }
    private static HashSet<string> GetSubtasksToRevise(List<SubtaskDef> all, HashSet<string> failed) { var toRevise = new HashSet<string>(failed); bool changed = true; while (changed) { changed = false; foreach (var st in all) if (!toRevise.Contains(st.Id) && st.Deps.Any(d => toRevise.Contains(d))) { toRevise.Add(st.Id); changed = true; } } return toRevise; }
    private string BuildAgentInput(SubtaskDef subtask, TaskBlackboard board, OrchestratorPlan plan, string? reviewerIssues = null) { var sb = new StringBuilder(); sb.AppendLine($"## Your Assignment: {subtask.Title}"); sb.AppendLine($"**Task:** {subtask.Task}"); sb.AppendLine($"**Expected Output:** {subtask.ExpectedOutput}"); sb.Append(board.BuildDepContext(subtask.Deps, plan.Subtasks)); if (!string.IsNullOrEmpty(reviewerIssues)) { sb.AppendLine("\n## Revision Required — Reviewer Feedback:"); sb.AppendLine(reviewerIssues); sb.AppendLine("\nPlease address ALL issues above in your revised output."); } return sb.ToString(); }

    private async Task RunSubtaskLayerAsync(List<SubtaskDef> layer, OrchestratorPlan plan, TaskBlackboard board, int messageId, string provider, int userId, int? chatSessionId, Func<string, string, Task>? onStepComplete, List<AgentStep> steps, List<AgentDefinition> allAgents, string? projectRoot, Dictionary<string, string>? reviewerIssuesPerTask = null, int revisionNumber = 0, ChatSession? session = null) {
        var policiesTask = LoadPoliciesAsync(); var memoriesTask = _memorySearch.SearchAsync(plan.Goal, userId); var skillsTask = _memorySearch.SearchSkillsAsync(plan.Goal, userId);
        var policies = await policiesTask; var sharedMemories = await memoriesTask; var sharedSkills = await skillsTask;
        var layerTasks = layer.Select(async subtask => {
            var agentDef = allAgents.FirstOrDefault(a => a.Name.Equals(subtask.Agent, StringComparison.OrdinalIgnoreCase)) ?? new AgentDefinition(subtask.Agent, subtask.Agent, "", "You are a helpful AI assistant.");
            string? issues = null; reviewerIssuesPerTask?.TryGetValue(subtask.Id, out issues); var input = BuildAgentInput(subtask, board, plan, issues);
            var step = await RunAgentStepAsync(agentDef.Name, agentDef.SystemPrompt, input, messageId, provider, userId, chatSessionId, revisionNumber + 1, projectRoot, policies, session, sharedMemories, sharedSkills);
            var toolOut = await _toolExecutor.ExecuteToolsAsync(step.Output, projectRoot); if (toolOut != step.Output) { step.Output = toolOut; await _db.SaveChangesAsync(); }
            board.Write(subtask.Id, agentDef.Name, step.Output, revision: revisionNumber);
            _ = Task.Run(async () => { try { await _evalService.EvaluateStepAsync(step.Id, subtask.Task, step.Output, provider); } catch (Exception ex) { _logger.LogError(ex, "EvaluateStepAsync failed for step {StepId}", step.Id); } });
            return (subtask.Id, step);
        }).ToList();
        var results = await Task.WhenAll(layerTasks);
        foreach (var (_, step) in results) { steps.Add(step); if (onStepComplete != null) await onStepComplete(step.Role, BuildStepHtml(step)); }
    }

    private async Task<TaskBlackboard> ExecuteTaskGraphAsync(OrchestratorPlan plan, string originalTask, int messageId, string defaultProvider, int userId, int? chatSessionId, Func<string, string, Task>? onStepComplete, List<AgentStep> steps, List<AgentDefinition> allAgents, ChatSession? session = null) {
        var projectRoot = await GetProjectRootAsync(chatSessionId); var board = new TaskBlackboard(); var layers = TopologicalLayers(plan.Subtasks);
        foreach (var layer in layers) await RunSubtaskLayerAsync(layer, plan, board, messageId, defaultProvider, userId, chatSessionId, onStepComplete, steps, allAgents, projectRoot, session: session);
        return board;
    }

    private async Task<bool> ReviseFailedSubtasksAsync(OrchestratorPlan plan, ReviewerFeedback feedback, TaskBlackboard board, int messageId, string defaultProvider, int userId, int? chatSessionId, Func<string, string, Task>? onStepComplete, List<AgentStep> steps, List<AgentDefinition> allAgents, ChatSession? session = null) {
        var failedIds = feedback.SubtaskReviews.Where(r => r.Verdict is "revision_needed" or "failed").Select(r => r.SubtaskId).ToHashSet(); if (!failedIds.Any()) return false;
        var toRevise = GetSubtasksToRevise(plan.Subtasks, failedIds);
        var issuesMap = feedback.SubtaskReviews.Where(r => toRevise.Contains(r.SubtaskId) && r.Issues.Any()).ToDictionary(r => r.SubtaskId, r => string.Join("\n", r.Issues.Select(i => $"- [{i.Severity.ToUpper()}] {i.Description}\n  → Suggestion: {i.Suggestion}")));
        var subtasksToRevise = plan.Subtasks.Where(s => toRevise.Contains(s.Id)).ToList(); var layers = TopologicalLayers(subtasksToRevise); var projectRoot = await GetProjectRootAsync(chatSessionId);
        _logger.LogInformation("Directing revision for {Count} subtasks: {Ids}", toRevise.Count, string.Join(", ", toRevise));
        foreach (var layer in layers) await RunSubtaskLayerAsync(layer, plan, board, messageId, defaultProvider, userId, chatSessionId, onStepComplete, steps, allAgents, projectRoot, issuesMap, 1, session);
        return true;
    }

    private string BuildTaskGraphReviewInput(OrchestratorPlan plan, TaskBlackboard board, string originalTask) {
        var sb = new StringBuilder(); sb.AppendLine($"## Original Task\n{originalTask}"); sb.AppendLine($"\n## Orchestrator Goal\n{plan.Goal}");
        if (!string.IsNullOrEmpty(plan.ExecutionNote)) sb.AppendLine($"\n## Execution Note\n{plan.ExecutionNote}");
        sb.AppendLine("\n## Subtask Results");
        foreach (var subtask in plan.Subtasks) { var artifact = board.Read(subtask.Id); sb.AppendLine($"\n### [{subtask.Id}] {subtask.Title} (agent: {subtask.Agent})"); sb.AppendLine($"**Expected:** {subtask.ExpectedOutput}"); if (artifact != null && artifact.RevisionNumber > 0) sb.AppendLine($"**(Revision {artifact.RevisionNumber})**"); sb.AppendLine($"**Output:**\n{artifact?.Content ?? "(no output)"}"); }
        return sb.ToString();
    }
}
