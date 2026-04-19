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

    public AiService(AppDbContext db, MemorySearchService memorySearch)
    {
        _db = db;
        _memorySearch = memorySearch;
    }

    // ─────────────────────────────────────────
    // 公開API
    // ─────────────────────────────────────────

    /// <summary>通常チャット。会話履歴・記憶・スキルを注入してAIを呼び出す。</summary>
    public async Task<string> GetResponseAsync(
        string prompt, int userId, int? chatSessionId, string provider = "gemini")
    {
        var systemPrompt = await BuildSystemPromptAsync(prompt, userId, agentRole: null);
        var history = await BuildHistoryBlockAsync(chatSessionId, limit: 10);
        string fullPrompt = string.IsNullOrEmpty(history)
            ? prompt
            : $"{history}\nUser: {prompt}";

        return await ExecuteCliDirectAsync(fullPrompt, provider, systemPrompt);
    }

    /// <summary>
    /// Harness Cooperative Mode。
    /// Orchestrator → Executor（最大2回リトライ）→ Reviewer の3段パイプライン。
    /// 各ステップの結果を AgentStep としてDBに記録する。
    /// </summary>
    public async Task<(string Html, List<AgentStep> Steps)> CooperateAsync(
        string task, int userId, int messageId, string provider = "gemini",
        Func<string, string, Task>? onStepComplete = null)
    {
        var steps = new List<AgentStep>();

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
            userId: userId
        );
        steps.Add(orchStep);
        if (onStepComplete != null) await onStepComplete(orchStep.Role, BuildStepHtml(orchStep));

        // JSONパース試行（失敗時はテキストプランとして継続）
        string planSummary = orchStep.Output;
        string subtaskBlock = task; // フォールバック
        try
        {
            var planDoc = System.Text.Json.JsonDocument.Parse(ExtractJson(orchStep.Output));
            planSummary = planDoc.RootElement.GetProperty("plan").GetString() ?? orchStep.Output;
            var subtasks = planDoc.RootElement.GetProperty("subtasks").EnumerateArray()
                .Select(s => s.GetProperty("description").GetString() ?? "")
                .Where(s => !string.IsNullOrEmpty(s));
            subtaskBlock = string.Join("\n", subtasks.Select((s, i) => $"{i + 1}. {s}"));
        }
        catch { /* JSONでない場合はそのまま使用 */ }

        // ─── Step 2: Executor（最大2回）───
        string executorPersona = """
            あなたは実装の専門家（Executor）です。
            Orchestratorの計画に基づいて、コード・文章・分析など具体的な成果物を作成してください。
            品質を重視し、完全な実装を提供してください。
            """;

        AgentStep execStep = null!;
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            string execInput = attempt == 1
                ? $"計画:\n{subtaskBlock}\n\n原タスク:\n{task}"
                : $"計画:\n{subtaskBlock}\n\n原タスク:\n{task}\n\n前回の実行結果（改善が必要）:\n{execStep.Output}\n\n上記を改善して再実装してください。";

            execStep = await RunAgentStepAsync(
                role: "Executor",
                persona: executorPersona,
                input: execInput,
                messageId: messageId,
                provider: provider,
                userId: userId,
                attemptNumber: attempt
            );
            steps.Add(execStep);
            if (onStepComplete != null) await onStepComplete(execStep.Role, BuildStepHtml(execStep));

            // Reviewerに品質チェックを依頼（最終ループでは不要）
            if (attempt < 2)
            {
                bool qualityOk = await QuickQualityCheckAsync(task, execStep.Output, provider);
                if (qualityOk) break;
                execStep.WasAccepted = false;
                await _db.SaveChangesAsync();
            }
        }

        // ─── Step 3: Reviewer ───
        string reviewerPersona = """
            あなたは評審の専門家（Reviewer）です。
            以下の情報を受け取り、ユーザーへの最終的な回答を作成してください。
            - 元のタスクの要件が満たされているか確認する
            - 不完全な点があれば補足する
            - Markdownで整形して読みやすく提示する
            """;

        var reviewStep = await RunAgentStepAsync(
            role: "Reviewer",
            persona: reviewerPersona,
            input: $"元タスク:\n{task}\n\n計画:\n{planSummary}\n\n実行結果:\n{execStep.Output}",
            messageId: messageId,
            provider: provider,
            userId: userId
        );
        steps.Add(reviewStep);
        reviewStep.WasAccepted = true;
        await _db.SaveChangesAsync();
        if (onStepComplete != null) await onStepComplete(reviewStep.Role, BuildStepHtml(reviewStep));

        // ─── HTML構造を生成 ───
        string html = BuildCooperativeHtml(steps, reviewStep.Output);
        return (html, steps);
    }

    public async IAsyncEnumerable<string> GetResponseStreamAsync(
        string prompt, int userId, int? chatSessionId, string provider = "gemini")
    {
        var systemPrompt = await BuildSystemPromptAsync(prompt, userId, agentRole: null);
        var history = await BuildHistoryBlockAsync(chatSessionId, limit: 10);
        string fullPrompt = string.IsNullOrEmpty(history)
            ? prompt
            : $"{history}\nUser: {prompt}";

        var processInfo = SetupProcessInfo(fullPrompt, provider, systemPrompt);

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

    public Task<string> ExecuteCliDirectAsync(string prompt, string provider, string? systemPrompt = null)
        => ExecuteCliAsync(prompt, provider, systemPrompt);

    // ─────────────────────────────────────────
    // 内部実装
    // ─────────────────────────────────────────

    private async Task<AgentStep> RunAgentStepAsync(
        string role, string persona, string input, int messageId,
        string provider, int userId, int attemptNumber = 1)
    {
        var roleSkills = await _memorySearch.SearchSkillsAsync(input, userId, agentRole: role);
        string fullPersona = persona;
        if (roleSkills.Any())
        {
            fullPersona += "\n\n[追加スキル指示]:\n" +
                string.Join("\n", roleSkills.Select(s => $"- {s.Description}"));
        }

        var sw = Stopwatch.StartNew();
        string output = await ExecuteCliAsync(input, provider, fullPersona);
        sw.Stop();

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

    private async Task<string> BuildSystemPromptAsync(string prompt, int userId, string? agentRole)
    {
        var memories = await _memorySearch.SearchAsync(prompt, userId);
        var skills = await _memorySearch.SearchSkillsAsync(prompt, userId, agentRole);

        var sb = new StringBuilder("あなたは高度なAIアシスタントです。");

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

    private ProcessStartInfo SetupProcessInfo(string prompt, string provider, string? systemPrompt = null)
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
            CreateNoWindow = true
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

    private async Task<string> ExecuteCliAsync(string prompt, string provider, string? systemPrompt = null)
    {
        var processInfo = SetupProcessInfo(prompt, provider, systemPrompt);

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
