using System.Diagnostics;
using System.Text;
using AiChatApp.Data;
using AiChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace AiChatApp.Services;

public class AiService
{
    private readonly AppDbContext _db;
    private readonly MemorySearchService _memorySearch;
    private readonly SessionMemoryService _sessionMemory;
    private readonly IServiceProvider _serviceProvider;
    private readonly SkillManagerService _skillManager;
    private SkillLearningService _skillLearning => _serviceProvider.GetRequiredService<SkillLearningService>();

    public record AgentDefinition(string Name, string DisplayName, string Description, string SystemPrompt);

    public AiService(AppDbContext db, MemorySearchService memorySearch, 
        SessionMemoryService sessionMemory, IServiceProvider serviceProvider, SkillManagerService skillManager)
    {
        _db = db;
        _memorySearch = memorySearch;
        _sessionMemory = sessionMemory;
        _serviceProvider = serviceProvider;
        _skillManager = skillManager;
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
        string prompt, int userId, int? chatSessionId, string provider = "gemini")
    {
        var systemPrompt = await BuildSystemPromptAsync(prompt, userId, chatSessionId, agentRole: null);
        var workingDir = await GetProjectRootAsync(chatSessionId);
        var history = await BuildHistoryBlockAsync(chatSessionId, limit: 10);
        string fullPrompt = string.IsNullOrEmpty(history)
            ? prompt
            : $"{history}\nUser: {prompt}";

        return await ExecuteCliDirectAsync(fullPrompt, provider, systemPrompt, workingDir);
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
            // 获取项目自定义角色 (原有逻辑)
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
            // --- 统一多智能体流程 ---
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
                steps.Add(step);
                lastOutput = step.Output;
                if (onStepComplete != null) await onStepComplete(step.Role, BuildStepHtml(step));
            }
            string html = BuildCooperativeHtml(steps, lastOutput);

            // --- Post-Cooperation Tasks (Async) ---
            if (chatSessionId.HasValue)
            {
                _ = Task.Run(async () => {
                    await _sessionMemory.PromoteToLongTermAsync(chatSessionId.Value, userId);
                    await _skillLearning.LearnFromInteractionAsync(task, lastOutput, steps, userId);
                });
            }

