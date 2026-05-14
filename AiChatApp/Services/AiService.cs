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

    private static readonly string[] SystemPromptFragments = { 
        "あなたは高度なAIアシスタントです", 
        "你是高度进化的自主 AI 代理",
        "你是高度進化的自主 AI 代理",
        "現在はソフトウェア開発プロジェクトのコンテキストで動作しています",
        "你目前运行在 AiChatApp 项目的上下文中",
        "你目前運行在 AiChatApp 專案的上下文中",
        "你是 Hyperion",
        "专注于软件工程、系统架构 and 自动化开发任务",
        "專注於軟體工程、系統架構 and 自動化開發任務",
        "请始终以 Hyperion 的身份行事",
        "請始終以 Hyperion 的身份行事",
        "你不仅要提供代码",
        "你不僅要提供代碼",
        "理解项目的整体结构",
        "理解專案的整體結構",
        "根据需要自主规划 and 执行文件操作",
        "根據需要自主規劃 and 執行文件操作",
        "明确列出你执行的动作",
        "明確列出你執行的動作",
        "あなたはタスク分解の専門家",
        "あなたは実装の専門家",
        "あなたは評審の専門家",
        "あなたは評価の専門家",
        "你是评估专家",
        "[MEMORY INSTRUCTION]",
        "[会話履歴]:",
        "[ユーザーの既知情報・長期記憶]:",
        "[相关的长期记忆]:",
        "重要な発見や制約があれば",
        "重要的发现或约束",
        "重要的發現或約束",
        "MEMORY: key=value",
        "[ENVIRONMENTAL POLICIES & CONSTRAINTS]:",
        "[当前会话上下文]:",
        "[プロジェクト文脈]:",
        "[利用可能なエージェント役割]:",
        "[有効なスキル指示]:",
        "[追加スキル指示]:",
        "当你与用户交流或执行任务时",
        "當你與用戶交流或執行任務時",
        "根据具体的实现或修改指令",
        "根據具體的實現或修改指令",
        "You are a highly capable AI assistant",
        "You are currently operating in the context of a software development project",
        "Always act as Hyperion",
        "Focus on software engineering, system architecture, and automated development tasks",
        "List the actions you take clearly"
    };

    private readonly ILogger<AiService> _logger;

    public AiService(AppDbContext db, MemorySearchService memorySearch, 
        SessionMemoryService sessionMemory, IServiceProvider serviceProvider, 
        SkillManagerService skillManager, PipelineLoaderService pipelineLoader, 
        SchemaValidationService schemaValidator, ToolExecutorService toolExecutor,
        EvalService evalService, IConfiguration config, ILogger<AiService> logger)
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
        _logger = logger;
    }

    public string DefaultProvider => _config["AiSettings:DefaultProvider"] ?? "gemini";
    public string FallbackProvider => _config["AiSettings:FallbackProvider"] ?? "gemini";
    public int TimeoutSeconds => int.TryParse(_config["AiSettings:TimeoutSeconds"], out var s) ? s : 300;

    private string GetSystemPromptTemplate(string key, string fallback) => 
        _config[$"AiSettings:SystemPrompts:{key}"] ?? fallback;

    /// <summary>获取所有可用的代理定义（统一从 SkillManager 获取）</summary>
    public async Task<List<AgentDefinition>> GetAvailableAgentsAsync(int userId)
    {
        var skills = await _skillManager.GetAllSkillsAsync(userId);
        return skills.Select(s => new AgentDefinition(s.Name, s.DisplayName, s.Description, s.Prompt)).ToList();
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
        await LogAgentStepAsync(messageId, agent?.RoleName ?? "Assistant", modelName, targetProvider, systemPrompt ?? "Default Assistant", fullPrompt, result.Output, (int)sw.ElapsedMilliseconds, result.PromptTokens, result.CompletionTokens, result.TotalTokens);

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
                _ = Task.Run(async () => {
                    try { await _evalService.EvaluateStepAsync(step.Id, task, step.Output, targetProvider); }
                    catch (Exception ex) { _logger.LogError(ex, "EvaluateStepAsync failed for step {StepId}", step.Id); }
                });

                steps.Add(step);
                lastOutput = step.Output;
                if (onStepComplete != null) await onStepComplete(step.Role, BuildStepHtml(step));
            }
            string html = BuildCooperativeHtml(steps, lastOutput);

            if (chatSessionId.HasValue)
            {
                _ = Task.Run(async () => {
                    try {
                        await _sessionMemory.PromoteToLongTermAsync(chatSessionId.Value, userId);
                        await _skillLearning.LearnFromInteractionAsync(task, lastOutput, steps, userId);
                    } catch (Exception ex) { _logger.LogError(ex, "PromoteToLongTerm/LearnFromInteraction failed for session {SessionId}", chatSessionId.Value); }
                });
            }

            return (html, steps);
        }

        // 2. 动态パイプライン模式 (Harness Engineering)
        var pipeline = _pipelineLoader.Get("default") ?? throw new Exception("Default pipeline not found.");
        string currentInput = task;
        string contextFromPreviousStages = "";
        OrchestratorPlan? activePlan = null;
        TaskBlackboard?   activeBoard = null;

        foreach (var stage in pipeline.Stages)
        {
            if (stage.IsOptional && string.IsNullOrEmpty(currentInput)) continue;

            // If task graph was executed, skip all non-reviewer intermediate stages
            if (activePlan != null && stage.Role != "reviewer") continue;

            AgentStep? stageStep = null;
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
                _ = Task.Run(async () => {
                    try { await _evalService.EvaluateStepAsync(stageStep.Id, task, stageStep.Output, stage.Provider ?? provider); }
                    catch (Exception ex) { _logger.LogError(ex, "EvaluateStepAsync failed for step {StepId}", stageStep.Id); }
                });

                break;
            }

            if (stageStep is null) continue;   // skip stage if somehow never ran
            steps.Add(stageStep);
            if (onStepComplete != null) await onStepComplete(stageStep.Role, BuildStepHtml(stageStep));

            // --- Task Graph: after Orchestrator, attempt parallel DAG execution ---
            if (stage.Role == "orchestrator" && stageStep.WasAccepted)
            {
                var plan = TryParseOrchestratorPlan(stageStep.Output);
                if (plan != null)
                {
                    activePlan  = plan;
                    activeBoard = await ExecuteTaskGraphAsync(plan, task, messageId, targetProvider, userId, chatSessionId, onStepComplete, steps);
                    currentInput = BuildTaskGraphReviewInput(plan, activeBoard, task);
                    contextFromPreviousStages = $"Orchestrator plan:\n{stageStep.Output}";
                    continue; // advance to Reviewer
                }
            }

            contextFromPreviousStages += $"\n--- Stage: {stage.Name} ---\n{stageStep.Output}\n";
            currentInput = stageStep.Output;

            if (stage.IsFinalStage) break;
        }

        if (!steps.Any())
            return (BuildCooperativeHtml(new List<AgentStep>(), task), steps);

        // ── Directed revision: if Reviewer requested changes, re-run only affected subtasks ──
        if (activePlan != null && activeBoard != null)
        {
            var reviewerStep = steps.LastOrDefault(s =>
                s.Role.Equals("Reviewer", StringComparison.OrdinalIgnoreCase));

            if (reviewerStep != null)
            {
                var feedback = TryParseReviewerFeedback(reviewerStep.Output);
                if (feedback?.OverallVerdict is "revision_needed" or "failed")
                {
                    var revised = await ReviseFailedSubtasksAsync(
                        activePlan, feedback, activeBoard,
                        messageId, targetProvider, userId, chatSessionId,
                        onStepComplete, steps);

                    if (revised)
                    {
                        // Re-run Reviewer with updated blackboard
                        var reviewPersona = await _pipelineLoader.GetPromptTemplateAsync("stage_reviewer.md");
                        var reviewInput   = BuildTaskGraphReviewInput(activePlan, activeBoard, task);
                        var newReviewStep = await RunAgentStepAsync(
                            "Reviewer", reviewPersona, reviewInput,
                            messageId, targetProvider, userId, chatSessionId, attemptNumber: 2);
                        newReviewStep.WasAccepted = true;
                        await _db.SaveChangesAsync();
                        steps.Add(newReviewStep);
                        if (onStepComplete != null)
                            await onStepComplete(newReviewStep.Role, BuildStepHtml(newReviewStep));
                    }
                }
            }
        }

        string finalResult = steps.Last().Output;
        if (chatSessionId.HasValue)
        {
            _ = Task.Run(async () => {
                try {
                    await _sessionMemory.PromoteToLongTermAsync(chatSessionId.Value, userId);
                    await _skillLearning.LearnFromInteractionAsync(task, finalResult, steps, userId);
                } catch (Exception ex) { _logger.LogError(ex, "PromoteToLongTerm/LearnFromInteraction failed for session {SessionId}", chatSessionId.Value); }
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

        // Pre-process prompt to add @ prefix to existing image files for Vision support
        string processedPrompt = fullPrompt;
        try
        {
            var workingDirForVision = workingDir ?? Directory.GetCurrentDirectory();
            var matches = Regex.Matches(fullPrompt, @"(\b[\w\-\.]+\.(png|jpg|jpeg|webp|gif|bmp)\b)", RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                var visionFileName = match.Value;
                if (!visionFileName.StartsWith("@"))
                {
                    var fullPath = Path.Combine(workingDirForVision, visionFileName);
                    if (File.Exists(fullPath))
                    {
                        processedPrompt = processedPrompt.Replace(visionFileName, "@" + visionFileName);
                    }
                }
            }
        }
        catch { /* Ignore processing errors */ }

        if (agent?.PreferredProvider != null) targetProvider = agent.PreferredProvider;

        string fileName = targetProvider switch
        {
            "copilot" => "copilot",
            "claudecode" => "claudecode",
            "claude" => "claude",
            "codex" => "codex",
            "opencode" => "opencode",
            _ => DefaultProvider
        };
        var useJsonStreaming = targetProvider == "gemini" || fileName == "gemini" ||
                              targetProvider == "claude" || fileName == "claude" ||
                              targetProvider == "claudecode" || fileName == "claudecode" ||
                              targetProvider == "copilot" || fileName == "copilot" ||
                              targetProvider == "codex" || fileName == "codex" ||
                              targetProvider == "opencode" || fileName == "opencode";

        var processInfo = SetupProcessInfo(targetProvider, workingDir, useJsonStreaming ? "stream-json" : null);

        // 获取 MessageId 用于记录日志
        int messageId = await GetLatestUserMessageIdAsync(chatSessionId);

        string inputToStdin = string.IsNullOrEmpty(systemPrompt)
            ? processedPrompt
            : $"{systemPrompt}\n\n{processedPrompt}";

        if (targetProvider == "opencode")
        {
            processInfo.ArgumentList.Add(inputToStdin);
            inputToStdin = string.Empty;
        }
        else if (targetProvider == "copilot")
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
        const int maxPrefixBuffer = 16384;

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
                    var root = doc.RootElement;

                    // Use the helper to extract tokens and model
                    TryParseJsonElement(root, ref chunk, ref extractedModel, ref pt, ref ct, ref tt, isIncremental: false);

                    // Text extraction — special handling for deltas in streaming
                    if (root.TryGetProperty("type", out var typeProp))
                    {
                        var type = typeProp.GetString();
                        if (type == "content_block_delta" || type == "text_delta")
                        {
                            if (root.TryGetProperty("delta", out var deltaProp) &&
                                deltaProp.TryGetProperty("text", out var textProp))
                                chunk = textProp.GetString() ?? "";
                        }
                        else if (type == "assistant.message_delta" && root.TryGetProperty("data", out var d) && d.TryGetProperty("deltaContent", out var dc))
                        {
                            chunk = dc.GetString() ?? "";
                        }
                    }
                }
                catch (JsonException) { /* Skip invalid JSON lines */ }

                if (chunk != null)
                {
                    fullResponse.Append(chunk);
                    var (toYield, handled) = HandlePrefixBuffer(prefixBuffer, chunk, prefixHandled, maxPrefixBuffer, systemPrompt, fullPrompt);
                    prefixHandled = handled;
                    if (toYield != null) yield return toYield;
                }
            }
        }
        else
        {
            var buffer = new char[64];
            int read;
            while ((read = await process.StandardOutput.ReadAsync(buffer,0, buffer.Length)) > 0)
            {
                var chunk = new string(buffer,0, read);
                fullResponse.Append(chunk);
                var (toYield, handled) = HandlePrefixBuffer(prefixBuffer, chunk, prefixHandled, maxPrefixBuffer, systemPrompt, fullPrompt);
                prefixHandled = handled;
                if (toYield != null) yield return toYield;
            }
        }
        if (!prefixHandled && prefixBuffer.Length > 0)
        {
            var stripped = StripEchoedPromptPrefix(prefixBuffer.ToString(), systemPrompt, fullPrompt);
            if (!string.IsNullOrEmpty(stripped)) yield return stripped;
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
        var providerForLog = extractedModel != null && (extractedModel.Contains("gemini") || extractedModel.Contains("claude") || extractedModel.Contains("copilot") || extractedModel.Contains("codex") || extractedModel.Contains("opencode"))
            ? extractedModel.ToLower() : targetProvider;
        await LogAgentStepAsync(messageId, agent?.RoleName ?? "Assistant", extractedModel ?? targetProvider, targetProvider, systemPrompt ?? "Default Assistant", fullPrompt, fullResponse.ToString(), (int)sw.ElapsedMilliseconds, pt, ct, tt);
    }

    public async Task<string> ExecuteCliDirectAsync(string prompt, string provider, string? systemPrompt = null, string? workingDir = null)
    {
        var result = await ExecuteCliAsync(prompt, provider, systemPrompt, workingDir);
        return result.Output;
    }

    /// <summary>
    /// 使用主动式代理配置执行 AI 任务（哨兵、记录员、主脑）。
    /// </summary>
    public async Task<string> ExecuteProactiveAgentAsync(ProactiveAgentProfile profile, string prompt, int? userId = null, int? chatSessionId = null, string? provider = null)
    {
        var targetProvider = provider ?? profile.PreferredProvider ?? DefaultProvider;
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
                await LogAgentStepAsync(messageId, profile.Role, result.Model, targetProvider, sb.ToString(), prompt, result.Output, (int)sw.ElapsedMilliseconds, result.PromptTokens, result.CompletionTokens, result.TotalTokens);
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

    private async Task LogAgentStepAsync(int messageId, string role, string model, string provider, string persona, string input, string output, int durationMs, int promptTokens = 0, int completionTokens = 0, int totalTokens = 0)
    {
        if (messageId <= 0) return;
        var step = new AgentStep
        {
            MessageId = messageId,
            Role = role,
            Model = model,
            Provider = provider.ToLower(),
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
            Provider = provider.ToLower(),
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

    private static HashSet<string> BuildDynamicFragments(string? systemPrompt, string? userPrompt = null)
    {
        var dynamicFragments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            foreach (var line in systemPrompt.Split('\n'))
            {
                var tl = line.Trim();
                if (tl.Length > 3) dynamicFragments.Add(tl);
            }
        }
        if (!string.IsNullOrEmpty(userPrompt))
        {
            foreach (var line in userPrompt.Split('\n'))
            {
                var tl = line.Trim();
                if (tl.Length > 3) dynamicFragments.Add(tl);
            }
        }
        return dynamicFragments;
    }

    private static string StripEchoedPromptPrefix(string text, string? systemPrompt = null, string? userPrompt = null)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var lines = text.Split('\n');
        int firstContentLine = -1;
        string? firstLineContent = null;
        
        var dynamicFragments = BuildDynamicFragments(systemPrompt, userPrompt);

        for (int i = 0; i < Math.Min(lines.Length, 300); i++)
        {
            var trimmedLine = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine)) continue;

            bool isPrefixHeader = false;
            string? matchedPrefix = null;
            foreach (var p in PromptPrefixes)
            {
                if (trimmedLine.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                {
                    isPrefixHeader = true;
                    matchedPrefix = p;
                    break;
                }
            }

            bool isSystemFragment = SystemPromptFragments.Any(f => trimmedLine.Contains(f, StringComparison.OrdinalIgnoreCase)) ||
                                   dynamicFragments.Contains(trimmedLine);

            if (isPrefixHeader)
            {
                // If the FULL line (including prefix) is an exact match of a dynamic fragment,
                // it's echoed system prompt content — skip it entirely
                if (dynamicFragments.Contains(trimmedLine))
                {
                    continue;
                }

                // Check if there is content AFTER the prefix on the SAME line
                var contentAfter = trimmedLine.Substring(matchedPrefix!.Length).Trim();
                if (!string.IsNullOrEmpty(contentAfter))
                {
                    // If the content after the prefix is also a known system fragment, skip it
                    if (SystemPromptFragments.Any(f => contentAfter.Contains(f, StringComparison.OrdinalIgnoreCase)) ||
                        dynamicFragments.Any(f => contentAfter.Contains(f, StringComparison.OrdinalIgnoreCase)))
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

        // All lines matched system prompt fragments — return empty, nothing is real content
        return string.Empty;
    }

    /// <summary>
    /// Handles the prefix-stripping buffer for streaming output.
    /// Returns (chunk to yield or null, whether prefix handling is now complete).
    /// </summary>
    private static (string? toYield, bool handled) HandlePrefixBuffer(
        StringBuilder prefixBuffer, string chunk, bool prefixHandled, int maxBuffer, string? systemPrompt = null, string? userPrompt = null)
    {
        if (prefixHandled)
            return (chunk, true);

        prefixBuffer.Append(chunk);
        var buf = prefixBuffer.ToString();

        // Build dynamic fragments from actual system prompt for prefix detection
        var dynamicFrags = BuildDynamicFragments(systemPrompt, userPrompt);

        bool startsWithPrefix =
            PromptPrefixes.Any(p => buf.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ||
            SystemPromptFragments.Any(f => buf.StartsWith(f, StringComparison.OrdinalIgnoreCase)) ||
            dynamicFrags.Any(f => buf.StartsWith(f, StringComparison.OrdinalIgnoreCase));

        bool couldBeginPrefix =
            PromptPrefixes.Any(p => p.StartsWith(buf, StringComparison.OrdinalIgnoreCase)) ||
            SystemPromptFragments.Any(f => f.StartsWith(buf, StringComparison.OrdinalIgnoreCase)) ||
            dynamicFrags.Any(f => f.StartsWith(buf, StringComparison.OrdinalIgnoreCase));

        if (!startsWithPrefix && !couldBeginPrefix)
        {
            // Run thorough stripping before flushing — content may be echoed system prompt
            // that doesn't match static prefix arrays (e.g. dynamic policy lines)
            var stripped = StripEchoedPromptPrefix(buf, systemPrompt, userPrompt);
            prefixBuffer.Clear();
            return (string.IsNullOrEmpty(stripped) ? null : stripped, true);
        }

        if (startsWithPrefix &&
            (buf.Contains("\nUser:") || buf.Contains("\nAssistant:") ||
             buf.Contains("\nAssistant ") || buf.Contains("\n[")))
        {
            var stripped = StripEchoedPromptPrefix(buf, systemPrompt, userPrompt);
            prefixBuffer.Clear();
            return (string.IsNullOrEmpty(stripped) ? null : stripped, true);
        }

        if (buf.Length >= maxBuffer)
        {
            var stripped = StripEchoedPromptPrefix(buf, systemPrompt, userPrompt);
            prefixBuffer.Clear();
            return (string.IsNullOrEmpty(stripped) ? null : stripped, true);
        }

        return (null, false); // still buffering
    }

    // ═══════════════════════════════════════════════════════════════
    // Task Graph  —  Orchestrator → DAG parallel execution → Review
    // ═══════════════════════════════════════════════════════════════

    // ── Data model ──────────────────────────────────────────────────

    private record SubtaskDef(string Id, string Title, string Agent, string Task, string ExpectedOutput, List<string> Deps);
    private record OrchestratorPlan(string Goal, List<SubtaskDef> Subtasks, string ExecutionNote);

    // Blackboard: shared typed workspace every agent reads from / writes to
    private record BlackboardArtifact(string SubtaskId, string AgentRole, string Content,
                                      string ArtifactType, int RevisionNumber, DateTime CreatedAt);

    private sealed class TaskBlackboard
    {
        private readonly Dictionary<string, BlackboardArtifact> _store = new();

        public void Write(string subtaskId, string agentRole, string content,
                          string artifactType = "text", int revision = 0)
            => _store[subtaskId] = new BlackboardArtifact(subtaskId, agentRole, content,
                                                          artifactType, revision, DateTime.UtcNow);

        public BlackboardArtifact? Read(string subtaskId)
            => _store.TryGetValue(subtaskId, out var a) ? a : null;

        // Builds the "upstream context" block shown to an agent for its listed deps.
        public string BuildDepContext(List<string> deps, List<SubtaskDef> allSubtasks)
        {
            if (!deps.Any()) return "";
            var sb = new StringBuilder("\n## Context from upstream tasks:");
            foreach (var dep in deps)
            {
                var artifact = Read(dep);
                if (artifact == null) continue;
                var def = allSubtasks.FirstOrDefault(s => s.Id == dep);
                sb.AppendLine($"\n### [{dep}] {def?.Title ?? dep} (by {artifact.AgentRole}):");
                sb.AppendLine(artifact.Content);
            }
            return sb.ToString();
        }
    }

    // Reviewer structured output
    private record ReviewIssue(string Severity, string Description, string Suggestion);
    private record SubtaskReview(string SubtaskId, string Verdict, double Score, List<ReviewIssue> Issues);
    private record ReviewerFeedback(string OverallVerdict, double FinalScore,
                                    List<SubtaskReview> SubtaskReviews, string Summary);

    // ── Parsing ─────────────────────────────────────────────────────

    private OrchestratorPlan? TryParseOrchestratorPlan(string output)
    {
        var json = ExtractJson(output);
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("subtasks", out var stEl)) return null;

            var subtasks = new List<SubtaskDef>();
            foreach (var st in stEl.EnumerateArray())
            {
                var id    = st.TryGetProperty("id",             out var v) ? v.GetString() ?? "" : "";
                var title = st.TryGetProperty("title",          out v)     ? v.GetString() ?? id : id;
                var agent = st.TryGetProperty("agent",          out v)     ? v.GetString() ?? "Executor" : "Executor";
                var task  = st.TryGetProperty("task",           out v)     ? v.GetString() ?? "" : "";
                var exp   = st.TryGetProperty("expectedOutput", out v)     ? v.GetString() ?? "" : "";
                var deps  = new List<string>();
                if (st.TryGetProperty("deps", out var depsEl))
                    foreach (var d in depsEl.EnumerateArray())
                        deps.Add(d.GetString() ?? "");
                if (!string.IsNullOrEmpty(id))
                    subtasks.Add(new SubtaskDef(id, title, agent, task, exp, deps));
            }
            if (!subtasks.Any()) return null;

            var goal = root.TryGetProperty("goal",          out var gv) ? gv.GetString() ?? "" : "";
            var note = root.TryGetProperty("executionNote", out var nv) ? nv.GetString() ?? "" : "";
            return new OrchestratorPlan(goal, subtasks, note);
        }
        catch { return null; }
    }

    private ReviewerFeedback? TryParseReviewerFeedback(string output)
    {
        var json = ExtractJson(output);
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var verdict    = root.TryGetProperty("overallVerdict", out var vv) ? vv.GetString() ?? "" : "";
            var score      = root.TryGetProperty("finalScore",      out var sv) ? sv.GetDouble() : 0.0;
            var summary    = root.TryGetProperty("summary",         out var sumv) ? sumv.GetString() ?? "" : "";
            var reviews    = new List<SubtaskReview>();

            if (root.TryGetProperty("subtaskReviews", out var revEl))
            {
                foreach (var r in revEl.EnumerateArray())
                {
                    var stId    = r.TryGetProperty("subtaskId", out var idv) ? idv.GetString() ?? "" : "";
                    var stVerdict = r.TryGetProperty("verdict",   out var rdv) ? rdv.GetString() ?? "" : "";
                    var stScore = r.TryGetProperty("score",      out var scv) ? scv.GetDouble() : 0.0;
                    var issues  = new List<ReviewIssue>();
                    if (r.TryGetProperty("issues", out var issEl))
                        foreach (var iss in issEl.EnumerateArray())
                            issues.Add(new ReviewIssue(
                                iss.TryGetProperty("severity",    out var sev) ? sev.GetString() ?? "" : "",
                                iss.TryGetProperty("description", out var dsc) ? dsc.GetString() ?? "" : "",
                                iss.TryGetProperty("suggestion",  out var sug) ? sug.GetString() ?? "" : ""
                            ));
                    if (!string.IsNullOrEmpty(stId))
                        reviews.Add(new SubtaskReview(stId, stVerdict, stScore, issues));
                }
            }
            return new ReviewerFeedback(verdict, score, reviews, summary);
        }
        catch { return null; }
    }

    // ── Scheduling ──────────────────────────────────────────────────

    // Returns subtasks grouped into parallel layers (topological sort).
    private static List<List<SubtaskDef>> TopologicalLayers(List<SubtaskDef> subtasks)
    {
        var remaining = subtasks.ToList();
        var completed = new HashSet<string>();
        var layers    = new List<List<SubtaskDef>>();

        while (remaining.Count > 0)
        {
            var ready = remaining.Where(s => s.Deps.All(d => completed.Contains(d))).ToList();
            if (!ready.Any()) break; // circular dependency guard
            layers.Add(ready);
            foreach (var s in ready) { remaining.Remove(s); completed.Add(s.Id); }
        }
        return layers;
    }

    // Given a set of directly-failed subtask IDs, propagate to all dependents.
    private static HashSet<string> GetSubtasksToRevise(List<SubtaskDef> all, HashSet<string> failed)
    {
        var toRevise = new HashSet<string>(failed);
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var st in all)
                if (!toRevise.Contains(st.Id) && st.Deps.Any(d => toRevise.Contains(d)))
                { toRevise.Add(st.Id); changed = true; }
        }
        return toRevise;
    }

    // ── Execution ───────────────────────────────────────────────────

    private string BuildAgentInput(SubtaskDef subtask, TaskBlackboard board,
                                   OrchestratorPlan plan, string? reviewerIssues = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Your Assignment: {subtask.Title}");
        sb.AppendLine($"**Task:** {subtask.Task}");
        sb.AppendLine($"**Expected Output:** {subtask.ExpectedOutput}");
        sb.Append(board.BuildDepContext(subtask.Deps, plan.Subtasks));

        if (!string.IsNullOrEmpty(reviewerIssues))
        {
            sb.AppendLine("\n## Revision Required — Reviewer Feedback:");
            sb.AppendLine(reviewerIssues);
            sb.AppendLine("\nPlease address ALL issues above in your revised output.");
        }
        return sb.ToString();
    }

    private async Task RunSubtaskLayerAsync(
        List<SubtaskDef> layer, OrchestratorPlan plan, TaskBlackboard board,
        int messageId, string provider, int userId, int? chatSessionId,
        Func<string, string, Task>? onStepComplete, List<AgentStep> steps,
        List<AgentDefinition> allAgents, string? projectRoot,
        Dictionary<string, string>? reviewerIssuesPerTask = null,
        int revisionNumber = 0)
    {
        var layerTasks = layer.Select(async subtask =>
        {
            var agentDef = allAgents.FirstOrDefault(a =>
                a.Name.Equals(subtask.Agent, StringComparison.OrdinalIgnoreCase))
                ?? new AgentDefinition(subtask.Agent, subtask.Agent, "", "You are a helpful AI assistant.");

            string? issues = null;
            reviewerIssuesPerTask?.TryGetValue(subtask.Id, out issues);
            var input = BuildAgentInput(subtask, board, plan, issues);

            var step = await RunAgentStepAsync(
                role: agentDef.Name,
                persona: agentDef.SystemPrompt,
                input: input,
                messageId: messageId,
                provider: provider,
                userId: userId,
                chatSessionId: chatSessionId,
                attemptNumber: revisionNumber + 1
            );

            var toolOut = await _toolExecutor.ExecuteToolsAsync(step.Output, projectRoot);
            if (toolOut != step.Output) { step.Output = toolOut; await _db.SaveChangesAsync(); }

            board.Write(subtask.Id, agentDef.Name, step.Output, revision: revisionNumber);

            _ = Task.Run(async () => {
                try { await _evalService.EvaluateStepAsync(step.Id, subtask.Task, step.Output, provider); }
                catch (Exception ex) { _logger.LogError(ex, "EvaluateStepAsync failed for step {StepId}", step.Id); }
            });

            return (subtask.Id, step);
        }).ToList();

        var results = await Task.WhenAll(layerTasks);
        foreach (var (_, step) in results)
        {
            steps.Add(step);
            if (onStepComplete != null) await onStepComplete(step.Role, BuildStepHtml(step));
        }
    }

    private async Task<TaskBlackboard> ExecuteTaskGraphAsync(
        OrchestratorPlan plan, string originalTask, int messageId,
        string defaultProvider, int userId, int? chatSessionId,
        Func<string, string, Task>? onStepComplete, List<AgentStep> steps)
    {
        var allAgents   = await GetAvailableAgentsAsync(userId);
        var projectRoot = await GetProjectRootAsync(chatSessionId);
        var board       = new TaskBlackboard();
        var layers      = TopologicalLayers(plan.Subtasks);

        foreach (var layer in layers)
            await RunSubtaskLayerAsync(layer, plan, board, messageId, defaultProvider,
                userId, chatSessionId, onStepComplete, steps, allAgents, projectRoot);

        return board;
    }

    // One directed revision pass: re-runs only failed subtasks (+ dependents).
    private async Task<bool> ReviseFailedSubtasksAsync(
        OrchestratorPlan plan, ReviewerFeedback feedback, TaskBlackboard board,
        int messageId, string defaultProvider, int userId, int? chatSessionId,
        Func<string, string, Task>? onStepComplete, List<AgentStep> steps)
    {
        var failedIds = feedback.SubtaskReviews
            .Where(r => r.Verdict is "revision_needed" or "failed")
            .Select(r => r.SubtaskId)
            .ToHashSet();

        if (!failedIds.Any()) return false;

        // Propagate failures to dependents so they re-run with fresh upstream output
        var toRevise = GetSubtasksToRevise(plan.Subtasks, failedIds);

        // Build per-task issue summary from Reviewer feedback
        var issuesMap = feedback.SubtaskReviews
            .Where(r => toRevise.Contains(r.SubtaskId) && r.Issues.Any())
            .ToDictionary(
                r => r.SubtaskId,
                r => string.Join("\n", r.Issues.Select(i =>
                    $"- [{i.Severity.ToUpper()}] {i.Description}\n  → Suggestion: {i.Suggestion}"))
            );

        var subtasksToRevise = plan.Subtasks.Where(s => toRevise.Contains(s.Id)).ToList();
        var layers = TopologicalLayers(subtasksToRevise);
        var allAgents = await GetAvailableAgentsAsync(userId);
        var projectRoot = await GetProjectRootAsync(chatSessionId);

        _logger.LogInformation("Directing revision for {Count} subtasks: {Ids}",
            toRevise.Count, string.Join(", ", toRevise));

        foreach (var layer in layers)
            await RunSubtaskLayerAsync(layer, plan, board, messageId, defaultProvider,
                userId, chatSessionId, onStepComplete, steps, allAgents, projectRoot,
                reviewerIssuesPerTask: issuesMap, revisionNumber: 1);

        return true;
    }

    // ── Review input builders ────────────────────────────────────────

    private string BuildTaskGraphReviewInput(OrchestratorPlan plan, TaskBlackboard board, string originalTask)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Original Task\n{originalTask}");
        sb.AppendLine($"\n## Orchestrator Goal\n{plan.Goal}");
        if (!string.IsNullOrEmpty(plan.ExecutionNote))
            sb.AppendLine($"\n## Execution Note\n{plan.ExecutionNote}");
        sb.AppendLine("\n## Subtask Results");
        foreach (var subtask in plan.Subtasks)
        {
            var artifact = board.Read(subtask.Id);
            sb.AppendLine($"\n### [{subtask.Id}] {subtask.Title} (agent: {subtask.Agent})");
            sb.AppendLine($"**Expected:** {subtask.ExpectedOutput}");
            if (artifact != null && artifact.RevisionNumber > 0)
                sb.AppendLine($"**(Revision {artifact.RevisionNumber})**");
            sb.AppendLine($"**Output:**\n{artifact?.Content ?? "(no output)"}");
        }
        return sb.ToString();
    }

    private static string CleanResponse(string text, string? systemPrompt = null, string? userPrompt = null)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // 1. Strip echoed System:/User: prompt prefix (Multi-line heuristic)
        text = StripEchoedPromptPrefix(text, systemPrompt, userPrompt);

        // 2. Remove XML-style thinking/thought tags
        text = Regex.Replace(text, @"<(thinking|thought|thought_process)>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<(thinking|thought|thought_process)>.*", "", RegexOptions.Singleline | RegexOptions.IgnoreCase); // Handle unclosed tags

        // 3. Remove "Thought:" or "Thinking:" blocks
        text = Regex.Replace(text, @"(^(Thought|Thinking|Reasoning):.*?\n\n)", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\n\n(Thought|Thinking|Reasoning):.*?\n\n", "\n\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"(Thought|Thinking|Reasoning):.*?$", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // 4. Aggressively and repeatedly strip any leading system fragments or prefixes from the start
        var cleanDynamicFrags = BuildDynamicFragments(systemPrompt, userPrompt);
        bool changed;
        do
        {
            changed = false;
            text = text.Trim();
            
            // Check for explicit prefixes (e.g., "System:", "Assistant:")
            foreach (var p in PromptPrefixes)
            {
                if (text.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                {
                    text = text.Substring(p.Length).Trim();
                    changed = true;
                    break;
                }
            }
            if (changed) continue;

            // Check for known system prompt fragments
            foreach (var f in SystemPromptFragments)
            {
                if (text.StartsWith(f, StringComparison.OrdinalIgnoreCase))
                {
                    text = text.Substring(f.Length).Trim();
                    changed = true;
                    break;
                }
            }
            if (changed) continue;

            // Check for dynamic fragments from the actual system prompt
            foreach (var f in cleanDynamicFrags)
            {
                if (text.StartsWith(f, StringComparison.OrdinalIgnoreCase))
                {
                    text = text.Substring(f.Length).Trim();
                    changed = true;
                    break;
                }
            }

            // Strip common punctuation that might remain after a prefix
            if (text.StartsWith(":") || text.StartsWith("-") || text.StartsWith(" "))
            {
                text = text.Substring(1).Trim();
                changed = true;
            }

        } while (changed && !string.IsNullOrEmpty(text));

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

        if (provider == "copilot" &&
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
        string fileName = provider.ToLower() switch
        {
            "copilot" => "copilot",
            "claudecode" => "claudecode",
            "claude" => "claude",
            "codex" => "codex",
            "opencode" => "opencode",
            "gemini" => "gemini",
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
        // Trust the workspace so --yolo mode is not overridden in headless/automated environments
        processInfo.EnvironmentVariables["GEMINI_CLI_TRUST_WORKSPACE"] = "true";

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
        else if (provider == "copilot")
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
                    
                    // If provider is a specific model name (not just "gemini"), pass it as -m
                    if (provider.ToLower() != "gemini")
                    {
                        processInfo.ArgumentList.Add("-m");
                        processInfo.ArgumentList.Add(provider);
                    }
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
        // Pre-process prompt to add @ prefix to existing image files for Vision support
        string processedPrompt = prompt;
        try
        {
            var workingDir = workingDirectory ?? Directory.GetCurrentDirectory();
            var matches = Regex.Matches(prompt, @"(\b[\w\-\.]+\.(png|jpg|jpeg|webp|gif|bmp)\b)", RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                var fileName = match.Value;
                if (!fileName.StartsWith("@"))
                {
                    var fullPath = Path.Combine(workingDir, fileName);
                    if (File.Exists(fullPath))
                    {
                        processedPrompt = processedPrompt.Replace(fileName, "@" + fileName);
                    }
                }
            }
        }
        catch { /* Ignore processing errors */ }

        var processInfo = SetupProcessInfo(provider, workingDirectory);

        string inputToStdin = string.IsNullOrEmpty(systemPrompt)
            ? processedPrompt
            : $"{systemPrompt}\n\n{processedPrompt}";

        if (provider == "opencode")
        {
            processInfo.ArgumentList.Add(inputToStdin);
            inputToStdin = string.Empty;
        }
        else if (provider == "copilot")
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

            var parsed = ParseCliOutput(output, provider, systemPrompt, prompt);
            if (parsed.TotalTokens > 0 || !string.IsNullOrEmpty(parsed.Output) && parsed.Output != output)
            {
                return parsed;
            }

            return new CliResult(CleanResponse(output, systemPrompt, prompt), provider, 0, 0, 0);
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

    private CliResult ParseCliOutput(string output, string provider, string? systemPrompt = null, string? userPrompt = null)
    {
        if (string.IsNullOrWhiteSpace(output)) return new CliResult(string.Empty, provider, 0, 0, 0);

        string? finalContent = null;
        string? finalModel = null;
        int totalPt = 0, totalCt = 0, totalTt = 0;
        bool foundAnyJson = false;

        // Diagnostic log
        Console.WriteLine($"[AiService] Parsing output from {provider} ({output.Length} chars).");

        // Strategy 1: Try parsing the ENTIRE output as a single JSON object (for pretty-printed outputs)
        var fullJsonText = ExtractJson(output);
        if (!string.IsNullOrEmpty(fullJsonText) && fullJsonText != "{}")
        {
            try
            {
                using var doc = JsonDocument.Parse(fullJsonText);
                if (TryParseJsonElement(doc.RootElement, ref finalContent, ref finalModel, ref totalPt, ref totalCt, ref totalTt))
                {
                    foundAnyJson = true;
                }
            }
            catch (JsonException) { /* Fallback to line-by-line */ }
        }

        // Strategy 2: If Strategy 1 didn't find enough, or to handle JSONL, try line-by-line
        if (!foundAnyJson || (totalTt == 0 && string.IsNullOrEmpty(finalContent)))
        {
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var jsonText = ExtractJson(line);
                if (string.IsNullOrEmpty(jsonText) || jsonText == "{}") continue;

                try
                {
                    using var doc = JsonDocument.Parse(jsonText);
                    if (TryParseJsonElement(doc.RootElement, ref finalContent, ref finalModel, ref totalPt, ref totalCt, ref totalTt, isIncremental: true))
                    {
                        foundAnyJson = true;
                    }
                }
                catch (JsonException) { }
            }
        }

        if (totalTt == 0 && (totalPt > 0 || totalCt > 0)) totalTt = totalPt + totalCt;

        if (foundAnyJson)
        {
            // If it's Copilot, the model might be "gpt-4o" or similar, we might want to keep it or prefix it
            var res = new CliResult(CleanResponse(finalContent ?? output, systemPrompt, userPrompt), finalModel ?? provider, totalPt, totalCt, totalTt);
            Console.WriteLine($"[AiService] Successfully parsed JSON from {provider}. Model: {res.Model}, Tokens: {res.TotalTokens}");
            return res;
        }

        Console.WriteLine($"[AiService] No valid JSON found in output from {provider}.");
        return new CliResult(CleanResponse(output, systemPrompt, userPrompt), provider, 0, 0, 0);
    }

    private bool TryParseJsonElement(JsonElement root, ref string? content, ref string? model, ref int pt, ref int ct, ref int tt, bool isIncremental = false)
    {
        bool foundSomething = false;

        // 1. Extract Model
        if (root.TryGetProperty("model", out var mProp) || root.TryGetProperty("modelVersion", out mProp))
        {
            var mv = mProp.GetString();
            if (!string.IsNullOrEmpty(mv)) { model = mv; foundSomething = true; }
        }
        
        // Copilot / MCP tools updated event
        if (root.TryGetProperty("type", out var tProp) && tProp.GetString() == "session.tools_updated")
        {
            if (root.TryGetProperty("data", out var d) && d.TryGetProperty("model", out var m))
            {
                var mv = m.GetString();
                if (!string.IsNullOrEmpty(mv)) { model = mv; foundSomething = true; }
            }
        }

        if (root.TryGetProperty("modelUsage", out var muEl) && muEl.ValueKind == JsonValueKind.Object)
        {
            var names = muEl.EnumerateObject().Select(p => p.Name).ToList();
            if (names.Any()) { model = string.Join("+", names); foundSomething = true; }
            
            foreach (var prop in muEl.EnumerateObject())
            {
                // snake_case (generic) or camelCase (Claude CLI)
                if (prop.Value.TryGetProperty("input_tokens", out var mit) || prop.Value.TryGetProperty("inputTokens", out mit)) { if (isIncremental) pt += mit.GetInt32(); else pt = mit.GetInt32(); }
                if (prop.Value.TryGetProperty("output_tokens", out var mot) || prop.Value.TryGetProperty("outputTokens", out mot)) { if (isIncremental) ct += mot.GetInt32(); else ct = mot.GetInt32(); }
                if (prop.Value.TryGetProperty("total_tokens", out var mtt)) { if (isIncremental) tt += mtt.GetInt32(); else tt = mtt.GetInt32(); }
                // Claude cache tokens — count as prompt tokens for usage totals
                if (prop.Value.TryGetProperty("cacheReadInputTokens", out var crit)) { if (isIncremental) pt += crit.GetInt32(); else pt += crit.GetInt32(); }
                if (prop.Value.TryGetProperty("cacheCreationInputTokens", out var ccit)) { if (isIncremental) pt += ccit.GetInt32(); else pt += ccit.GetInt32(); }
            }
        }

        // 2. Extract Usage
        JsonElement usageProp;
        bool hasUsage = root.TryGetProperty("usage", out usageProp) ||
                        root.TryGetProperty("usage_metadata", out usageProp) ||
                        root.TryGetProperty("usageMetadata", out usageProp);
        
        // Claude CLI: message.usage
        if (!hasUsage && root.TryGetProperty("message", out var msg) && msg.TryGetProperty("usage", out usageProp))
            hasUsage = true;
        
        // Copilot: data.outputTokens (only output tokens usually)
        if (!hasUsage && root.TryGetProperty("data", out var dataEl) && dataEl.TryGetProperty("outputTokens", out var otProp))
        {
            if (isIncremental) ct += otProp.GetInt32(); else ct = otProp.GetInt32();
            foundSomething = true;
        }

        if (hasUsage)
        {
            foundSomething = true;
            if (usageProp.TryGetProperty("input_tokens", out var it) || usageProp.TryGetProperty("prompt_tokens", out it) || usageProp.TryGetProperty("prompt_token_count", out it) || usageProp.TryGetProperty("promptTokenCount", out it) || usageProp.TryGetProperty("inputTokenCount", out it)) 
            { if (isIncremental) pt += it.GetInt32(); else pt = it.GetInt32(); }
            
            if (usageProp.TryGetProperty("output_tokens", out var ot) || usageProp.TryGetProperty("completion_tokens", out ot) || usageProp.TryGetProperty("completion_token_count", out ot) || usageProp.TryGetProperty("candidate_token_count", out ot) || usageProp.TryGetProperty("candidatesTokenCount", out ot) || usageProp.TryGetProperty("candidateTokenCount", out ot) || usageProp.TryGetProperty("outputTokenCount", out ot)) 
            { if (isIncremental) ct += ot.GetInt32(); else ct = ot.GetInt32(); }
            
            if (usageProp.TryGetProperty("total_tokens", out var tot) || usageProp.TryGetProperty("total_token_count", out tot) || usageProp.TryGetProperty("totalTokenCount", out tot))
            { if (isIncremental) tt += tot.GetInt32(); else tt = tot.GetInt32(); }

            // Claude cache tokens — add to prompt token total for accurate usage accounting
            if (usageProp.TryGetProperty("cache_read_input_tokens", out var crit)) { if (isIncremental) pt += crit.GetInt32(); else pt += crit.GetInt32(); }
            if (usageProp.TryGetProperty("cache_creation_input_tokens", out var ccit)) { if (isIncremental) pt += ccit.GetInt32(); else pt += ccit.GetInt32(); }
        }

        // 3. Extract Stats (Gemini)
        if (root.TryGetProperty("stats", out var statsEl))
        {
            foundSomething = true;
            if (statsEl.TryGetProperty("input_tokens", out var sit)) { if (isIncremental) pt += sit.GetInt32(); else pt = sit.GetInt32(); }
            if (statsEl.TryGetProperty("output_tokens", out var sot)) { if (isIncremental) ct += sot.GetInt32(); else ct = sot.GetInt32(); }
            if (statsEl.TryGetProperty("total_tokens", out var stt)) { if (isIncremental) tt += stt.GetInt32(); else tt = stt.GetInt32(); }
            
            if (statsEl.TryGetProperty("models", out var modelsEl) && modelsEl.ValueKind == JsonValueKind.Object)
            {
                var names = new List<string>();
                foreach (var m in modelsEl.EnumerateObject())
                {
                    names.Add(m.Name);
                    if (m.Value.TryGetProperty("tokens", out var tEl))
                    {
                        if (tEl.TryGetProperty("input", out var ti)) { if (isIncremental) pt += ti.GetInt32(); else pt = ti.GetInt32(); }
                        if (tEl.TryGetProperty("candidates", out var tc)) { if (isIncremental) ct += tc.GetInt32(); else ct = tc.GetInt32(); }
                        if (tEl.TryGetProperty("total", out var tt2)) { if (isIncremental) tt += tt2.GetInt32(); else tt = tt2.GetInt32(); }
                    }
                }
                if (names.Any()) model = string.Join("+", names);
            }
        }

        // 4. Extract Content
        if (root.TryGetProperty("type", out var typeProp))
        {
            foundSomething = true;
            var t = typeProp.GetString();
            if (t == "text" && root.TryGetProperty("part", out var op) && op.TryGetProperty("text", out var otText))
                content = (content ?? "") + otText.GetString();
            else if (t == "assistant.message" && root.TryGetProperty("content", out var amc))
                content = (content ?? "") + amc.GetString();
            else if (t == "message" && root.TryGetProperty("content", out var mMsg) &&
                     (!root.TryGetProperty("role", out var roleProp) || roleProp.GetString() != "user"))
                content = (content ?? "") + mMsg.GetString();
            else if (t == "assistant.message" && root.TryGetProperty("data", out var d) && d.TryGetProperty("content", out var c))
                content = (content ?? "") + c.GetString();
            else if (t == "assistant.message_delta" && root.TryGetProperty("data", out var dd) && dd.TryGetProperty("deltaContent", out var dc))
                content = (content ?? "") + dc.GetString();
            else if (t == "result" && root.TryGetProperty("result", out var rc))
                content = rc.GetString();
            else if (t == "item.completed" && root.TryGetProperty("item", out var itm) &&
                     itm.TryGetProperty("type", out var itmType) && itmType.GetString() == "agent_message" &&
                     itm.TryGetProperty("text", out var itmText))
                content = itmText.GetString();
        }
        else if (root.TryGetProperty("response", out var res)) { content = res.GetString(); foundSomething = true; }
        else if (root.TryGetProperty("result", out var res2)) { content = res2.GetString(); foundSomething = true; }
        else if (root.TryGetProperty("content", out var res3)) { content = res3.GetString(); foundSomething = true; }
        else if (root.TryGetProperty("text", out var res4)) { content = res4.GetString(); foundSomething = true; }

        return foundSomething;
    }
}
