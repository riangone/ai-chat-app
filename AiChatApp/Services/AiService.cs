using System.Diagnostics;
using System.Text;
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
    private SkillLearningService _skillLearning => _serviceProvider.GetRequiredService<SkillLearningService>();

    public record AgentDefinition(string Name, string DisplayName, string Description, string SystemPrompt);

    public AiService(AppDbContext db, MemorySearchService memorySearch, 
        SessionMemoryService sessionMemory, IServiceProvider serviceProvider, 
        SkillManagerService skillManager, PipelineLoaderService pipelineLoader, 
        SchemaValidationService schemaValidator, ToolExecutorService toolExecutor,
        EvalService evalService)
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
    }

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
        string prompt, int userId, int? chatSessionId, string provider = "gemini", int? agentId = null)
    {
        AgentProfile? agent = agentId.HasValue ? await _db.AgentProfiles.FindAsync(agentId.Value) : null;
        var systemPrompt = await BuildSystemPromptAsync(prompt, userId, chatSessionId, agent?.RoleName, agent);
        var workingDir = await GetProjectRootAsync(chatSessionId);
        var history = await BuildHistoryBlockAsync(chatSessionId, limit: 10);
        string fullPrompt = string.IsNullOrEmpty(history)
            ? prompt
            : $"{history}\nUser: {prompt}";

        var targetProvider = agent?.PreferredProvider ?? provider;

        // 获取当前会话最新的 MessageId (刚才保存的 User 消息)
        int messageId = 0;
        if (chatSessionId.HasValue)
        {
            var lastMsg = await _db.Messages
                .Where(m => m.ChatSessionId == chatSessionId.Value && !m.IsAi)
                .OrderByDescending(m => m.Id)
                .FirstOrDefaultAsync();
            messageId = lastMsg?.Id ?? 0;
        }

        var sw = Stopwatch.StartNew();
        string response = await ExecuteCliDirectAsync(fullPrompt, targetProvider, systemPrompt, workingDir);
        sw.Stop();

        // ログを記録
        if (messageId > 0)
        {
            var step = new AgentStep
            {
                MessageId = messageId,
                Role = agent?.RoleName ?? "Assistant",
                Persona = systemPrompt ?? "Default Assistant",
                Input = fullPrompt,
                Output = response,
                DurationMs = (int)sw.ElapsedMilliseconds,
                WasAccepted = true,
                CreatedAt = DateTime.UtcNow
            };
            _db.AgentSteps.Add(step);
            await _db.SaveChangesAsync();
        }

        return response;
    }

    /// <summary>
    /// Harness Cooperative Mode。
    /// Orchestrator → Executor（最大2回リトライ）→ Reviewer の3段パイプライン。
    /// 各ステップの結果を AgentStep としてDBに記録する。
    /// </summary>
    public async Task<(string Html, List<AgentStep> Steps)> CooperateAsync(
        string task, int userId, int messageId, int? chatSessionId, string provider = "gemini",
        List<string>? selectedAgentNames = null,
        Func<string, string, Task>? onStepComplete = null)
    {
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
                    provider: provider,
                    userId: userId,
                    chatSessionId: chatSessionId
                );

                // --- Tool Execution ---
                var projectRoot = await GetProjectRootAsync(chatSessionId);
                step.Output = await _toolExecutor.ExecuteToolsAsync(step.Output, projectRoot);
                await _db.SaveChangesAsync();

                // --- Evaluation ---
                _ = Task.Run(() => _evalService.EvaluateStepAsync(step.Id, task, step.Output, provider));

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
                    provider: stage.Provider ?? provider,
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
        string task, int userId, int messageId, string provider = "gemini")
    {
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

        var orchStep = await RunAgentStepAsync("Orchestrator", orchestratorPersona, task, messageId, provider, userId);
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
        
        var execStep = await RunAgentStepAsync("Executor", executorPersona, execInput, messageId, provider, userId);
        steps.Add(execStep);
        yield return $"event: step-complete\ndata: Executor|{BuildStepHtml(execStep).Replace("\n", "\\n")}\n\n";

        // ─── Step 3: Reviewer ───
        yield return "event: step-start\ndata: Reviewer\n\n";
        string reviewerPersona = "あなたは評審の専門家（Reviewer）です。最終的な回答をMarkdownで作成してください。";
        
        var reviewStep = await RunAgentStepAsync("Reviewer", reviewerPersona, $"元タスク:\n{task}\n\n実行結果:\n{execStep.Output}", messageId, provider, userId);
        steps.Add(reviewStep);
        
        string finalHtml = BuildCooperativeHtml(steps, reviewStep.Output);
        yield return $"event: final\ndata: {finalHtml.Replace("\n", "\\n")}\n\n";
    }

    public async IAsyncEnumerable<string> GetResponseStreamAsync(
        string prompt, int userId, int? chatSessionId, string provider = "gemini", int? agentId = null)
    {
        AgentProfile? agent = agentId.HasValue ? await _db.AgentProfiles.FindAsync(agentId.Value) : null;
        var systemPrompt = await BuildSystemPromptAsync(prompt, userId, chatSessionId, agent?.RoleName, agent);
        var workingDir = await GetProjectRootAsync(chatSessionId);
        var history = await BuildHistoryBlockAsync(chatSessionId, limit: 10);
        string fullPrompt = string.IsNullOrEmpty(history)
            ? prompt
            : $"{history}\nUser: {prompt}";

        var targetProvider = agent?.PreferredProvider ?? provider;
        var processInfo = SetupProcessInfo(targetProvider, workingDir);

        // 获取 MessageId 用于记录日志
        int messageId = 0;
        if (chatSessionId.HasValue)
        {
            var lastMsg = await _db.Messages
                .Where(m => m.ChatSessionId == chatSessionId.Value && !m.IsAi)
                .OrderByDescending(m => m.Id)
                .FirstOrDefaultAsync();
            messageId = lastMsg?.Id ?? 0;
        }

        var sw = Stopwatch.StartNew();
        using var process = Process.Start(processInfo);
        if (process == null) 
        {
            yield return "Error: Could not start CLI.";
            yield break;
        }

        // Write full prompt to stdin
        string inputToStdin = string.IsNullOrEmpty(systemPrompt)
            ? fullPrompt
            : $"System: {systemPrompt}\n\nUser: {fullPrompt}";
        
        await process.StandardInput.WriteAsync(inputToStdin);
        process.StandardInput.Close();

        var fullResponse = new StringBuilder();
        var buffer = new char[64];
        int read;
        while ((read = await process.StandardOutput.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            var chunk = new string(buffer, 0, read);
            fullResponse.Append(chunk);
            yield return chunk;
        }
        await process.WaitForExitAsync();
        sw.Stop();

        // ストリーム完了後にログを保存
        if (messageId > 0)
        {
            var step = new AgentStep
            {
                MessageId = messageId,
                Role = agent?.RoleName ?? "Assistant",
                Persona = systemPrompt ?? "Default Assistant",
                Input = fullPrompt,
                Output = fullResponse.ToString(),
                DurationMs = (int)sw.ElapsedMilliseconds,
                WasAccepted = true,
                CreatedAt = DateTime.UtcNow
            };
            _db.AgentSteps.Add(step);
            await _db.SaveChangesAsync();
        }
    }

    public Task<string> ExecuteCliDirectAsync(string prompt, string provider, string? systemPrompt = null, string? workingDir = null)
        => ExecuteCliAsync(prompt, provider, systemPrompt, workingDir);

    private async Task<string?> GetProjectRootAsync(int? chatSessionId)
    {
        if (!chatSessionId.HasValue) return null;
        var session = await _db.ChatSessions.Include(s => s.Project).FirstOrDefaultAsync(s => s.Id == chatSessionId.Value);
        return session?.Project?.RootPath;
    }

    // ─────────────────────────────────────────
    // 内部実装
    // ─────────────────────────────────────────

    private async Task<AgentStep> RunAgentStepAsync(
        string role, string persona, string input, int messageId,
        string provider, int userId, int? chatSessionId = null, int attemptNumber = 1)
    {
        var roleSkills = await _memorySearch.SearchSkillsAsync(input, userId, agentRole: role);
        var workingDir = await GetProjectRootAsync(chatSessionId);
        string fullPersona = persona;

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

            // Memory Instruction
            fullPersona += "\n\n[MEMORY INSTRUCTION]:\n重要な発見や制約があれば \"MEMORY: key=value\" の形式で行末に出力してください。";
        }

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

    private async Task<bool> QuickQualityCheckAsync(string originalTask, string execution, string provider)
    {
        string checkPrompt = $"""
            以下のタスクに対して実行結果が十分か判断してください。
            タスク: {originalTask}
            実行結果: {execution}
            
            結果が十分であれば "OK"、不十分であれば "RETRY" のみを返してください。
            """;

        string result = await ExecuteCliAsync(checkPrompt, provider, systemPrompt: null);
        return result.Contains("OK", StringComparison.OrdinalIgnoreCase);
    }
    private async Task<string> BuildSystemPromptAsync(string prompt, int userId, int? chatSessionId, string? agentRole, AgentProfile? selectedAgent = null)
    {
        var memories = await _memorySearch.SearchAsync(prompt, userId);
        var skills = await _memorySearch.SearchSkillsAsync(prompt, userId, agentRole);

        var sb = new StringBuilder("あなたは高度なAIアシスタントです。現在はソフトウェア開発プロジェクトのコンテキストで動作しています。");
        sb.Append("\nユーザーから具体的な実装や修正の指示があった場合、単にコードを提示するだけでなく、プロジェクトのファイル構造を理解し、必要に応じてファイルを直接操作・更新する計画を立てて実行してください。");
        sb.Append("\n出力には、実行すべき具体的なアクション（ファイルの読み込み、書き込み、置換など）を明確に含めてください。");

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
        var sb = new StringBuilder("<div class='space-y-4'>");

        foreach (var step in steps.Where(s => s.Role != "Reviewer"))
        {
            sb.Append(BuildStepHtml(step));
        }

        sb.Append($"""
            <div class='divider text-xs opacity-30'>FINAL OUTPUT</div>
            <div class='prose prose-sm max-w-none'>{System.Net.WebUtility.HtmlEncode(finalOutput)}</div>
            </div>
            """);

        return sb.ToString();
    }

    private static string ExtractJson(string text)
    {
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start >= 0 && end > start) return text[start..(end + 1)];
        return "{}";
    }

    private ProcessStartInfo SetupProcessInfo(string provider, string? workingDirectory = null)
    {
        string fileName = provider switch
        {
            "gh-copilot" => "copilot",
            "claude" => "claude",
            "codex" => "codex",
            _ => "gemini"
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

        if (provider == "codex")
        {
            processInfo.ArgumentList.Add("exec");
            processInfo.ArgumentList.Add("--dangerously-bypass-approvals-and-sandbox");
            processInfo.ArgumentList.Add("--color");
            processInfo.ArgumentList.Add("never");
        }
        else
        {
            processInfo.ArgumentList.Add("-p");
            processInfo.ArgumentList.Add(""); // Headless mode, read from stdin
            if (provider == "claude")
                processInfo.ArgumentList.Add("--dangerously-skip-permissions");
            else if (provider == "gh-copilot")
            {
                processInfo.ArgumentList.Add("--yolo");
                processInfo.ArgumentList.Add("--silent");
            }
            else
                processInfo.ArgumentList.Add("--yolo");
        }

        return processInfo;
    }

    public async Task<string> GenerateTitleAsync(string userPrompt, string aiResponse, string provider = "gemini")
    {
        string prompt = $"""
            以下のやり取りに基づいて、チャットセッションの短いタイトルを生成してください。
            タイトルは5語以内、または15文字程度で、装飾なしのプレーンテキストのみを返してください。
            
            ユーザー: {userPrompt}
            AI: {(aiResponse.Length > 200 ? aiResponse[..200] + "..." : aiResponse)}
            """;

        string result = await ExecuteCliAsync(prompt, provider, systemPrompt: "あなたはチャットタイトルの命名者です。簡潔で適切なタイトルのみを返します。");
        return result.Trim().Trim('"', '\'').Replace("\n", " ");
    }

    private async Task<string> ExecuteCliAsync(string prompt, string provider, string? systemPrompt = null, string? workingDirectory = null)
    {
        var processInfo = SetupProcessInfo(provider, workingDirectory);

        try
        {
            using var process = Process.Start(processInfo);
            if (process == null) return $"Error: Could not start {provider} CLI.";

            // Write prompt to stdin
            string inputToStdin = string.IsNullOrEmpty(systemPrompt)
                ? prompt
                : $"System: {systemPrompt}\n\nUser: {prompt}";
            
            await process.StandardInput.WriteAsync(inputToStdin);
            process.StandardInput.Close();

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(error))
                return $"[Error from {provider}]: {error}";

            return string.IsNullOrWhiteSpace(output) ? "No response received from AI." : output;
        }
        catch (Exception ex)
        {
            return $"[Exception]: {ex.Message}";
        }
    }
}
