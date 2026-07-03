using AiChatApp.Data;
using AiChatApp.Models;
using AiChatApp.Services.Pulse;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiChatApp.Services;

public class ProjectPulseService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProjectPulseService> _logger;
    private readonly IConfiguration _configuration;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(10);

    public ProjectPulseService(
        IServiceProvider serviceProvider,
        ILogger<ProjectPulseService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ProjectPulseService (Loop) started with multi-project Git scanning.");

        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformPulseCheckAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during project pulse check.");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    /// <summary>
    /// 管理者からの手動トリガー用。定期ループを待たずに、指定した1プロジェクトだけ即座に
    /// ProcessProjectAsync を実行する。ProactiveSettings:Enabled が false でも動作する
    /// （明示的なリクエストなので、定期スキャンの有効/無効設定とは独立させる）。
    /// </summary>
    /// <returns>プロジェクトが見つかり処理を実行できた場合 true。</returns>
    public async Task<bool> TriggerProjectPulseAsync(int projectId, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = await db.Projects.Include(p => p.Agents).FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return false;

        await ReconcileOrphanedLedgersAsync(ct);

        var gitPulseSource = scope.ServiceProvider.GetRequiredService<GitCommitPulseSource>();
        var pulseAction = scope.ServiceProvider.GetRequiredService<PulseActionService>();
        await ProcessProjectAsync(project, db, gitPulseSource, pulseAction, ct);
        return true;
    }

    /// <summary>
    /// Status="running" のまま一定時間(既定30分。PulseActionService の重複判定しきい値と合わせる)
    /// 更新が止まっている ledger を「孤児」とみなし failed に倒し、対応する worktree/branch を
    /// 強制回収する。プロセスが CooperateAsync 実行中に kill/クラッシュした場合、finally が
    /// 走らず ledger が running のまま・worktree ディレクトリが残ったままになるケースへの対策。
    /// 同じ sourceKey の変更が二度と来なければ通常経路では永遠に気づけないため、定期チェックの
    /// 冒頭と手動トリガー時の両方で呼ぶ。
    /// </summary>
    private async Task ReconcileOrphanedLedgersAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var worktreeManager = scope.ServiceProvider.GetRequiredService<WorktreeManagerService>();

        var staleMinutes = _configuration.GetValue<int>("ProactiveSettings:StaleRunningMinutes");
        if (staleMinutes <= 0) staleMinutes = 30;
        var cutoff = DateTime.UtcNow.AddMinutes(-staleMinutes);

        var staleLedgers = await db.ProjectPulseLedgers
            .Where(l => l.Status == "running" && l.CreatedAt < cutoff)
            .ToListAsync(ct);

        if (staleLedgers.Count == 0) return;

        var projectIds = staleLedgers.Select(l => l.ProjectId).Distinct().ToList();
        var projects = await db.Projects
            .Where(p => projectIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        foreach (var ledger in staleLedgers)
        {
            _logger.LogWarning(
                "Reconciling orphaned pulse ledger {Id} (ProjectId={ProjectId}, stuck in 'running' since {CreatedAt}).",
                ledger.Id, ledger.ProjectId, ledger.CreatedAt);

            if (!string.IsNullOrEmpty(ledger.BranchName) && projects.TryGetValue(ledger.ProjectId, out var project))
            {
                try
                {
                    var worktreePath = WorktreeManagerService.ComputeWorktreePath(project.RootPath, ledger.BranchName);
                    await worktreeManager.RemoveAsync(project.RootPath, worktreePath, ledger.BranchName, deleteBranch: true, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean up orphaned worktree for ledger {Id}", ledger.Id);
                }
            }

            ledger.Status = "failed";
            ledger.CompletedAt = DateTime.UtcNow;
            ledger.ResultSummary = string.IsNullOrEmpty(ledger.ResultSummary)
                ? "[Reconciled] Orphaned 'running' ledger auto-marked as failed; worktree/branch cleanup attempted."
                : ledger.ResultSummary + " [Reconciled: orphaned running ledger auto-marked as failed; worktree cleanup attempted.]";
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task PerformPulseCheckAsync(CancellationToken ct)
    {
        if (!_configuration.GetValue<bool>("ProactiveSettings:Enabled"))
        {
            _logger.LogDebug("ProjectPulse is disabled via configuration.");
            return;
        }

        await ReconcileOrphanedLedgersAsync(ct);

        List<Project> projects;
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            projects = await db.Projects.Include(p => p.Agents).ToListAsync(ct);
        }

        var maxConcurrent = _configuration.GetValue<int>("ProactiveSettings:MaxConcurrentProjects");
        if (maxConcurrent <= 0) maxConcurrent = 2;

        using var semaphore = new SemaphoreSlim(maxConcurrent);

        // 各プロジェクトごとに独立した DI スコープ（= 独立した AppDbContext インスタンス）を張る。
        // DbContext はスレッドセーフではないため、Task.WhenAll で並行実行する複数プロジェクト間で
        // 同一インスタンスを共有すると "A second operation was started on this context instance..."
        // が発生し、cursor の重複 INSERT など競合破損を招く。
        var tasks = projects.Select(async project =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                using var projectScope = _serviceProvider.CreateScope();
                var db = projectScope.ServiceProvider.GetRequiredService<AppDbContext>();
                var gitPulseSource = projectScope.ServiceProvider.GetRequiredService<GitCommitPulseSource>();
                var pulseAction = projectScope.ServiceProvider.GetRequiredService<PulseActionService>();
                await ProcessProjectAsync(project, db, gitPulseSource, pulseAction, ct);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task ProcessProjectAsync(
        Project project,
        AppDbContext db,
        GitCommitPulseSource gitPulseSource,
        PulseActionService pulseAction,
        CancellationToken ct)
    {
        try
        {
            var currentHash = await gitPulseSource.GetCurrentCommitHashAsync(project.RootPath);
            if (string.IsNullOrEmpty(currentHash))
            {
                _logger.LogDebug("Project {ProjectId} ({Name}) is not a git repository, skipping.", project.Id, project.Name);
                return;
            }

            var cursor = await db.ProjectPulseCursors
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ProjectId == project.Id, ct);

            if (cursor == null)
            {
                cursor = new ProjectPulseCursor
                {
                    ProjectId = project.Id,
                    LastKnownHash = currentHash,
                    UpdatedAt = DateTime.UtcNow
                };
                db.ProjectPulseCursors.Add(cursor);
                await db.SaveChangesAsync(ct);
                _logger.LogDebug("Initialized cursor for project {Name} at {Hash}", project.Name, currentHash[..8]);
                return;
            }

            if (cursor.LastKnownHash == currentHash)
            {
                _logger.LogDebug("No new commits for project {Name}.", project.Name);
                return;
            }

            var sourceKey = $"{cursor.LastKnownHash}..{currentHash}";
            _logger.LogInformation("Detected changes for project {Name}: {Key}", project.Name, sourceKey);

            await pulseAction.ExecuteAsync(project, sourceKey, cursor.LastKnownHash, currentHash, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing project {Name} (Id={Id})", project.Name, project.Id);
        }
    }
}
