using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;
using AiChatApp.Data;
using AiChatApp.Models;
using AiChatApp.Services.Harness;
using Microsoft.EntityFrameworkCore;

namespace AiChatApp.Services;

public class AiService
{
    private readonly AppDbContext _db;
    private readonly MemorySearchService _memorySearch;
    private readonly SessionMemoryService _sessionMemory;
    private readonly IServiceProvider _serviceProvider;
    private readonly SkillManagerService _skillManager;
    private readonly PipelineLoaderService _pipelineLoader;
    private readonly SchemaValidationService _schemaValidator;
    private readonly ToolExecutorService _toolExecutor;
    private readonly EvalService _evalService;
    private readonly IConfiguration _config;
    private SkillLearningService _skillLearning => _serviceProvider.GetRequiredService<SkillLearningService>();

    public record AgentDefinition(string Name, string DisplayName, string Description, string SystemPrompt);
    private record CliResult(string Output, string Model, int PromptTokens, int CompletionTokens, int TotalTokens);

    public AiService(AppDbContext db, MemorySearchService memorySearch, 
        SessionMemoryService sessionMemory, IServiceProvider serviceProvider, 
        SkillManagerService skillManager, PipelineLoaderService pipelineLoader, 
        SchemaValidationService schemaValidator, ToolExecutorService toolExecutor,
        EvalService evalService, IConfiguration config)
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
        _config = config;
    }

    public string DefaultProvider => _config["AiSettings:DefaultProvider"] ?? "gemini";
    public string FallbackProvider => _config["AiSettings:FallbackProvider"] ?? "gemini";
    public int TimeoutSeconds => int.TryParse(_config["AiSettings:TimeoutSeconds"], out var s) ? s : 300;

    private string GetSystemPromptTemplate(string key, string fallback) => 
        _config[$"AiSettings:SystemPrompts:{key}"] ?? fallback;

    /// <summary>获取所有可用的代理定义（统一从 SkillManager 获取）</summary>
    public async Task<List<AgentDefinition>> GetAvailableAgentsAsync(int userId)
    {
        var skills = await _skillManager.GetAllSkillsAsync();
        return skills.Select(s => new AgentDefinition(s.Name, s.DisplayName, s.Description, s.Prompt)).ToList();
    }

    private async Task<AgentDefinition?> LoadAgentFromDirAsync(string dirPath)
    {
        var skillFile = Path.Combine(dirPath, "SKILL.md");
        if (!File.Exists(skillFile)) return null;

        var content = await File.ReadAllTextAsync(skillFile);
        var name = Path.GetFileName(dirPath);
        var description = "";
        var systemPrompt = content;

        // 尝试解析 YAML Front Matter (简单实现)
        if (content.StartsWith("---"))
        {
            var endIdx = content.IndexOf("---", 3);
            if (endIdx > 0)
            {
                var yaml = content.Substring(3, endIdx - 3);
                var lines = yaml.Split('\n');
                foreach (var line in lines)
                {
                    if (line.StartsWith("name:")) name = line.Replace("name:", "").Trim();
                    if (line.StartsWith("description:")) description = line.Replace("description:", "").Trim();
                }
                systemPrompt = content.Substring(endIdx + 3).Trim();
            }
        }

        return new AgentDefinition(name, name, description, systemPrompt);
    }

    // ─────────────────────────────────────────
    // 公開API
    // ─────────────────────────────────────────

    /// <summary>通常チャット。会話履歴・記憶・スキルを注入してAIを呼び出す。</summary>
    public async Task<string> GetResponseAsync(
        string prompt, int userId, int? chatSessionId, string? provider = null, int? agentId = null)
    {
        var targetProvider = string.IsNullOrWhiteSpace(provider) ? DefaultProvider : provider;
        AgentProfile? agent = agentId.HasValue ? await _db.AgentProfiles.FindAsync(agentId.Value) : null;
        var systemPrompt = await BuildSystemPromptAsync(prompt, userId, chatSessionId, agent?.RoleName, agent);
        var workingDir = await GetProjectRootAsync(chatSessionId);
        var history = await BuildHistoryBlockAsync(chatSessionId, limit: 10);
        string fullPrompt = string.IsNullOrEmpty(history)
            ? prompt
            : $"{history}\nUser: {prompt}";

        if (agent?.PreferredProvider != null) targetProvider = agent.PreferredProvider;

        int messageId = await GetLatestUserMessageIdAsync(chatSessionId);

        var sw = Stopwatch.StartNew();
        var result = await ExecuteCliAsync(fullPrompt, targetProvider, systemPrompt, workingDir);
        sw.Stop();

        // ログを記録 — Model が空のときは provider 名を使用して統計ページで正しく分類できるようにする
        var modelName = string.IsNullOrEmpty(result.Model) ? targetProvider : result.Model;
        await LogAgentStepAsync(messageId, agent?.RoleName ?? "Assistant", modelName, systemPrompt ?? "Default Assistant", fullPrompt, result.Output, (int)sw.ElapsedMilliseconds, result.PromptTokens, result.CompletionTokens, result.TotalTokens);

        return result.Output;
    }

    /// <summary>
    /// Harness Cooperative Mode。
    /// Orchestrator → Executor（最大2回リトライ）→ Reviewer の3段パイプライン。
    /// 各ステップの結果を AgentStep としてDBに記録する。
    /// </summary>
    public async Task<(string Html, List<AgentStep> Steps)> CooperateAsync(
        string task, int userId, int messageId, int? chatSessionId, string? provider = null,
        List<string>? selectedAgentNames = null,
        Func<string, string, Task>? onStepComplete = null)
    {
        var targetProvider = provider ?? DefaultProvider;
        var steps = new List<AgentStep>();
        List<AgentDefinition> agentsToRun = new();

        // 1. 如果指定了特定 Agent 列表，按顺序运行 (Legacy/Custom 模式)
        if (selectedAgentNames != null && selectedAgentNames.Any())
        {
            var allAvailable = await GetAvailableAgentsAsync(userId);
            foreach (var name in selectedAgentNames)
            {
                var def = allAvailable.FirstOrDefault(a => a.Name == name);
                if (def != null) agentsToRun.Add(def);
            }
        }
        else if (chatSessionId.HasValue)
        {
            // 获取项目自定义角色
            var session = await _db.ChatSessions
                .Include(s => s.Project)
                    .ThenInclude(p => p!.Agents)
                .FirstOrDefaultAsync(s => s.Id == chatSessionId.Value);
            
            if (session?.Project?.Agents != null && session.Project.Agents.Any())
            {
                var activeAgents = session.Project.Agents.Where(a => a.IsActive).OrderBy(a => a.Id).ToList();
                agentsToRun = activeAgents.Select(a => new AgentDefinition(a.RoleName, a.RoleName, "DB Agent", a.SystemPrompt)).ToList();
            }
        }

        if (agentsToRun.Any())
        {
            string lastOutput = "";
            foreach (var agent in agentsToRun)
            {
                string input = string.IsNullOrEmpty(lastOutput) 
                    ? task 
                    : $"Task: {task}\n\nPrevious Agent Output:\n{lastOutput}";

                var step = await RunAgentStepAsync(
                    role: agent.Name,
                    persona: agent.SystemPrompt,
                    input: input,
                    messageId: messageId,
                    provider: targetProvider,
                    userId: userId,
                    chatSessionId: chatSessionId
                );

                // --- Tool Execution ---
                var projectRoot = await GetProjectRootAsync(chatSessionId);
                step.Output = await _toolExecutor.ExecuteToolsAsync(step.Output, projectRoot);
                await _db.SaveChangesAsync();

                // --- Evaluation ---
                _ = Task.Run(() => _evalService.EvaluateStepAsync(step.Id, task, step.Output, targetProvider));

                steps.Add(step);
                lastOutput = step.Output;
                if (onStepComplete != null) await onStepComplete(step.Role, BuildStepHtml(step));
            }
            string html = BuildCooperativeHtml(steps, lastOutput);

            if (chatSessionId.HasValue)
            {
                _ = Task.Run(async () => {
                    await _sessionMemory.PromoteToLongTermAsync(chatSessionId.Value, userId);
                    await _skillLearning.LearnFromInteractionAsync(task, lastOutput, steps, userId);
                });
            }

            return (html, steps);
        }

        // 2. 动态パイプライン模式 (Harness Engineering)
        var pipeline = _pipelineLoader.Get("default") ?? throw new Exception("Default pipeline not found.");
        string currentInput = task;
        string contextFromPreviousStages = "";

        foreach (var stage in pipeline.Stages)
        {
            if (stage.IsOptional && string.IsNullOrEmpty(currentInput)) continue;

            AgentStep stageStep = null!;
            string stagePersona = !string.IsNullOrEmpty(stage.SystemPromptTemplate) 
                ? await _pipelineLoader.GetPromptTemplateAsync(stage.SystemPromptTemplate)
                : stage.SystemPromptInline ?? "You are a helpful AI assistant.";
            
            for (int attempt = 1; attempt <= stage.MaxAttempts; attempt++)
            {
                string combinedInput = string.IsNullOrEmpty(contextFromPreviousStages)
                    ? currentInput
                    : $"Task: {task}\n\nContext from previous stages:\n{contextFromPreviousStages}\n\nCurrent stage input: {currentInput}";

                stageStep = await RunAgentStepAsync(
                    role: stage.Name,
                    persona: stagePersona,
                    input: combinedInput,
                    messageId: messageId,
                    provider: stage.Provider ?? targetProvider,
                    userId: userId,
                    chatSessionId: chatSessionId,
                    attemptNumber: attempt
                );

                // --- Tool Execution ---
                var projectRoot = await GetProjectRootAsync(chatSessionId);
                var toolOutput = await _toolExecutor.ExecuteToolsAsync(stageStep.Output, projectRoot);
                if (toolOutput != stageStep.Output)
                {
                    stageStep.Output = toolOutput;
                    await _db.SaveChangesAsync();
                }

                // --- Schema Validation ---
                if (!string.IsNullOrEmpty(stage.OutputSchema))
                {
                    var validationResult = _schemaValidator.Validate($"{stage.OutputSchema}.json", stageStep.Output);
                    if (!validationResult.IsValid)
                    {
                        stageStep.WasAccepted = false;
                        await _db.SaveChangesAsync();

                        if (attempt < stage.MaxAttempts)
                        {
                            currentInput = _schemaValidator.GenerateCorrectivePrompt(validationResult.Errors, stageStep.Output);
                            continue; // Retry with corrective prompt
                        }
                    }
                }

                // --- Quality Check ---
                if (stage.RetryOnQualityFail && attempt < stage.MaxAttempts)
                {
                    bool qualityOk = await QuickQualityCheckAsync(task, stageStep.Output, provider);
                    if (!qualityOk)
                    {
                        stageStep.WasAccepted = false;
                        await _db.SaveChangesAsync();
                        continue; // Retry
                    }
                }

                // Success
                stageStep.WasAccepted = true;
                await _db.SaveChangesAsync();

                // --- Evaluation ---
                _ = Task.Run(() => _evalService.EvaluateStepAsync(stageStep.Id, task, stageStep.Output, stage.Provider ?? provider));

                break;
            }

            steps.Add(stageStep);
            if (onStepComplete != null) await onStepComplete(stageStep.Role, BuildStepHtml(stageStep));

            contextFromPreviousStages += $"\n--- Stage: {stage.Name} ---\n{stageStep.Output}\n";
            currentInput = stageStep.Output;

            if (stage.IsFinalStage) break;
        }

        string finalResult = steps.Last().Output;
        if (chatSessionId.HasValue)
        {
            _ = Task.Run(async () => {
                await _sessionMemory.PromoteToLongTermAsync(chatSessionId.Value, userId);
                await _skillLearning.LearnFromInteractionAsync(task, finalResult, steps, userId);
            });
        }

        return (BuildCooperativeHtml(steps, finalResult), steps);
    }

    public async IAsyncEnumerable<string> CooperateStreamAsync(
        string task, int userId, int messageId, int? chatSessionId = null, string? provider = null)
    {
        var targetProvider = provider ?? DefaultProvider;
        var steps = new List<AgentStep>();
        List<AgentDefinition> agentsToRun = new();

        if (chatSessionId.HasValue)
        {
            var session = await _db.ChatSessions
                .Include(s => s.Project)
                    .ThenInclude(p => p!.Agents)
                .FirstOrDefaultAsync(s => s.Id == chatSessionId.Value);
            
            if (session?.Project?.Agents != null && session.Project.Agents.Any())
            {
                var activeAgents = session.Project.Agents.Where(a => a.IsActive).OrderBy(a => a.Id).ToList();
                agentsToRun = activeAgents.Select(a => new AgentDefinition(a.RoleName, a.RoleName, "DB Agent", a.SystemPrompt)).ToList();
            }
        }

        if (!agentsToRun.Any())
        {
            agentsToRun.Add(new AgentDefinition("Orchestrator", "Orchestrator", "Task Breakdown", """
                あなたはタスク分解の専門家（Orchestrator）です。
                ユーザーのタスクを分析し、以下のJSON形式でのみ回答してください：
                {
                  "plan": "全体方針の概要（1-2文）",
                  "subtasks": [
                    {"id": 1, "description": "サブタスクの説明", "expectedOutput": "期待される成果物"},
                    ...
                  ]
                }
                """));
            agentsToRun.Add(new AgentDefinition("Executor", "Executor", "Implementation", "あなたは実装の専門家（Executor）です。計画に基づいて成果物を作成してください。"));
            agentsToRun.Add(new AgentDefinition("Reviewer", "Reviewer", "Review", "あなたは評審の専門家（Reviewer）です。最終的な回答をMarkdownで作成してください。"));
        }

        string contextFromPrevious = "";
        foreach (var agent in agentsToRun)
        {
            yield return $"event: step-start\ndata: {agent.DisplayName}\n\n";
            
            string currentInput = string.IsNullOrEmpty(contextFromPrevious)
                ? task
                : $"Task: {task}\n\nPrevious steps context:\n{contextFromPrevious}";

            var step = await RunAgentStepAsync(agent.Name, agent.SystemPrompt, currentInput, messageId, targetProvider, userId, chatSessionId);
            steps.Add(step);
            
            yield return $"event: step-complete\ndata: {agent.DisplayName}|{BuildStepHtml(step).Replace("\n", "\\n")}\n\n";
            contextFromPrevious += $"\n--- {agent.Name} ---\n{step.Output}\n";
        }

        string finalResult = steps.Last().Output;
        string finalHtml = BuildCooperativeHtml(steps, finalResult);
        yield return $"event: final\ndata: {finalHtml.Replace("\n", "\\n")}\n\n";
    }

    public async IAsyncEnumerable<string> GetResponseStreamAsync(
        string prompt, int userId, int? chatSessionId, string? provider = null, int? agentId = null)
    {
        var targetProvider = provider ?? DefaultProvider;
        AgentProfile? agent = agentId.HasValue ? await _db.AgentProfiles.FindAsync(agentId.Value) : null;
        var systemPrompt = await BuildSystemPromptAsync(prompt, userId, chatSessionId, agent?.RoleName, agent);
        var workingDir = await GetProjectRootAsync(chatSessionId);
        var history = await BuildHistoryBlockAsync(chatSessionId, limit: 10);
        string fullPrompt = string.IsNullOrEmpty(history)
            ? prompt
            : $"{history}\nUser: {prompt}";

        if (agent?.PreferredProvider != null) targetProvider = agent.PreferredProvider;

        string fileName = targetProvider switch
        {
            "gh-copilot" => "copilot",
            "claudecode" => "claudecode",
            "claude" => "claude",
            "codex" => "codex",
            "opencode" => "opencode",
            _ => DefaultProvider
        };
        var useJsonStreaming = targetProvider == "gemini" || fileName == "gemini" ||
                              targetProvider == "claude" || fileName == "claude" ||
                              targetProvider == "claudecode" || fileName == "claudecode" ||
                              targetProvider == "gh-copilot" || fileName == "copilot" ||
                              targetProvider == "codex" || fileName == "codex" ||
                              targetProvider == "opencode" || fileName == "opencode";

        var processInfo = SetupProcessInfo(targetProvider, workingDir, useJsonStreaming ? "stream-json" : null);

        // 获取 MessageId 用于记录日志
        int messageId = await GetLatestUserMessageIdAsync(chatSessionId);

        string inputToStdin = string.IsNullOrEmpty(systemPrompt)
            ? fullPrompt
            : $"{systemPrompt}\n\n{fullPrompt}";

        if (targetProvider == "opencode")
        {
            processInfo.ArgumentList.Add(inputToStdin);
            inputToStdin = string.Empty;
        }
        else if (targetProvider == "gh-copilot")
        {
            processInfo.ArgumentList.Add("-p");
            processInfo.ArgumentList.Add(inputToStdin);
            inputToStdin = string.Empty;
        }

        var sw = Stopwatch.StartNew();
        Process? process = null;
        string? startError = null;
        try
        {
            process = Process.Start(processInfo);
        }
        catch (Exception ex)
        {
            startError = $"Error starting CLI: {ex.Message}";
        }

        if (startError != null)
        {
            yield return startError;
            yield break;
        }

        if (process == null)
        {
            yield return "Error: Could not start CLI.";
            yield break;
        }

        await process.StandardInput.WriteAsync(inputToStdin);
        process.StandardInput.Close();

        // Read stderr line-by-line; kill process immediately on known fatal errors
        var stderrContent = new StringBuilder();
        var stderrTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync()) != null)
            {
                stderrContent.AppendLine(line);
                if (line.Contains("402") ||
                    line.Contains("quota", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("No prompt provided", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("authentication failed", StringComparison.OrdinalIgnoreCase))
                {
                    try { process.Kill(true); } catch { }
                }
            }
            return stderrContent.ToString();
        });

        // Kill process after timeout to prevent indefinite hang
        using var cts = new CancellationTokenSource();
        _ = Task.Delay(TimeSpan.FromSeconds(TimeoutSeconds), cts.Token).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
            }
        }, TaskScheduler.Default);

        var fullResponse = new StringBuilder();
        string? extractedModel = null;
        string? lastAssistantContent = null; // Claude CLI fallback: full text from last assistant event
        int pt = 0, ct = 0, tt = 0;

        // Buffer for detecting and stripping echoed System:/User: prefix at stream start
        var prefixBuffer = new StringBuilder();
        bool prefixHandled = false;
        const int maxPrefixBuffer = 4096;
        string[] promptPrefixes = { "System:", "User:", "Assistant:", "Context:", "History:", "[会話履歴]:", "[ユーザーの既知情報・長期記憶]:" };

        if (useJsonStreaming)
        {
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string? chunk = null;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    
                    // Capture model — root "model"/"modelVersion" (Gemini), or nested "message.model" (Claude CLI)
                    if (doc.RootElement.TryGetProperty("model", out var mProp) ||
                        doc.RootElement.TryGetProperty("modelVersion", out mProp))
                    {
                        var mv = mProp.GetString();
                        if (!string.IsNullOrEmpty(mv)) extractedModel = mv;
                    }
                    else if (doc.RootElement.TryGetProperty("message", out var msgForModel) &&
                             msgForModel.TryGetProperty("model", out var nestedMProp))
                    {
                        var mv = nestedMProp.GetString();
                        if (!string.IsNullOrEmpty(mv)) extractedModel = mv;
                    }

                    // Capture usage — multi-provider:
                    //   Gemini streaming: stats.{input_tokens,output_tokens,total_tokens} on type=="result" line
                    //   Gemini non-streaming: no top-level usage (handled separately in non-streaming path)
                    //   Claude CLI: usage.{input_tokens,output_tokens} at root or message.usage
                    //   OpenAI / Copilot: usage.{prompt_tokens,completion_tokens}
                    //   Anthropic API: usageMetadata camelCase
                    JsonElement usageProp;
                    bool hasUsage = doc.RootElement.TryGetProperty("usage", out usageProp) ||
                                   doc.RootElement.TryGetProperty("usage_metadata", out usageProp) ||
                                   doc.RootElement.TryGetProperty("usageMetadata", out usageProp);
                    if (!hasUsage && doc.RootElement.TryGetProperty("message", out var msgForUsage) &&
                        msgForUsage.TryGetProperty("usage", out usageProp))
                        hasUsage = true;

                    if (hasUsage)
                    {
                        if (usageProp.TryGetProperty("input_tokens", out var itProp) || usageProp.TryGetProperty("prompt_tokens", out itProp) || usageProp.TryGetProperty("prompt_token_count", out itProp) || usageProp.TryGetProperty("promptTokenCount", out itProp) || usageProp.TryGetProperty("inputTokenCount", out itProp)) pt = itProp.GetInt32();
                        if (usageProp.TryGetProperty("output_tokens", out var otProp) || usageProp.TryGetProperty("completion_tokens", out otProp) || usageProp.TryGetProperty("completion_token_count", out otProp) || usageProp.TryGetProperty("candidate_token_count", out otProp) || usageProp.TryGetProperty("candidatesTokenCount", out otProp) || usageProp.TryGetProperty("candidateTokenCount", out otProp) || usageProp.TryGetProperty("outputTokenCount", out otProp)) ct = otProp.GetInt32();
                        if (usageProp.TryGetProperty("total_tokens", out var ttProp) || usageProp.TryGetProperty("total_token_count", out ttProp) || usageProp.TryGetProperty("totalTokenCount", out ttProp)) tt = ttProp.GetInt32();
                        if (tt == 0 && (pt > 0 || ct > 0)) tt = pt + ct;
                    }

                    // Gemini streaming result line: {"type":"result","stats":{"input_tokens":N,"output_tokens":K,"total_tokens":M,"models":{...}}}
                    if (doc.RootElement.TryGetProperty("stats", out var statsEl))
                    {
                        if (statsEl.TryGetProperty("input_tokens", out var sitProp)) pt = sitProp.GetInt32();
                        if (statsEl.TryGetProperty("output_tokens", out var sotProp)) ct = sotProp.GetInt32();
                        if (statsEl.TryGetProperty("total_tokens", out var sttProp)) tt = sttProp.GetInt32();
                        
                        // Extract actual model names from stats.models keys
                        if (statsEl.TryGetProperty("models", out var modelsEl) && modelsEl.ValueKind == JsonValueKind.Object)
                        {
                            var names = new List<string>();
                            foreach (var modelEntry in modelsEl.EnumerateObject())
                            {
                                names.Add(modelEntry.Name);
                                // Also sum tokens from individual models if top-level stats were 0
                                if (pt == 0 || ct == 0)
                                {
                                    if (modelEntry.Value.TryGetProperty("tokens", out var tokEl))
                                    {
                                        if (tokEl.TryGetProperty("input", out var tit)) pt += tit.GetInt32();
                                        if (tokEl.TryGetProperty("candidates", out var tct)) ct += tct.GetInt32();
                                        if (tokEl.TryGetProperty("total", out var ttt)) tt += ttt.GetInt32();
                                    }
                                }
                            }
                            if (names.Any()) extractedModel = string.Join("+", names);
                        }
                        if (tt == 0 && (pt > 0 || ct > 0)) tt = pt + ct;
                    }

                    // Extract tokens and model names from modelUsage keys (Claude CLI)
                    // {"modelUsage":{"claude-sonnet-4-6":{"input_tokens":N,"output_tokens":K}}}
                    if (doc.RootElement.TryGetProperty("modelUsage", out var modelUsageEl) && modelUsageEl.ValueKind == JsonValueKind.Object)
                    {
                        var muNames = new List<string>();
                        foreach (var prop in modelUsageEl.EnumerateObject())
                        {
                            muNames.Add(prop.Name);
                            if (prop.Value.TryGetProperty("input_tokens", out var mit)) pt += mit.GetInt32();
                            if (prop.Value.TryGetProperty("output_tokens", out var mot)) ct += mot.GetInt32();
                            if (prop.Value.TryGetProperty("total_tokens", out var mtt)) tt += mtt.GetInt32();
                        }
                        if (tt == 0 && (pt > 0 || ct > 0)) tt = pt + ct;
                        if (muNames.Any()) extractedModel = string.Join("+", muNames);
                    }

                    // Text extraction — ordered by provider format
                    if (doc.RootElement.TryGetProperty("type", out var typeProp))
                    {
                        var type = typeProp.GetString();
                        if (type == "message")
                        {
                            // Gemini CLI: {"type":"message","role":"assistant","content":"chunk","delta":true}
                            bool isAssistant = true;
                            if (doc.RootElement.TryGetProperty("role", out var roleProp))
                            {
                                var role = roleProp.GetString();
                                if (role == "user" || role == "system") isAssistant = false;
                            }
                            if (isAssistant && doc.RootElement.TryGetProperty("content", out var contentProp))
                                chunk = contentProp.GetString() ?? "";
                        }
                        else if (type == "content_block_delta" || type == "text_delta")
                        {
                            // Claude CLI / Anthropic API streaming delta
                            if (doc.RootElement.TryGetProperty("delta", out var deltaProp) &&
                                deltaProp.TryGetProperty("text", out var textProp))
                                chunk = textProp.GetString() ?? "";
                        }
                        else if (type == "assistant" && doc.RootElement.TryGetProperty("message", out var aMsg))
                        {
                            // Claude CLI verbose streaming: {"type":"assistant","message":{"content":[{"type":"text","text":"..."}],...}}
                            // Content is the FULL accumulated text so far — only use as fallback if no delta chunks yet
                            if (aMsg.TryGetProperty("content", out var aContentArr) && aContentArr.ValueKind == JsonValueKind.Array)
                            {
                                var sb2 = new StringBuilder();
                                foreach (var block in aContentArr.EnumerateArray())
                                {
                                    if (block.TryGetProperty("type", out var blockType) && blockType.GetString() == "text" &&
                                        block.TryGetProperty("text", out var blockText))
                                        sb2.Append(blockText.GetString());
                                }
                                lastAssistantContent = sb2.Length > 0 ? sb2.ToString() : null;
                            }
                        }
                        else if (type == "result" && doc.RootElement.TryGetProperty("result", out var resultProp))
                        {
                            // Claude CLI final event: {"type":"result","result":"full text","usage":{...}}
                            var resultText = resultProp.GetString() ?? "";
                            if (fullResponse.Length == 0 && !string.IsNullOrEmpty(resultText))
                                chunk = resultText;
                        }
                        else if (type == "item.completed" && doc.RootElement.TryGetProperty("item", out var codexItem))
                        {
                            // Codex: {"type":"item.completed","item":{"type":"agent_message","text":"Hi"}}
                            if (codexItem.TryGetProperty("type", out var itemType) && itemType.GetString() == "agent_message" &&
                                codexItem.TryGetProperty("text", out var itemText))
                                chunk = itemText.GetString() ?? "";
                        }
                        else if (type == "turn.completed" && doc.RootElement.TryGetProperty("usage", out var codexUsage))
                        {
                            // Codex: {"type":"turn.completed","usage":{"input_tokens":N,"output_tokens":K}}
                            // Token extraction already handled by generic "usage" property check above.
                            // Model name not provided by codex CLI — will use provider name as fallback.
                            if (string.IsNullOrEmpty(extractedModel)) extractedModel = "codex";
                        }
                        else if (type == "text" && doc.RootElement.TryGetProperty("part", out var ocPart))
                        {
                            // OpenCode: {"type":"text","part":{"text":"Hi",...}}
                            if (ocPart.TryGetProperty("text", out var ocText))
                                chunk = ocText.GetString() ?? "";
                        }
                        else if (type == "step_finish" && doc.RootElement.TryGetProperty("part", out var ocFinish))
                        {
                            // OpenCode: {"type":"step_finish","part":{"tokens":{"total":N,"input":M,"output":K},...}}
                            if (ocFinish.TryGetProperty("tokens", out var ocTokens))
                            {
                                if (ocTokens.TryGetProperty("input", out var ocIn)) pt = ocIn.GetInt32();
                                if (ocTokens.TryGetProperty("output", out var ocOut)) ct = ocOut.GetInt32();
                                if (ocTokens.TryGetProperty("total", out var ocTot)) tt = ocTot.GetInt32();
                                if (tt == 0 && (pt > 0 || ct > 0)) tt = pt + ct;
                            }
                            // Extract model from part if available
                            if (ocFinish.TryGetProperty("model", out var ocModel)) { var mv = ocModel.GetString(); if (!string.IsNullOrEmpty(mv)) extractedModel = mv; }
                            if (string.IsNullOrEmpty(extractedModel)) extractedModel = "opencode";
                        }
                    }
                    else if (doc.RootElement.TryGetProperty("content", out var directContent))
                    {
                        chunk = directContent.GetString() ?? "";
                    }
                    // Gemini API streaming format: candidates[0].content.parts[0].text
                    else if (doc.RootElement.TryGetProperty("candidates", out var streamCandidates) &&
                             streamCandidates.ValueKind == JsonValueKind.Array && streamCandidates.GetArrayLength() > 0 &&
                             streamCandidates[0].TryGetProperty("content", out var sCContent) &&
                             sCContent.TryGetProperty("parts", out var sParts) &&
                             sParts.ValueKind == JsonValueKind.Array && sParts.GetArrayLength() > 0 &&
                             sParts[0].TryGetProperty("text", out var sPartText))
                    {
                        chunk = sPartText.GetString() ?? "";
                    }
                }
                catch (JsonException) { /* Skip invalid JSON lines */ }

                if (chunk != null)
                {
                    fullResponse.Append(chunk);
                    yield return chunk; // For JSON streaming, we trust the role/type filtering
                }
            }
        }
        else
        {
            var buffer = new char[64];
            int read;
            while ((read = await process.StandardOutput.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                var chunk = new string(buffer, 0, read);
                fullResponse.Append(chunk);
                if (!prefixHandled)
                {
                    prefixBuffer.Append(chunk);
                    var buf = prefixBuffer.ToString();
                    
                    bool startsWithPrefix = promptPrefixes.Any(p => buf.StartsWith(p, StringComparison.OrdinalIgnoreCase));
                    
                    if (!startsWithPrefix)
                    {
                        prefixHandled = true;
                        yield return buf;
                        prefixBuffer.Clear();
                    }
                    else if (buf.Contains("\nUser:") || buf.Contains("\nAssistant:"))
                    {
                        // We think we've reached the end of the echoed prompt
                        var stripped = StripEchoedPromptPrefix(buf);
                        prefixHandled = true;
                        if (!string.IsNullOrEmpty(stripped)) yield return stripped;
                        prefixBuffer.Clear();
                    }
                    else if (buf.Length >= maxPrefixBuffer)
                    {
                        prefixHandled = true;
                        yield return buf;
                        prefixBuffer.Clear();
                    }
                }
                else
                {
                    yield return chunk;
                }
            }
            if (!prefixHandled && prefixBuffer.Length > 0)
            {
                var stripped = StripEchoedPromptPrefix(prefixBuffer.ToString());
                if (!string.IsNullOrEmpty(stripped)) yield return stripped;
            }
        }
        try { await process.WaitForExitAsync(); } catch { }
        cts.Cancel();
        sw.Stop();

        var stderrOutput = await stderrTask;

        // Claude CLI verbose streaming: if no content_block_delta chunks, fall back to last assistant event content
        if (fullResponse.Length == 0 && !string.IsNullOrEmpty(lastAssistantContent))
        {
            fullResponse.Append(lastAssistantContent);
            yield return lastAssistantContent;
        }

        // stderr フォールバック: stdout が空の場合は gemini に切り替え、またはエラーを表示
        if (fullResponse.Length == 0)
        {
            if (targetProvider != FallbackProvider)
            {
                await foreach (var chunk in GetResponseStreamAsync(prompt, userId, chatSessionId, FallbackProvider, agentId))
                {
                    yield return chunk;
                }
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(stderrOutput))
            {
                var parsedError = ExtractCliError(stderrOutput, targetProvider);
                var errMsg = $"[Error from {targetProvider}]: {parsedError}";
                fullResponse.Append(errMsg);
                yield return errMsg;
            }
        }

        // ストリーム完了後にログを保存
        await LogAgentStepAsync(messageId, agent?.RoleName ?? "Assistant", extractedModel ?? targetProvider, systemPrompt ?? "Default Assistant", fullPrompt, fullResponse.ToString(), (int)sw.ElapsedMilliseconds, pt, ct, tt);
    }

    public async Task<string> ExecuteCliDirectAsync(string prompt, string provider, string? systemPrompt = null, string? workingDir = null)
    {
        var result = await ExecuteCliAsync(prompt, provider, systemPrompt, workingDir);
        return result.Output;
    }

    /// <summary>
    /// 使用主动式代理配置执行 AI 任务（哨兵、记录员、主脑）。
    /// </summary>
    public async Task<string> ExecuteProactiveAgentAsync(ProactiveAgentProfile profile, string prompt, int? userId = null, int? chatSessionId = null)
    {
        var targetProvider = profile.PreferredProvider ?? DefaultProvider;
        var workingDir = await GetProjectRootAsync(chatSessionId);
        
        var sb = new StringBuilder(profile.SystemPrompt);
        
        if (profile.UseMemory && userId.HasValue)
        {
            var memories = await _memorySearch.SearchAsync(prompt, userId.Value);
            if (memories.Any())
            {
                sb.AppendLine("\n[相关的长期记忆]:");
                foreach (var m in memories) sb.AppendLine($"- {m.Content}");
            }

            if (chatSessionId.HasValue)
            {
                var sessionMemoryContext = await _sessionMemory.ReadAllAsContextAsync(chatSessionId.Value);
                if (!string.IsNullOrEmpty(sessionMemoryContext))
                {
                    sb.AppendLine("\n[当前会话上下文]:");
                    sb.AppendLine(sessionMemoryContext);
                }
            }
        }

        var sw = Stopwatch.StartNew();
        var result = await ExecuteCliAsync(prompt, targetProvider, sb.ToString(), workingDir);
        sw.Stop();

        // 记录日志（如果可能）
        if (userId.HasValue && chatSessionId.HasValue)
        {
            int messageId = await GetLatestUserMessageIdAsync(chatSessionId);
            if (messageId > 0)
            {
                await LogAgentStepAsync(messageId, profile.Role, result.Model, sb.ToString(), prompt, result.Output, (int)sw.ElapsedMilliseconds, result.PromptTokens, result.CompletionTokens, result.TotalTokens);
            }
        }

        return result.Output;
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
        var lastMsg = await _db.Messages
            .Where(m => m.ChatSessionId == chatSessionId.Value && !m.IsAi)
            .OrderByDescending(m => m.Id)
            .FirstOrDefaultAsync();
        return lastMsg?.Id ?? 0;
    }

    private async Task LogAgentStepAsync(int messageId, string role, string model, string persona, string input, string output, int durationMs, int promptTokens = 0, int completionTokens = 0, int totalTokens = 0)
    {
        if (messageId <= 0) return;
        var step = new AgentStep
        {
            MessageId = messageId,
            Role = role,
            Model = model,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens,
            Persona = persona ?? "Default Assistant",
            Input = input,
            Output = output,
            DurationMs = durationMs,
            WasAccepted = true,
            CreatedAt = DateTime.UtcNow
        };
        _db.AgentSteps.Add(step);
        await _db.SaveChangesAsync();
    }

    // ─────────────────────────────────────────
    // 内部実装
    // ─────────────────────────────────────────

    private async Task<AgentStep> RunAgentStepAsync(
        string role, string persona, string input, int messageId,
        string provider, int userId, int? chatSessionId = null, int attemptNumber = 1)
    {
        var roleSkills = await _memorySearch.SearchSkillsAsync(input, userId, agentRole: role);
        var memories = await _memorySearch.SearchAsync(input, userId);
        var workingDir = await GetProjectRootAsync(chatSessionId);
        var policies = await LoadPoliciesAsync();
        
        // Base Persona
        var sb = new StringBuilder();

        // Fetch project-specific role prompt if exists
        if (chatSessionId.HasValue)
        {
            var session = await _db.ChatSessions
                .Include(s => s.Project)
                .ThenInclude(p => p!.Agents)
                .FirstOrDefaultAsync(s => s.Id == chatSessionId.Value);
            
            var projectAgent = session?.Project?.Agents.FirstOrDefault(a => a.RoleName.Equals(role, StringComparison.OrdinalIgnoreCase));
            if (projectAgent != null)
            {
                sb.AppendLine(projectAgent.SystemPrompt);
                sb.AppendLine();
            }
        }

        sb.AppendLine(persona);
        sb.AppendLine(policies);

        if (memories.Any())
        {
            sb.AppendLine("\n[ユーザーの既知情報・長期記憶]:");
            foreach (var m in memories) sb.AppendLine($"- {m.Content}");
        }

        if (chatSessionId.HasValue)
        {
            // Inject Session Memory
            var sessionMemoryContext = await _sessionMemory.ReadAllAsContextAsync(chatSessionId.Value);
            if (!string.IsNullOrEmpty(sessionMemoryContext))
            {
                sb.AppendLine();
                sb.AppendLine(sessionMemoryContext);
            }
        }

        // Memory Instruction
        sb.AppendLine(GetSystemPromptTemplate("MemoryInstruction", "\n[MEMORY INSTRUCTION]:\n重要な発見や制約があれば \"MEMORY: key=value\" の形式で行末に出力してください。"));

        if (roleSkills.Any())
        {
            sb.AppendLine("\n[追加スキル指示]:");
            sb.Append(string.Join("\n", roleSkills.Select(s => $"- {s.Description}")));
            
            // 使用したスキルのメトリクスを更新（簡易的に最初の1つ）
            var firstSkill = roleSkills.First();
            _ = Task.Run(() => _skillLearning.UpdateSkillMetricsAsync(firstSkill.Id, true));
        }

        string fullPersona = sb.ToString();
        var sw = Stopwatch.StartNew();
        var result = await ExecuteCliAsync(input, provider, fullPersona, workingDir);
        sw.Stop();

        // Extract and Save Memory
        if (chatSessionId.HasValue)
        {
            await ParseAndSaveMemoryAsync(chatSessionId.Value, role, result.Output);
        }

        var step = new AgentStep
        {
            MessageId = messageId,
            Role = role,
            Model = string.IsNullOrEmpty(result.Model) ? provider : result.Model,
            PromptTokens = result.PromptTokens,
            CompletionTokens = result.CompletionTokens,
            TotalTokens = result.TotalTokens,
            Persona = fullPersona,
            Input = input,
            Output = result.Output,
            AttemptNumber = attemptNumber,
            WasAccepted = true,
            DurationMs = (int)sw.ElapsedMilliseconds,
            CreatedAt = DateTime.UtcNow
        };
        _db.AgentSteps.Add(step);
        await _db.SaveChangesAsync();

        return step;
    }

    private async Task ParseAndSaveMemoryAsync(int sessionId, string role, string output)
    {
        var lines = output.Split('\n');
        foreach (var line in lines)
        {
            if (line.Contains("MEMORY:", StringComparison.OrdinalIgnoreCase))
            {
                var content = line.Substring(line.IndexOf("MEMORY:", StringComparison.OrdinalIgnoreCase) + 7).Trim();
                var parts = content.Split('=');
                if (parts.Length == 2)
                {
                    await _sessionMemory.WriteAsync(sessionId, role, parts[0].Trim(), parts[1].Trim());
                }
            }
        }
    }

    private async Task<bool> QuickQualityCheckAsync(string originalTask, string execution, string? provider = null)
    {
        var targetProvider = provider ?? DefaultProvider;
        string checkPrompt = $"""
            以下のタスクに対して実行結果が十分か判断してください。
            タスク: {originalTask}
            実行結果: {execution}
            
            結果が十分であれば "OK"、不十分であれば "RETRY" のみを返してください。
            """;

        var result = await ExecuteCliAsync(checkPrompt, targetProvider, systemPrompt: null);
        return result.Output.Contains("OK", StringComparison.OrdinalIgnoreCase);
    }
    private async Task<string> LoadPoliciesAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "pipelines", "policies");
        if (!Directory.Exists(path)) return "";

        var sb = new StringBuilder("\n\n[ENVIRONMENTAL POLICIES & CONSTRAINTS]:\n");
        var files = Directory.GetFiles(path, "*.md");
        if (!files.Any()) return "";

        foreach (var file in files)
        {
            var content = await File.ReadAllTextAsync(file);
            sb.Append($"--- Policy: {Path.GetFileNameWithoutExtension(file)} ---\n{content}\n");
        }
        return sb.ToString();
    }

    private async Task<string> BuildSystemPromptAsync(string prompt, int userId, int? chatSessionId, string? agentRole, AgentProfile? selectedAgent = null)
    {
        var memories = await _memorySearch.SearchAsync(prompt, userId);
        var skills = await _memorySearch.SearchSkillsAsync(prompt, userId, agentRole);
        var policies = await LoadPoliciesAsync();

        var sb = new StringBuilder(GetSystemPromptTemplate("Default", "あなたは高度なAIアシスタントです。現在はソフトウェア開発プロジェクトのコンテキストで動作しています。"));
        sb.Append(policies);

        if (selectedAgent != null)
        {
            sb.Append($"\n\n[現在のアクティブエージェント]:\n役割: {selectedAgent.RoleName}\n指示: {selectedAgent.SystemPrompt}");
        }

        // Add Project Context if available
        if (chatSessionId.HasValue)
        {
            var session = await _db.ChatSessions
                .Include(s => s.Project)
                .ThenInclude(p => p!.Agents)
                .FirstOrDefaultAsync(s => s.Id == chatSessionId.Value);

            if (session?.Project != null)
            {
                sb.Append($"\n\n[プロジェクト文脈]:\nプロジェクト名: {session.Project.Name}\nルートパス: {session.Project.RootPath}");
                
                if (session.Project.Agents.Any())
                {
                    sb.Append("\n\n[利用可能なエージェント役割]:\n");
                    foreach (var agent in session.Project.Agents)
                    {
                        if (selectedAgent != null && agent.Id == selectedAgent.Id) continue;
                        sb.Append($"- {agent.RoleName}: {agent.SystemPrompt}\n");
                    }
                }
            }

            // Inject Session Memory
            var sessionMemoryContext = await _sessionMemory.ReadAllAsContextAsync(chatSessionId.Value);
            if (!string.IsNullOrEmpty(sessionMemoryContext))
            {
                sb.Append("\n\n" + sessionMemoryContext);
            }
        }

        if (memories.Any())
        {
            sb.Append("\n\n[ユーザーの既知情報・長期記憶]:\n");
            foreach (var m in memories) sb.Append($"- {m.Content}\n");
        }

        if (skills.Any())
        {
            sb.Append("\n\n[有効なスキル指示]:\n");
            foreach (var s in skills) sb.Append($"- {s.Description}\n");
        }

        // Memory Instruction
        sb.Append(GetSystemPromptTemplate("MemoryInstruction", "\n\n[MEMORY INSTRUCTION]:\n重要な発見や制約があれば \"MEMORY: key=value\" の形式で行末に出力してください。"));

        return sb.ToString();
    }

    private async Task<string> BuildHistoryBlockAsync(int? chatSessionId, int limit)
    {
        if (!chatSessionId.HasValue) return "";

        var msgs = await _db.Messages
            .Where(m => m.ChatSessionId == chatSessionId.Value)
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .OrderBy(m => m.Timestamp)
            .ToListAsync();

        if (!msgs.Any()) return "";

        var sb = new StringBuilder("[会話履歴]:\n");
        foreach (var m in msgs)
            sb.Append($"{(m.IsAi ? "Assistant" : "User")}: {m.Content}\n");

        return sb.ToString();
    }

    private static string BuildStepHtml(AgentStep step)
    {
        string badgeClass = step.Role switch
        {
            "Orchestrator" => "badge-info",
            "Executor" => "badge-success",
            "Reviewer" => "badge-secondary",
            _ => "badge-ghost"
        };
        string retryBadge = step.AttemptNumber > 1
            ? $"<span class='badge badge-warning badge-xs ml-1'>Retry #{step.AttemptNumber}</span>" : "";
        string acceptedIcon = step.WasAccepted ? "" :
            "<span class='badge badge-error badge-xs ml-1'>Rejected</span>";

        return $"""
            <div class='collapse collapse-arrow bg-base-300/30 border border-base-content/10 mb-2'>
                <input type='checkbox' />
                <div class='collapse-title text-sm font-medium flex items-center gap-2'>
                    <span class='badge {badgeClass} badge-sm'>{step.Role}</span>
                    {retryBadge}{acceptedIcon}
                    <span class='opacity-50 text-xs ml-auto'>{step.DurationMs}ms</span>
                </div>
                <div class='collapse-content text-xs opacity-80'>
                    <pre class='whitespace-pre-wrap font-sans'>{System.Net.WebUtility.HtmlEncode(step.Output)}</pre>
                </div>
            </div>
            """;
    }

    private static string BuildCooperativeHtml(List<AgentStep> steps, string finalOutput)
    {
        return System.Net.WebUtility.HtmlEncode(finalOutput);
    }

    private static string ExtractJson(string text)
    {
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start >= 0 && end > start) return text[start..(end + 1)];
        return "{}";
    }

    private static string StripEchoedPromptPrefix(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var lines = text.Split('\n');
        int firstContentLine = -1;
        string? firstLineContent = null;
        
        string[] promptPrefixes = { 
            "System:", "User:", "Assistant:", "Context:", "History:", 
            "Thought:", "Thinking:", "[会話履歴]:", "[ユーザーの既知情報・長期記憶]:", 
            "[相关的长期记忆]:", "[当前会话上下文]:",
            "[追加スキル指示]:", "[MEMORY INSTRUCTION]:", "[ENVIRONMENTAL POLICIES & CONSTRAINTS]:", 
            "--- Policy:", "Role:", "Persona:", "Input:", "Output:"
        };
        // Removed "Memory:" from the list above because AI uses "MEMORY: key=value" for memory extraction.

        string[] systemPromptFragments = { 
            "あなたは高度なAIアシスタントです", 
            "你是高度进化的自主 AI 代理",
            "現在はソフトウェア開発プロジェクトのコンテキストで動作しています",
            "你目前运行在 AiChatApp 项目的上下文中",
            "あなたはタスク分解の専門家",
            "あなたは実装の専門家",
            "あなたは評審の専門家",
            "[MEMORY INSTRUCTION]",
            "[会話履歴]:",
            "[ユーザーの既知情報・長期記憶]:",
            "[相关的长期记忆]:",
            "重要な発見や制約があれば"
        };

        for (int i = 0; i < Math.Min(lines.Length, 150); i++)
        {
            var trimmedLine = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine)) continue;

            bool isPrefixHeader = false;
            string? matchedPrefix = null;
            foreach (var p in promptPrefixes)
            {
                if (trimmedLine.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                {
                    isPrefixHeader = true;
                    matchedPrefix = p;
                    break;
                }
            }

            bool isSystemFragment = systemPromptFragments.Any(f => trimmedLine.Contains(f));

            if (isPrefixHeader)
            {
                // Check if there is content AFTER the prefix on the SAME line
                var contentAfter = trimmedLine.Substring(matchedPrefix!.Length).Trim();
                if (!string.IsNullOrEmpty(contentAfter))
                {
                    // If the content after the prefix is also a known system fragment, skip it
                    if (systemPromptFragments.Any(f => contentAfter.Contains(f)))
                    {
                        continue;
                    }

                    // This is likely the start of the actual response
                    firstContentLine = i;
                    firstLineContent = contentAfter;
                    break;
                }
                // It was just a header line, skip and continue
                continue;
            }
            
            if (isSystemFragment)
            {
                // System prompts are echoes, skip them
                continue;
            }

            // If it's not a prefix header and not a system fragment, it must be content!
            firstContentLine = i;
            firstLineContent = lines[i];
            break;
        }

        if (firstContentLine != -1)
        {
            var remainingLines = lines.Skip(firstContentLine + 1).ToList();
            var result = firstLineContent + (remainingLines.Any() ? "\n" + string.Join("\n", remainingLines) : "");
            return result.Trim();
        }

        return text.Trim();
    }

    private static string CleanResponse(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // 1. Strip echoed System:/User: prompt prefix
        text = StripEchoedPromptPrefix(text);

        // 2. Remove XML-style thinking/thought tags
        text = Regex.Replace(text, @"<(thinking|thought|thought_process)>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<(thinking|thought|thought_process)>.*", "", RegexOptions.Singleline | RegexOptions.IgnoreCase); // Handle unclosed tags

        // 3. Remove "Thought:" or "Thinking:" blocks
        text = Regex.Replace(text, @"(^(Thought|Thinking|Reasoning):.*?\n\n)", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\n\n(Thought|Thinking|Reasoning):.*?\n\n", "\n\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"(Thought|Thinking|Reasoning):.*?$", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // 4. Final check for any leaking "System:" or "Assistant:" at the very beginning
        if (text.StartsWith("System:", StringComparison.OrdinalIgnoreCase))
            text = text.Substring(7).Trim();
        if (text.StartsWith("Assistant:", StringComparison.OrdinalIgnoreCase))
            text = text.Substring(10).Trim();

        return text.Trim();
    }

    private static string ExtractCliError(string stderr, string provider)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return "Unknown CLI error.";

        var lines = stderr
            .Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var messages = new List<string>();

        foreach (var line in lines)
        {
            if (provider == "codex" && TryExtractCodexJsonError(line, out var jsonError))
            {
                messages.Add(jsonError);
                continue;
            }

            if (ShouldIgnoreStderrLine(line, provider))
                continue;

            messages.Add(line);
        }

        var distinctMessages = messages
            .Select(m => m.Trim())
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct()
            .ToList();

        if (distinctMessages.Count == 0)
            return "Request failed in CLI process.";

        var combined = string.Join("\n", distinctMessages);
        return NormalizeProviderError(combined, provider);
    }

    private static string NormalizeProviderError(string message, string provider)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Request failed in CLI process.";

        if (provider == "codex" &&
            message.Contains("model is not supported when using Codex with a ChatGPT account", StringComparison.OrdinalIgnoreCase))
        {
            return "Codex is logged in with a ChatGPT account, so API models such as gpt-5.5 are unavailable. Re-authenticate Codex with an OpenAI API key.";
        }

        if (provider == "gh-copilot" &&
            (message.Contains("402", StringComparison.OrdinalIgnoreCase) ||
             message.Contains("You have no quota", StringComparison.OrdinalIgnoreCase)))
        {
            return "GitHub Copilot quota is unavailable or the subscription has expired. Check your Copilot plan and quota status.";
        }

        return message;
    }

    private static bool TryExtractCodexJsonError(string line, out string message)
    {
        message = string.Empty;
        const string prefix = "ERROR:";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var payload = line[prefix.Length..].Trim();
        if (!payload.StartsWith("{", StringComparison.Ordinal))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var errorMessage))
            {
                message = errorMessage.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(message);
            }

            if (doc.RootElement.TryGetProperty("message", out var rootMessage))
            {
                message = rootMessage.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(message);
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private static bool ShouldIgnoreStderrLine(string line, string provider)
    {
        if (line.StartsWith("System:", StringComparison.Ordinal) ||
            line.StartsWith("User:", StringComparison.Ordinal))
        {
            return true;
        }

        if (provider == "opencode")
            return true;

        if (provider != "codex")
            return false;

        if (line == "Reading prompt from stdin..." || line == "--------" || line == "user")
            return true;

        if (line.StartsWith("OpenAI Codex v", StringComparison.Ordinal) ||
            line.StartsWith("workdir:", StringComparison.Ordinal) ||
            line.StartsWith("model:", StringComparison.Ordinal) ||
            line.StartsWith("provider:", StringComparison.Ordinal) ||
            line.StartsWith("approval:", StringComparison.Ordinal) ||
            line.StartsWith("sandbox:", StringComparison.Ordinal) ||
            line.StartsWith("reasoning effort:", StringComparison.Ordinal) ||
            line.StartsWith("reasoning summaries:", StringComparison.Ordinal) ||
            line.StartsWith("session id:", StringComparison.Ordinal) ||
            line.StartsWith("System:", StringComparison.Ordinal) ||
            line.StartsWith("User:", StringComparison.Ordinal))
        {
            return true;
        }

        if (line.Contains("failed to record rollout items", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private ProcessStartInfo SetupProcessInfo(string provider, string? workingDirectory = null, string? outputFormat = null)
    {
        string fileName = provider switch
        {
            "gh-copilot" => "copilot",
            "claudecode" => "claudecode",
            "claude" => "claude",
            "codex" => "codex",
            "opencode" => "opencode",
            _ => DefaultProvider
        };

        var processInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory()
        };

        // Explicitly disable sandbox if docker/podman is not available
        processInfo.EnvironmentVariables["GEMINI_SANDBOX"] = "false";

        if (provider == "codex")
        {
            processInfo.ArgumentList.Add("exec");
            processInfo.ArgumentList.Add("--dangerously-bypass-approvals-and-sandbox");
            processInfo.ArgumentList.Add("--color");
            processInfo.ArgumentList.Add("never");
            processInfo.ArgumentList.Add("--json"); // JSONL streaming output with token usage
        }
        else if (provider == "opencode")
        {
            processInfo.ArgumentList.Add("run");
            processInfo.ArgumentList.Add("--format");
            processInfo.ArgumentList.Add("json"); // JSONL output with token usage
        }
        else if (provider == "gh-copilot")
        {
            processInfo.ArgumentList.Add("--allow-all-tools");
            processInfo.ArgumentList.Add("--output-format");
            processInfo.ArgumentList.Add(outputFormat ?? "json");
        }
        else
        {
            processInfo.ArgumentList.Add("-p");
            processInfo.ArgumentList.Add(""); // Headless mode, read from stdin
            
            if (provider == "claude" || fileName == "claude" || provider == "claudecode" || fileName == "claudecode")
            {
                processInfo.ArgumentList.Add("--dangerously-skip-permissions");
                processInfo.ArgumentList.Add("--sandbox");
                processInfo.ArgumentList.Add("false");
                processInfo.ArgumentList.Add("--output-format");
                processInfo.ArgumentList.Add(outputFormat ?? "json");
                // stream-json requires --verbose for Claude CLI
                if (outputFormat == "stream-json")
                    processInfo.ArgumentList.Add("--verbose");
            }
            else
            {
                processInfo.ArgumentList.Add("--yolo");
                processInfo.ArgumentList.Add("--sandbox");
                processInfo.ArgumentList.Add("false");
                if (provider == "gemini" || fileName == "gemini")
                {
                    processInfo.ArgumentList.Add("--output-format");
                    processInfo.ArgumentList.Add(outputFormat ?? "json");
                    // REMOVED --raw-output to ensure we get the JSON envelope with token usage
                }
            }
        }

        return processInfo;
    }

    public async Task<string> GenerateTitleAsync(string userPrompt, string aiResponse, string? provider = null)
    {
        var targetProvider = provider ?? DefaultProvider;
        string prompt = $"""
            以下のやり取りに基づいて、チャットセッションの短いタイトルを生成してください。
            タイトルは5語以内、または15文字程度で、装飾なしのプレーンテキストのみを返してください。
            
            ユーザー: {userPrompt}
            AI: {(aiResponse.Length > 200 ? aiResponse[..200] + "..." : aiResponse)}
            """;

        var result = await ExecuteCliAsync(prompt, targetProvider, systemPrompt: GetSystemPromptTemplate("TitleGenerator", "あなたはチャットタイトルの命名者です。簡潔で適切なタイトルのみを返します。"));
        return result.Output.Trim().Trim('"', '\'').Replace("\n", " ");
    }

    private async Task<CliResult> ExecuteCliAsync(string prompt, string provider, string? systemPrompt = null, string? workingDirectory = null)
    {
        var processInfo = SetupProcessInfo(provider, workingDirectory);

        string inputToStdin = string.IsNullOrEmpty(systemPrompt)
            ? prompt
            : $"{systemPrompt}\n\n{prompt}";

        if (provider == "opencode")
        {
            processInfo.ArgumentList.Add(inputToStdin);
            inputToStdin = string.Empty;
        }
        else if (provider == "gh-copilot")
        {
            processInfo.ArgumentList.Add("-p");
            processInfo.ArgumentList.Add(inputToStdin);
            inputToStdin = string.Empty;
        }

        try
        {
            using var process = Process.Start(processInfo);
            if (process == null) return new CliResult($"Error: Could not start {provider} CLI.", provider, 0, 0, 0);

            using var cts = new CancellationTokenSource();
            _ = Task.Delay(TimeSpan.FromSeconds(TimeoutSeconds), cts.Token).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    try { if (!process.HasExited) process.Kill(true); } catch { }
                }
            }, TaskScheduler.Default);

            await process.StandardInput.WriteAsync(inputToStdin);
            process.StandardInput.Close();

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(); } catch { }
            cts.Cancel();

            if (string.IsNullOrWhiteSpace(output) || (process.ExitCode != 0 && provider != "opencode"))
            {
                if (provider != FallbackProvider)
                {
                    return await ExecuteCliAsync(prompt, FallbackProvider, systemPrompt, workingDirectory);
                }
                
                if (!string.IsNullOrWhiteSpace(error))
                    return new CliResult($"[Error from {provider}]: {ExtractCliError(error, provider)}", provider, 0, 0, 0);
            }

            if (string.IsNullOrWhiteSpace(output)) return new CliResult("No response received from AI.", provider, 0, 0, 0);

            // JSONL path: Many modern CLIs (codex, opencode, claudecode, copilot) output one JSON object per line or as a stream
            // We'll treat all as potential JSONL if they aren't a single valid JSON object.
            bool isJsonl = provider == "codex" || provider == "opencode" || provider == "gh-copilot" || provider == "claude" || provider == "claudecode";
            if (isJsonl)
            {
                string? jsonlContent = null;
                string? jsonlModel = null;
                int jsonlPt = 0, jsonlCt = 0, jsonlTt = 0;
                foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    try
                    {
                        using var lineDoc = JsonDocument.Parse(rawLine.Trim());
                        var root = lineDoc.RootElement;
                        
                        // Extract Model
                        if (root.TryGetProperty("model", out var mProp) || root.TryGetProperty("modelVersion", out mProp))
                        {
                            var mv = mProp.GetString();
                            if (!string.IsNullOrEmpty(mv)) jsonlModel = mv;
                        }

                        // Extract Model from modelUsage
                        if (root.TryGetProperty("modelUsage", out var muEl) && muEl.ValueKind == JsonValueKind.Object)
                        {
                            var names = new List<string>();
                            foreach (var prop in muEl.EnumerateObject())
                            {
                                names.Add(prop.Name);
                                if (prop.Value.TryGetProperty("input_tokens", out var mit)) jsonlPt += mit.GetInt32();
                                if (prop.Value.TryGetProperty("output_tokens", out var mot)) jsonlCt += mot.GetInt32();
                                if (prop.Value.TryGetProperty("total_tokens", out var mtt)) jsonlTt += mtt.GetInt32();
                            }
                            if (names.Any()) jsonlModel = string.Join("+", names);
                        }

                        // Extract Usage (Standard)
                        JsonElement usageProp;
                        bool hasUsage = root.TryGetProperty("usage", out usageProp) ||
                                       root.TryGetProperty("usage_metadata", out usageProp) ||
                                       root.TryGetProperty("usageMetadata", out usageProp);
                        if (!hasUsage && root.TryGetProperty("message", out var msg) && msg.TryGetProperty("usage", out usageProp))
                            hasUsage = true;
                        
                        if (hasUsage)
                        {
                            if (usageProp.TryGetProperty("input_tokens", out var it) || usageProp.TryGetProperty("prompt_tokens", out it) || usageProp.TryGetProperty("prompt_token_count", out it) || usageProp.TryGetProperty("promptTokenCount", out it) || usageProp.TryGetProperty("inputTokenCount", out it)) jsonlPt += it.GetInt32();
                            if (usageProp.TryGetProperty("output_tokens", out var ot) || usageProp.TryGetProperty("completion_tokens", out ot) || usageProp.TryGetProperty("completion_token_count", out ot) || usageProp.TryGetProperty("candidate_token_count", out ot) || usageProp.TryGetProperty("candidatesTokenCount", out ot) || usageProp.TryGetProperty("candidateTokenCount", out ot) || usageProp.TryGetProperty("outputTokenCount", out ot)) jsonlCt += ot.GetInt32();
                            if (usageProp.TryGetProperty("total_tokens", out var tt) || usageProp.TryGetProperty("total_token_count", out tt) || usageProp.TryGetProperty("totalTokenCount", out tt)) jsonlTt += tt.GetInt32();
                        }

                        // Extract Stats (Gemini style)
                        if (root.TryGetProperty("stats", out var statsEl))
                        {
                            if (statsEl.TryGetProperty("input_tokens", out var sit)) jsonlPt += sit.GetInt32();
                            if (statsEl.TryGetProperty("output_tokens", out var sot)) jsonlCt += sot.GetInt32();
                            if (statsEl.TryGetProperty("total_tokens", out var stt)) jsonlTt += stt.GetInt32();
                            if (statsEl.TryGetProperty("models", out var modelsEl) && modelsEl.ValueKind == JsonValueKind.Object)
                            {
                                var names = new List<string>();
                                foreach (var m in modelsEl.EnumerateObject())
                                {
                                    names.Add(m.Name);
                                    if (m.Value.TryGetProperty("tokens", out var tEl))
                                    {
                                        if (tEl.TryGetProperty("input", out var ti)) jsonlPt += ti.GetInt32();
                                        if (tEl.TryGetProperty("candidates", out var tc)) jsonlCt += tc.GetInt32();
                                        if (tEl.TryGetProperty("total", out var tt2)) jsonlTt += tt2.GetInt32();
                                    }
                                }
                                if (names.Any()) jsonlModel = string.Join("+", names);
                            }
                        }

                        // Extract Content
                        if (root.TryGetProperty("type", out var tProp))
                        {
                            var t = tProp.GetString();
                            if (t == "text" && root.TryGetProperty("part", out var op) && op.TryGetProperty("text", out var ot))
                                jsonlContent = (jsonlContent ?? "") + ot.GetString();
                            else if (t == "message" && root.TryGetProperty("content", out var mc))
                                jsonlContent = (jsonlContent ?? "") + mc.GetString();
                            else if (t == "result" && root.TryGetProperty("result", out var rc))
                                jsonlContent = rc.GetString(); // Final result often contains full text
                            else if (t == "item.completed" && root.TryGetProperty("item", out var itm) &&
                                     itm.TryGetProperty("type", out var itmType) && itmType.GetString() == "agent_message" &&
                                     itm.TryGetProperty("text", out var itmText))
                                jsonlContent = itmText.GetString();
                            else if (t == "step_finish" && root.TryGetProperty("part", out var of))
                            {
                                if (of.TryGetProperty("model", out var om)) { var mv = om.GetString(); if (!string.IsNullOrEmpty(mv)) jsonlModel = mv; }
                            }
                        }
                        else if (root.TryGetProperty("response", out var res)) jsonlContent = res.GetString();
                        else if (root.TryGetProperty("result", out var res2)) jsonlContent = res2.GetString();
                        else if (root.TryGetProperty("content", out var res3)) jsonlContent = res3.GetString();
                    }
                    catch (JsonException) { }
                }
                if (jsonlTt == 0 && (jsonlPt > 0 || jsonlCt > 0)) jsonlTt = jsonlPt + jsonlCt;
                if (!string.IsNullOrEmpty(jsonlContent))
                    return new CliResult(CleanResponse(jsonlContent), jsonlModel ?? provider, jsonlPt, jsonlCt, jsonlTt);
            }

            try
            {
                var jsonText = ExtractJson(output);
                if (!string.IsNullOrEmpty(jsonText) && jsonText != "{}")
                {
                    using var doc = JsonDocument.Parse(jsonText);
                    string? extractedModel = null;
                    int pt = 0, ct = 0, tt = 0;

                    // Model: root "model"/"modelVersion" (Gemini), nested "message.model", or "modelUsage" keys (Claude CLI)
                    if (doc.RootElement.TryGetProperty("model", out var mProp) ||
                        doc.RootElement.TryGetProperty("modelVersion", out mProp))
                    {
                        var mv = mProp.GetString();
                        if (!string.IsNullOrEmpty(mv)) extractedModel = mv;
                    }
                    else if (doc.RootElement.TryGetProperty("message", out var msgForModel2) &&
                             msgForModel2.TryGetProperty("model", out var nestedMProp2))
                    {
                        var mv = nestedMProp2.GetString();
                        if (!string.IsNullOrEmpty(mv)) extractedModel = mv;
                    }
                    else if (doc.RootElement.TryGetProperty("modelUsage", out var muEl2) && muEl2.ValueKind == JsonValueKind.Object)
                    {
                        var muNames2 = new List<string>();
                        foreach (var prop in muEl2.EnumerateObject())
                        {
                            muNames2.Add(prop.Name);
                            if (prop.Value.TryGetProperty("input_tokens", out var mit)) pt += mit.GetInt32();
                            if (prop.Value.TryGetProperty("output_tokens", out var mot)) ct += mot.GetInt32();
                            if (prop.Value.TryGetProperty("total_tokens", out var mtt)) tt += mtt.GetInt32();
                        }
                        if (muNames2.Any()) extractedModel = string.Join("+", muNames2);
                        if (tt == 0 && (pt > 0 || ct > 0)) tt = pt + ct;
                    }

                    // Usage: Claude (input_tokens/output_tokens), OpenAI (prompt_tokens), Anthropic API (usageMetadata)
                    JsonElement usageProp2;
                    bool hasUsage2 = doc.RootElement.TryGetProperty("usage", out usageProp2) ||
                                    doc.RootElement.TryGetProperty("usage_metadata", out usageProp2) ||
                                    doc.RootElement.TryGetProperty("usageMetadata", out usageProp2);
                    if (!hasUsage2 && doc.RootElement.TryGetProperty("message", out var msgForUsage2) &&
                        msgForUsage2.TryGetProperty("usage", out usageProp2))
                        hasUsage2 = true;
                    if (hasUsage2)
                    {
                        if (usageProp2.TryGetProperty("input_tokens", out var itProp2) || usageProp2.TryGetProperty("prompt_tokens", out itProp2) || usageProp2.TryGetProperty("prompt_token_count", out itProp2) || usageProp2.TryGetProperty("promptTokenCount", out itProp2) || usageProp2.TryGetProperty("inputTokenCount", out itProp2)) pt = itProp2.GetInt32();
                        if (usageProp2.TryGetProperty("output_tokens", out var otProp2) || usageProp2.TryGetProperty("completion_tokens", out otProp2) || usageProp2.TryGetProperty("completion_token_count", out otProp2) || usageProp2.TryGetProperty("candidate_token_count", out otProp2) || usageProp2.TryGetProperty("candidatesTokenCount", out otProp2) || usageProp2.TryGetProperty("candidateTokenCount", out otProp2) || usageProp2.TryGetProperty("outputTokenCount", out otProp2)) ct = otProp2.GetInt32();
                        if (usageProp2.TryGetProperty("total_tokens", out var ttProp2) || usageProp2.TryGetProperty("total_token_count", out ttProp2) || usageProp2.TryGetProperty("totalTokenCount", out ttProp2)) tt = ttProp2.GetInt32();
                        if (tt == 0 && (pt > 0 || ct > 0)) tt = pt + ct;
                    }
                    // Gemini CLI non-streaming: {"response":"text","stats":{"models":{"gemini-X":{"tokens":{"input":N,"candidates":K,"total":M}}}}}
                    if (doc.RootElement.TryGetProperty("stats", out var stats2El))
                    {
                        if (stats2El.TryGetProperty("input_tokens", out var sit2)) pt = sit2.GetInt32();
                        if (stats2El.TryGetProperty("output_tokens", out var sot2)) ct = sot2.GetInt32();
                        if (stats2El.TryGetProperty("total_tokens", out var stt2)) tt = stt2.GetInt32();

                        if (stats2El.TryGetProperty("models", out var models2El) && models2El.ValueKind == JsonValueKind.Object)
                        {
                            var modelNames2 = new List<string>();
                            foreach (var modelEntry in models2El.EnumerateObject())
                            {
                                modelNames2.Add(modelEntry.Name);
                                if (pt == 0 || ct == 0)
                                {
                                    if (modelEntry.Value.TryGetProperty("tokens", out var tokEl))
                                    {
                                        if (tokEl.TryGetProperty("input", out var tit)) pt += tit.GetInt32();
                                        if (tokEl.TryGetProperty("candidates", out var tct)) ct += tct.GetInt32();
                                        if (tokEl.TryGetProperty("total", out var ttt)) tt += ttt.GetInt32();
                                    }
                                }
                            }
                            if (modelNames2.Any() && string.IsNullOrEmpty(extractedModel))
                                extractedModel = string.Join("+", modelNames2);
                        }
                        if (tt == 0 && (pt > 0 || ct > 0)) tt = pt + ct;
                    }

                    string? content = null;
                    if (doc.RootElement.TryGetProperty("result", out var resultProp2)) content = resultProp2.GetString();       // Claude CLI
                    else if (doc.RootElement.TryGetProperty("response", out var resProp)) content = resProp.GetString();
                    else if (doc.RootElement.TryGetProperty("content", out var contentProp)) content = contentProp.GetString();
                    else if (doc.RootElement.TryGetProperty("text", out var textProp)) content = textProp.GetString();
                    // Gemini API format: candidates[0].content.parts[0].text
                    else if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                             candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0 &&
                             candidates[0].TryGetProperty("content", out var cContent) &&
                             cContent.TryGetProperty("parts", out var parts) &&
                             parts.ValueKind == JsonValueKind.Array && parts.GetArrayLength() > 0 &&
                             parts[0].TryGetProperty("text", out var partText))
                    {
                        content = partText.GetString();
                    }

                    if (content != null)
                        return new CliResult(CleanResponse(content), extractedModel ?? provider, pt, ct, tt);
                }
            }
            catch (JsonException) { }

            return new CliResult(CleanResponse(output), provider, 0, 0, 0);
        }
        catch (Exception ex)
        {
            if (provider != FallbackProvider)
            {
                return await ExecuteCliAsync(prompt, FallbackProvider, systemPrompt, workingDirectory);
            }
            return new CliResult($"[Exception]: {ex.Message}", provider, 0, 0, 0);
        }
    }
}
