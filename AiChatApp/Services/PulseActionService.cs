using System.Diagnostics;
using AiChatApp.Data;
using AiChatApp.Hubs;
using AiChatApp.Models;
using AiChatApp.Services.Harness;
using AiChatApp.Services.Pulse;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AiChatApp.Services;

public class PulseActionService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly AppDbContext _db;
    private readonly WorktreeManagerService _worktreeManager;
    private readonly GitCommitPulseSource _gitPulseSource;
    private readonly AiService _aiService;
    private readonly EvalService _evalService;
    private readonly ProactiveBrainService _proactiveBrain;
    private readonly IHubContext<ProactiveAgentHub> _hubContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PulseActionService> _logger;

    public PulseActionService(
        IServiceProvider serviceProvider,
        AppDbContext db,
        WorktreeManagerService worktreeManager,
        GitCommitPulseSource gitPulseSource,
        AiService aiService,
        EvalService evalService,
        ProactiveBrainService proactiveBrain,
        IHubContext<ProactiveAgentHub> hubContext,
        IConfiguration configuration,
        ILogger<PulseActionService> logger)
    {
        _serviceProvider = serviceProvider;
        _db = db;
        _worktreeManager = worktreeManager;
        _gitPulseSource = gitPulseSource;
        _aiService = aiService;
        _evalService = evalService;
        _proactiveBrain = proactiveBrain;
        _hubContext = hubContext;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task ExecuteAsync(Project project, string sourceKey, string oldHash, string newHash, CancellationToken ct)
    {
        var autoFixEnabled = _configuration.GetValue<bool>("ProactiveSettings:AutoFixEnabled");
        var keepFailedBranches = _configuration.GetValue<bool>("ProactiveSettings:KeepFailedBranches");
        var evalThreshold = _configuration.GetValue<double>("ProactiveSettings:EvalThreshold");

        // Check for existing ledger entry to avoid duplicates
        var existingLedger = await _db.ProjectPulseLedgers
            .FirstOrDefaultAsync(l => l.ProjectId == project.Id
                                   && l.SourceType == "git_commit"
                                   && l.SourceKey == sourceKey, ct);

        if (existingLedger != null)
        {
            if (existingLedger.Status is "succeeded" or "skipped")
            {
                _logger.LogDebug("Ledger entry {Id} already processed (status={Status}), skipping.", existingLedger.Id, existingLedger.Status);
                return;
            }

            if (existingLedger.Status == "running" && existingLedger.CreatedAt.AddMinutes(30) > DateTime.UtcNow)
            {
                _logger.LogDebug("Ledger entry {Id} is still running (created at {CreatedAt}), skipping.", existingLedger.Id, existingLedger.CreatedAt);
                return;
            }
        }

        if (!autoFixEnabled)
        {
            _logger.LogInformation("AutoFix disabled. Registering skipped ledger entry for {Key}", sourceKey);

            if (existingLedger == null)
            {
                existingLedger = new ProjectPulseLedger
                {
                    ProjectId = project.Id,
                    SourceType = "git_commit",
                    SourceKey = sourceKey,
                    Status = "skipped",
                    ResultSummary = $"AutoFix disabled; change from {oldHash[..8]}..{newHash[..8]} detected (notified)."
                };
                _db.ProjectPulseLedgers.Add(existingLedger);
                await _db.SaveChangesAsync(ct);
            }
            else
            {
                existingLedger.Status = "skipped";
                existingLedger.CompletedAt = DateTime.UtcNow;
                existingLedger.ResultSummary = $"AutoFix disabled; change from {oldHash[..8]}..{newHash[..8]} detected (notified).";
                await _db.SaveChangesAsync(ct);
            }

            var diffSummary = await _gitPulseSource.GetDiffSummaryAsync(project.RootPath, oldHash, newHash);
            if (!string.IsNullOrWhiteSpace(diffSummary))
            {
                var activeUserId = await _db.Users.Select(u => (int?)u.Id).FirstOrDefaultAsync(ct) ?? 1;
                await _proactiveBrain.AnalyzeProjectPulseAsync(diffSummary, activeUserId);
            }

            await UpdateCursorAsync(project.Id, newHash, ct);
            return;
        }

        // AutoFixEnabled=true: full loop
        var ledger = existingLedger ?? new ProjectPulseLedger
        {
            ProjectId = project.Id,
            SourceType = "git_commit",
            SourceKey = sourceKey,
            Status = "running"
        };

        if (existingLedger == null)
            _db.ProjectPulseLedgers.Add(ledger);
        else
            existingLedger.Status = "running";

        await _db.SaveChangesAsync(ct); // ledger.Id を確定させる

        // ブランチ名は worktree 作成前に確定させ、即座に ledger へ保存しておく。
        // こうしておかないと、CooperateAsync 実行中にプロセスが落ちた場合
        // （kill/クラッシュで finally が走らない場合）、"running" のまま残ったledgerから
        // どの worktree を指していたか分からず孤児化する（起動時 reconciliation が回収できなくなる）。
        var branchName = $"pulse/{DateTime.UtcNow:yyyyMMdd-HHmmss}-{ledger.Id}";
        ledger.BranchName = branchName;
        await _db.SaveChangesAsync(ct);

        try
        {
            var diffSummary = await _gitPulseSource.GetDiffSummaryAsync(project.RootPath, oldHash, newHash)
                              ?? "(no diff)";

            var sem = _worktreeManager.GetProjectLock(project.Id);
            await sem.WaitAsync(ct);

            string? worktreePath = null;
            try
            {
                worktreePath = await _worktreeManager.CreateAsync(project.RootPath, branchName, ct);

                var agentTask = $@"You are analyzing a project pulse (code change). 
Review the diff and implement necessary fixes or improvements in the worktree at {worktreePath}.

Diff summary:
{diffSummary}

Instructions:
1. Read the relevant files in the worktree.
2. Apply necessary changes to fix issues or improve the code.
3. Ensure all changes compile and are reasonable.
4. The working directory for all file operations is: {worktreePath}
5. Do NOT push or create PRs. Just make the changes.

Project: {project.Name} ({project.RootPath})";

                var agentNames = project.Agents?.Select(a => a.RoleName).Where(n => !string.IsNullOrEmpty(n)).ToList();
                var onStepComplete = BuildStepCallback(project.Id);

                // AgentStep.MessageId / Message.ChatSessionId は NOT NULL の外部キーなので、
                // CooperateAsync に渡す前に実在する ChatSession/Message 行を作っておく必要がある。
                // 以前は messageId: 0 / chatSessionId: null のダミー値を渡しており、
                // 参照先が存在せず SQLite の FOREIGN KEY constraint failed で
                // AutoFix ループ全体が必ず失敗していた。
                var pulseSession = new ChatSession
                {
                    UserId = project.UserId,
                    ProjectId = project.Id,
                    Title = $"Pulse AutoFix: {branchName}",
                    PreferredProvider = "opencode"
                };
                _db.ChatSessions.Add(pulseSession);
                await _db.SaveChangesAsync(ct);

                var pulseUserMsg = new Message { ChatSessionId = pulseSession.Id, Content = agentTask, IsAi = false };
                var pulseAiMsg = new Message { ChatSessionId = pulseSession.Id, Content = "", IsAi = true, AgentName = "Multi-Agent" };
                _db.Messages.Add(pulseUserMsg);
                _db.Messages.Add(pulseAiMsg);
                await _db.SaveChangesAsync(ct); // pulseAiMsg.Id を確定させる

                _logger.LogInformation("Starting CooperateAsync for project {ProjectId} on branch {Branch}", project.Id, branchName);

                var (htmlOutput, steps) = await _aiService.CooperateAsync(
                    agentTask,
                    userId: project.UserId,
                    messageId: pulseAiMsg.Id,
                    chatSessionId: pulseSession.Id,
                    provider: "opencode",
                    selectedAgentNames: agentNames,
                    onStepComplete: onStepComplete);

                double? accuracy = null, safety = null;
                foreach (var step in steps)
                {
                    if (step.Id > 0)
                    {
                        await _evalService.EvaluateStepAsync(step.Id, agentTask, step.Output);
                    }
                }

                var evaluations = await _db.Evaluations
                    .Where(e => steps.Select(s => s.Id).Contains(e.AgentStepId))
                    .ToListAsync(ct);

                accuracy = evaluations.Where(e => e.Criteria == "Accuracy").Select(e => (double?)e.Score).Max();
                safety = evaluations.Where(e => e.Criteria == "Safety").Select(e => (double?)e.Score).Max();

                var threshold = evalThreshold > 0 ? evalThreshold : 0.6;
                if ((accuracy.HasValue && accuracy < threshold) || (safety.HasValue && safety < threshold))
                {
                    _logger.LogWarning("Eval threshold not met: Accuracy={Accuracy}, Safety={Safety} (threshold={Threshold})",
                        accuracy, safety, threshold);

                    ledger.Status = "failed";
                    ledger.BranchName = keepFailedBranches ? branchName : null;
                    ledger.ResultSummary = $"AutoFix failed: Accuracy={accuracy?.ToString("0.00") ?? "N/A"}, Safety={safety?.ToString("0.00") ?? "N/A"}. Branch '{branchName}' {(keepFailedBranches ? "kept for review" : "deleted")}.";

                    if (!keepFailedBranches && worktreePath != null)
                        await _worktreeManager.RemoveAsync(project.RootPath, worktreePath, branchName, deleteBranch: true, ct);
                    else if (worktreePath != null)
                        await _worktreeManager.RemoveAsync(project.RootPath, worktreePath, branchName, deleteBranch: false, ct);

                    worktreePath = null;
                    ledger.CompletedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync(ct);
                    return;
                }

                var summary = htmlOutput.Length > 500 ? htmlOutput[..500] + "..." : htmlOutput;
                await CommitWorktreeChangesAsync(worktreePath, summary);

                ledger.Status = "succeeded";
                ledger.BranchName = branchName;
                ledger.ResultSummary = $"AutoFix completed. Changes committed on branch '{branchName}'. Summary: {summary}";
                ledger.CompletedAt = DateTime.UtcNow;

                if (worktreePath != null)
                    await _worktreeManager.RemoveAsync(project.RootPath, worktreePath, branchName, deleteBranch: false, ct);

                worktreePath = null;
            }
            finally
            {
                if (worktreePath != null)
                {
                    try
                    {
                        await _worktreeManager.RemoveAsync(project.RootPath, worktreePath, branchName, deleteBranch: false, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to clean up worktree {Path}", worktreePath);
                    }
                }
                sem.Release();
            }

            await UpdateCursorAsync(project.Id, newHash, ct);
            await _db.SaveChangesAsync(ct);

            await _hubContext.Clients.Group($"project-{project.Id}")
                .SendAsync("ProactiveMessage", new
                {
                    Title = "Project Pulse - AutoFix Complete",
                    Content = $"AutoFix completed on branch '{branchName}'. Please review and merge.",
                    Type = "pulse_result",
                    ledgerId = ledger.Id
                }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pulse action failed for project {ProjectId} key {Key}", project.Id, sourceKey);
            ledger.Status = "failed";
            ledger.ResultSummary = $"Exception: {ex.Message}";
            ledger.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    private Func<string, string, Task>? BuildStepCallback(int projectId)
    {
        return async (stepRole, stepOutput) =>
        {
            try
            {
                await _hubContext.Clients.Group($"project-{projectId}")
                    .SendAsync("ProactiveMessage", new
                    {
                        Title = $"Pulse Step: {stepRole}",
                        Content = stepOutput.Length > 200 ? stepOutput[..200] + "..." : stepOutput,
                        Type = "pulse_step"
                    });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send pulse step notification");
            }
        };
    }

    private async Task CommitWorktreeChangesAsync(string worktreePath, string summary)
    {
        var psiAdd = new ProcessStartInfo("git", "add -A")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = worktreePath
        };
        using var procAdd = Process.Start(psiAdd);
        if (procAdd != null)
        {
            await procAdd.WaitForExitAsync();
            var addError = await procAdd.StandardError.ReadToEndAsync();
            if (procAdd.ExitCode != 0)
                _logger.LogWarning("git add failed in worktree: {Error}", addError);
        }

        var safeSummary = summary.Length > 100 ? summary[..100] : summary;
        var psiCommit = new ProcessStartInfo("git", $"commit -m \"pulse: {safeSummary.Replace("\"", "\\\"")}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = worktreePath
        };
        using var procCommit = Process.Start(psiCommit);
        if (procCommit != null)
        {
            await procCommit.WaitForExitAsync();
            var commitError = await procCommit.StandardError.ReadToEndAsync();
            var commitOutput = await procCommit.StandardOutput.ReadToEndAsync();
            if (procCommit.ExitCode != 0)
                _logger.LogWarning("git commit failed in worktree: {Error}", commitError);
            else
                _logger.LogInformation("Committed pulse changes in worktree: {Output}", commitOutput);
        }
    }

    private async Task UpdateCursorAsync(int projectId, string newHash, CancellationToken ct)
    {
        var cursor = await _db.ProjectPulseCursors.FindAsync(new object[] { projectId }, ct);
        if (cursor == null)
        {
            cursor = new ProjectPulseCursor
            {
                ProjectId = projectId,
                LastKnownHash = newHash,
                UpdatedAt = DateTime.UtcNow
            };
            _db.ProjectPulseCursors.Add(cursor);
        }
        else
        {
            cursor.LastKnownHash = newHash;
            cursor.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }
}
