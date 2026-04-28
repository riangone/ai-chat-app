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
        string response = await ExecuteCliDirectAsync(fullPrompt, targetProvider, systemPrompt, workingDir);
        sw.Stop();

        // ログを記録
        await LogAgentStepAsync(messageId, agent?.RoleName ?? "Assistant", systemPrompt ?? "Default Assistant", fullPrompt, response, (int)sw.ElapsedMilliseconds);

        return response;
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
        string task, int userId, int messageId, string? provider = null)
    {
        var targetProvider = provider ?? DefaultProvider;
        var steps = new List<AgentStep>();

        // ─── Step 1: Orchestrator ───
        yield return "event: step-start\ndata: Orchestrator\n\n";
        string orchestratorPersona = """
            あなたはタスク分解の専門家（Orchestrator）です。
            ユーザーのタスクを分析し、以下のJSON形式でのみ回答してください：
            {
              "plan": "全体方針の概要（1-2文）",
              "subtasks": [
                {"id": 1, "description": "サブタスクの説明", "expectedOutput": "期待される成果物"},
                ...
              ]
            }
            """;

        var orchStep = await RunAgentStepAsync("Orchestrator", orchestratorPersona, task, messageId, targetProvider, userId);
        steps.Add(orchStep);
        yield return $"event: step-complete\ndata: Orchestrator|{BuildStepHtml(orchStep).Replace("\n", "\\n")}\n\n";

        // JSONパース
        string planSummary = orchStep.Output;
        string subtaskBlock = task;
        try
        {
            var planDoc = System.Text.Json.JsonDocument.Parse(ExtractJson(orchStep.Output));
            planSummary = planDoc.RootElement.GetProperty("plan").GetString() ?? orchStep.Output;
            var subtasks = planDoc.RootElement.GetProperty("subtasks").EnumerateArray()
                .Select(s => s.GetProperty("description").GetString() ?? "")
                .Where(s => !string.IsNullOrEmpty(s));
            subtaskBlock = string.Join("\n", subtasks.Select((s, i) => $"{i + 1}. {s}"));
        }
        catch { }

        // ─── Step 2: Executor ───
        yield return "event: step-start\ndata: Executor\n\n";
        string executorPersona = "あなたは実装の専門家（Executor）です。計画に基づいて成果物を作成してください。";
        string execInput = $"計画:\n{subtaskBlock}\n\n原タスク:\n{task}";
        
        var execStep = await RunAgentStepAsync("Executor", executorPersona, execInput, messageId, targetProvider, userId);
        steps.Add(execStep);
        yield return $"event: step-complete\ndata: Executor|{BuildStepHtml(execStep).Replace("\n", "\\n")}\n\n";

        // ─── Step 3: Reviewer ───
        yield return "event: step-start\ndata: Reviewer\n\n";
        string reviewerPersona = "あなたは評審の専門家（Reviewer）です。最終的な回答をMarkdownで作成してください。";
        
        var reviewStep = await RunAgentStepAsync("Reviewer", reviewerPersona, $"元タスク:\n{task}\n\n実行結果:\n{execStep.Output}", messageId, targetProvider, userId);
        steps.Add(reviewStep);
        
        string finalHtml = BuildCooperativeHtml(steps, reviewStep.Output);
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
            "claude" => "claude",
            "codex" => "codex",
            "opencode" => "opencode",
            _ => DefaultProvider
        };
        var useJsonStreaming = targetProvider == "gemini" || fileName == "gemini" || targetProvider == "claude" || fileName == "claude";

        var processInfo = SetupProcessInfo(targetProvider, workingDir, useJsonStreaming ? "stream-json" : null);

        // 获取 MessageId 用于记录日志
        int messageId = await GetLatestUserMessageIdAsync(chatSessionId);

        string inputToStdin = string.IsNullOrEmpty(systemPrompt)
            ? fullPrompt
            : $"{systemPrompt}\n\n{fullPrompt}";

        if (targetProvider == "opencode")
        {
            processInfo.ArgumentList.Add("--yolo");
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
        // Buffer for detecting and stripping echoed System:/User: prefix at stream start
        var prefixBuffer = new StringBuilder();
        bool prefixHandled = false;
        const int maxPrefixBuffer = 4096;
        string[] promptPrefixes = { "System:", "User:", "Assistant:", "Context:", "History:", "Memory:", "[会話履歴]:", "[ユーザーの既知情報・長期記憶]:" };

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
                    // Standard message format (Gemini/Claude CLI)
                    if (doc.RootElement.TryGetProperty("type", out var typeProp))
                    {
                        var type = typeProp.GetString();
                        if (type == "message")
                        {
                            // Skip messages from user or system to avoid echoing the prompt
                            bool isAssistant = true;
                            if (doc.RootElement.TryGetProperty("role", out var roleProp))
                            {
                                var role = roleProp.GetString();
                                if (role == "user" || role == "system") isAssistant = false;
                            }

                            if (isAssistant && doc.RootElement.TryGetProperty("content", out var contentProp))
                            {
                                chunk = contentProp.GetString() ?? "";
                            }
                        }
                        else if (type == "content_block_delta" || type == "text_delta")
                        {
                            if (doc.RootElement.TryGetProperty("delta", out var deltaProp) &&
                                deltaProp.TryGetProperty("text", out var textProp))
                            {
                                chunk = textProp.GetString() ?? "";
                            }
                        }
                    }
                    else if (doc.RootElement.TryGetProperty("content", out var directContent))
                    {
                        chunk = directContent.GetString() ?? "";
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
        await LogAgentStepAsync(messageId, agent?.RoleName ?? "Assistant", systemPrompt ?? "Default Assistant", fullPrompt, fullResponse.ToString(), (int)sw.ElapsedMilliseconds);
    }

    public Task<string> ExecuteCliDirectAsync(string prompt, string provider, string? systemPrompt = null, string? workingDir = null)
        => ExecuteCliAsync(prompt, provider, systemPrompt, workingDir);

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

    private async Task LogAgentStepAsync(int messageId, string role, string persona, string input, string output, int durationMs)
    {
        if (messageId <= 0) return;
        var step = new AgentStep
        {
            MessageId = messageId,
            Role = role,
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
        string fullPersona = persona + policies;

        if (memories.Any())
        {
            fullPersona += "\n\n[ユーザーの既知情報・長期記憶]:\n";
            foreach (var m in memories) fullPersona += $"- {m.Content}\n";
        }

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
                fullPersona = projectAgent.SystemPrompt + "\n\n" + persona;
            }

            // Inject Session Memory
            var sessionMemoryContext = await _sessionMemory.ReadAllAsContextAsync(chatSessionId.Value);
            if (!string.IsNullOrEmpty(sessionMemoryContext))
            {
                fullPersona += "\n\n" + sessionMemoryContext;
            }
        }

        // Memory Instruction
        fullPersona += GetSystemPromptTemplate("MemoryInstruction", "\n\n[MEMORY INSTRUCTION]:\n重要な発見や制約があれば \"MEMORY: key=value\" の形式で行末に出力してください。");

        if (roleSkills.Any())
        {
            fullPersona += "\n\n[追加スキル指示]:\n" +
                string.Join("\n", roleSkills.Select(s => $"- {s.Description}"));
            
            // 使用したスキルのメトリクスを更新（簡易的に最初の1つ）
            var firstSkill = roleSkills.First();
            _ = Task.Run(() => _skillLearning.UpdateSkillMetricsAsync(firstSkill.Id, true));
        }

        var sw = Stopwatch.StartNew();
        string output = await ExecuteCliAsync(input, provider, fullPersona, workingDir);
        sw.Stop();

        // Extract and Save Memory
        if (chatSessionId.HasValue)
        {
            await ParseAndSaveMemoryAsync(chatSessionId.Value, role, output);
        }

        var step = new AgentStep
        {
            MessageId = messageId,
            Role = role,
            Persona = fullPersona,
            Input = input,
            Output = output,
            AttemptNumber = attemptNumber,
            WasAccepted = true,
            DurationMs = (int)sw.ElapsedMilliseconds
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

        string result = await ExecuteCliAsync(checkPrompt, targetProvider, systemPrompt: null);
        return result.Contains("OK", StringComparison.OrdinalIgnoreCase);
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
        int lastPromptLine = -1;
        
        string[] promptPrefixes = { 
            "System:", "User:", "Assistant:", "Context:", "History:", "Memory:", 
            "Thought:", "Thinking:", "[会話履歴]:", "[ユーザーの既知情報・長期記憶]:", 
            "[追加スキル指示]:", "[MEMORY INSTRUCTION]:", "[ENVIRONMENTAL POLICIES & CONSTRAINTS]:", 
            "--- Policy:", "Role:", "Persona:", "Input:", "Output:"
        };
        string[] systemPromptFragments = { 
            "あなたは高度なAIアシスタントです", 
            "現在はソフトウェア開発プロジェクトのコンテキストで動作しています",
            "あなたはタスク分解の専門家",
            "あなたは実装の専門家",
            "あなたは評審の専門家",
            "[MEMORY INSTRUCTION]",
            "[会話履歴]:",
            "[ユーザーの既知情報・長期記憶]:",
            "重要な発見や制約があれば"
        };

        for (int i = 0; i < Math.Min(lines.Length, 150); i++)
        {
            var trimmedLine = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                lastPromptLine = i;
                continue;
            }

            bool isPromptHeader = promptPrefixes.Any(p => trimmedLine.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            bool isSystemFragment = systemPromptFragments.Any(f => trimmedLine.Contains(f));

            if (isPromptHeader || isSystemFragment)
            {
                lastPromptLine = i;
            }
            else
            {
                // Stop if we hit a line that's definitely not a prompt line
                // But only if we've already found some prompt lines
                if (lastPromptLine != -1 && trimmedLine.Length > 0)
                {
                    // Special case: if the line starts with "Assistant:", we want to skip it and take the rest
                    if (trimmedLine.StartsWith("Assistant:", StringComparison.OrdinalIgnoreCase))
                    {
                        lastPromptLine = i;
                        continue;
                    }
                    break; 
                }
            }
        }

        if (lastPromptLine >= 0)
        {
            var remainingLines = lines.Skip(lastPromptLine + 1).ToList();
            if (remainingLines.Any())
            {
                var joined = string.Join("\n", remainingLines).Trim();
                // Recursively strip if the first line of the result is still a prefix
                if (promptPrefixes.Any(p => joined.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    return StripEchoedPromptPrefix(joined);
                return joined;
            }
            else
            {
                // If everything was stripped, maybe the last line WAS the response but matched a fragment
                return text.Trim();
            }
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
        }
        else if (provider == "opencode")
        {
            processInfo.ArgumentList.Add("run");
            processInfo.ArgumentList.Add("--dangerously-skip-permissions");
            processInfo.ArgumentList.Add("--yolo");
        }
        else if (provider == "gh-copilot")
        {
            processInfo.ArgumentList.Add("--allow-all-tools");
        }
        else
        {
            processInfo.ArgumentList.Add("-p");
            processInfo.ArgumentList.Add(""); // Headless mode, read from stdin
            
            if (provider == "claude" || fileName == "claude")
            {
                processInfo.ArgumentList.Add("--dangerously-skip-permissions");
                processInfo.ArgumentList.Add("--sandbox");
                processInfo.ArgumentList.Add("false");
                processInfo.ArgumentList.Add("--output-format");
                processInfo.ArgumentList.Add(outputFormat ?? "json");
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
                    processInfo.ArgumentList.Add("--raw-output");
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

        string result = await ExecuteCliAsync(prompt, targetProvider, systemPrompt: GetSystemPromptTemplate("TitleGenerator", "あなたはチャットタイトルの命名者です。簡潔で適切なタイトルのみを返します。"));
        return result.Trim().Trim('"', '\'').Replace("\n", " ");
    }

    private async Task<string> ExecuteCliAsync(string prompt, string provider, string? systemPrompt = null, string? workingDirectory = null)
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
            if (process == null) return $"Error: Could not start {provider} CLI.";

            using var cts = new CancellationTokenSource();
            // Kill process after timeout to prevent indefinite hang
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
                    // Fallback to configured fallback provider
                    return await ExecuteCliAsync(prompt, FallbackProvider, systemPrompt, workingDirectory);
                }
                
                if (!string.IsNullOrWhiteSpace(error))
                    return $"[Error from {provider}]: {ExtractCliError(error, provider)}";
            }

            if (string.IsNullOrWhiteSpace(output)) return "No response received from AI.";

            // Try to extract JSON and get the final response property
            try
            {
                var jsonText = ExtractJson(output);
                if (!string.IsNullOrEmpty(jsonText) && jsonText != "{}")
                {
                    using var doc = JsonDocument.Parse(jsonText);
                    // Check common properties for the final response
                    if (doc.RootElement.TryGetProperty("response", out var resProp))
                        return CleanResponse(resProp.GetString() ?? "");
                    if (doc.RootElement.TryGetProperty("content", out var contentProp))
                        return CleanResponse(contentProp.GetString() ?? "");
                    if (doc.RootElement.TryGetProperty("text", out var textProp))
                        return CleanResponse(textProp.GetString() ?? "");
                }
            }
            catch (JsonException)
            {
                // Fallback to raw output if JSON parsing fails
            }

            // If not JSON or property not found, clean the raw output
            return CleanResponse(output);
        }
        catch (Exception ex)
        {
            if (provider != FallbackProvider)
            {
                return await ExecuteCliAsync(prompt, FallbackProvider, systemPrompt, workingDirectory);
            }
            return $"[Exception]: {ex.Message}";
        }
    }
}