            return (html, steps);
        }

        // --- 默认 3 阶段流程 ---
        // ─── Step 1: Orchestrator ───
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

        var orchStep = await RunAgentStepAsync(
            role: "Orchestrator",
            persona: orchestratorPersona,
            input: task,
            messageId: messageId,
            provider: provider,
            userId: userId,
            chatSessionId: chatSessionId
        );
        steps.Add(orchStep);
        if (onStepComplete != null) await onStepComplete(orchStep.Role, BuildStepHtml(orchStep));

        // JSONパース試行
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

        // ─── Step 2: Executor（最大2回）───
        string executorPersona = "あなたは実装の専門家（Executor）です。Orchestratorの計画に基づいて成果物を作成してください。";
        AgentStep execStep = null!;
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            string execInput = attempt == 1
                ? $"計画:\n{subtaskBlock}\n\n原タスク:\n{task}"
                : $"計画:\n{subtaskBlock}\n\n原タスク:\n{task}\n\n前回の実行結果:\n{execStep.Output}\n\n再実装してください。";

            execStep = await RunAgentStepAsync(
                role: "Executor",
                persona: executorPersona,
                input: execInput,
                messageId: messageId,
                provider: provider,
                userId: userId,
                chatSessionId: chatSessionId,
                attemptNumber: attempt
            );
            steps.Add(execStep);
            if (onStepComplete != null) await onStepComplete(execStep.Role, BuildStepHtml(execStep));

            if (attempt < 2)
            {
                bool qualityOk = await QuickQualityCheckAsync(task, execStep.Output, provider);
                if (qualityOk) break;
                execStep.WasAccepted = false;
                await _db.SaveChangesAsync();
            }
        }

        // ─── Step 3: Reviewer ───
        string reviewerPersona = "あなたは評審の専門家（Reviewer）です。最終的な回答をMarkdownで作成してください。";

        var reviewStep = await RunAgentStepAsync(
            role: "Reviewer",
            persona: reviewerPersona,
            input: $"元タスク:\n{task}\n\n計画:\n{planSummary}\n\n実行結果:\n{execStep.Output}",
            messageId: messageId,
            provider: provider,
            userId: userId,
            chatSessionId: chatSessionId
        );
        steps.Add(reviewStep);
        reviewStep.WasAccepted = true;
        await _db.SaveChangesAsync();
        if (onStepComplete != null) await onStepComplete(reviewStep.Role, BuildStepHtml(reviewStep));

        // --- Post-Cooperation Tasks (Async) ---
        if (chatSessionId.HasValue)
        {
            _ = Task.Run(async () => {
                await _sessionMemory.PromoteToLongTermAsync(chatSessionId.Value, userId);
                await _skillLearning.LearnFromInteractionAsync(task, reviewStep.Output, steps, userId);
            });
        }

        return (BuildCooperativeHtml(steps, reviewStep.Output), steps);
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
        string prompt, int userId, int? chatSessionId, string provider = "gemini")
    {
        var systemPrompt = await BuildSystemPromptAsync(prompt, userId, chatSessionId, agentRole: null);
        var workingDir = await GetProjectRootAsync(chatSessionId);
        var history = await BuildHistoryBlockAsync(chatSessionId, limit: 10);
        string fullPrompt = string.IsNullOrEmpty(history)
            ? prompt
            : $"{history}\nUser: {prompt}";

        var processInfo = SetupProcessInfo(fullPrompt, provider, systemPrompt, workingDir);

        using var process = Process.Start(processInfo);
        if (process == null) 
        {
            yield return "Error: Could not start CLI.";
            yield break;
        }

        var buffer = new char[64];
        int read;
        while ((read = await process.StandardOutput.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            yield return new string(buffer, 0, read);
        }
        await process.WaitForExitAsync();
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

    private async Task<string> BuildSystemPromptAsync(string prompt, int userId, int? chatSessionId, string? agentRole)
    {
        var memories = await _memorySearch.SearchAsync(prompt, userId);
        var skills = await _memorySearch.SearchSkillsAsync(prompt, userId, agentRole);

        var sb = new StringBuilder("あなたは高度なAIアシスタントです。");

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

    private ProcessStartInfo SetupProcessInfo(string prompt, string provider, string? systemPrompt = null, string? workingDirectory = null)
    {
        string fileName = provider switch
        {
            "gh-copilot" => "copilot",
            "claude" => "claude",
            "codex" => "codex",
            _ => "gemini"
        };

        string fullPrompt = string.IsNullOrEmpty(systemPrompt)
            ? prompt
            : $"System: {systemPrompt}\n\nUser: {prompt}";

        var processInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory()
        };

        if (provider == "codex")
        {
            processInfo.ArgumentList.Add("exec");
            processInfo.ArgumentList.Add(fullPrompt);
            processInfo.ArgumentList.Add("--dangerously-bypass-approvals-and-sandbox");
            processInfo.ArgumentList.Add("--color");
            processInfo.ArgumentList.Add("never");
        }
        else
        {
            processInfo.ArgumentList.Add("-p");
            processInfo.ArgumentList.Add(fullPrompt);
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

    private async Task<string> ExecuteCliAsync(string prompt, string provider, string? systemPrompt = null, string? workingDirectory = null)
    {
        var processInfo = SetupProcessInfo(prompt, provider, systemPrompt, workingDirectory);

        try
        {
            using var process = Process.Start(processInfo);
            if (process == null) return $"Error: Could not start {provider} CLI.";

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(error))
                return $"Error from {provider}: {error}";

            return string.IsNullOrWhiteSpace(output) ? "No response received." : output;
        }
        catch (Exception ex)
        {
            return $"Exception: {ex.Message}";
        }
    }
}
