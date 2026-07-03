using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AiChatApp.Services;

public class WorktreeManagerService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<WorktreeManagerService> _logger;
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _projectLocks = new();

    public WorktreeManagerService(IConfiguration configuration, ILogger<WorktreeManagerService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// リポジトリルートとブランチ名から worktree の絶対パスを決定論的に計算する。
    /// CreateAsync が使う式と完全に一致させること（孤児 worktree の回収時に、DBにパスを
    /// 保存していなくてもここから逆算して git worktree remove を呼べるようにするため）。
    /// </summary>
    public static string ComputeWorktreePath(string repoRootPath, string branchName)
    {
        var worktreesDir = Path.Combine(Path.GetDirectoryName(repoRootPath) ?? ".", ".worktrees");
        var projectName = Path.GetFileName(repoRootPath);
        return Path.Combine(worktreesDir, $"{projectName}-{branchName.Replace('/', '-')}");
    }

    public async Task<string> CreateAsync(string repoRootPath, string branchName, CancellationToken ct)
    {
        var worktreePath = ComputeWorktreePath(repoRootPath, branchName);
        var worktreesDir = Path.GetDirectoryName(worktreePath)!;

        Directory.CreateDirectory(worktreesDir);

        var psi = new ProcessStartInfo("git", $"worktree add -b {branchName} \"{worktreePath}\" HEAD")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repoRootPath
        };
        using var proc = Process.Start(psi);
        if (proc == null) throw new InvalidOperationException("Failed to start git worktree add process.");

        var output = await proc.StandardOutput.ReadToEndAsync(ct);
        var error = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
        {
            _logger.LogError("git worktree add failed: {Error}", error);
            throw new InvalidOperationException($"git worktree add failed: {error}");
        }

        _logger.LogInformation("Worktree created at {Path} on branch {Branch}", worktreePath, branchName);
        return worktreePath;
    }

    public async Task RemoveAsync(string repoRootPath, string worktreePath, string branchName, bool deleteBranch, CancellationToken ct)
    {
        // 重要: git worktree remove / branch -D は「その worktree を認識している репозиторジ」の
        // working directory 内で実行しないと対象を見つけられず静的に失敗する。WorkingDirectory を
        // 指定していなかった旧実装は常にこのアプリ自身(ai-chat-app)の cwd で実行していたため、
        // スキャン対象の外部プロジェクトの worktree は事実上一度も削除できず、孤児として残り続けていた。
        var psi = new ProcessStartInfo("git", $"worktree remove --force \"{worktreePath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repoRootPath
        };
        using var proc = Process.Start(psi);
        if (proc == null)
        {
            _logger.LogWarning("Failed to start git worktree remove process.");
        }
        else
        {
            var error = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            if (proc.ExitCode != 0)
                _logger.LogWarning("git worktree remove failed (may already be removed): {Error}", error);
            else
                _logger.LogInformation("Worktree removed at {Path}", worktreePath);
        }

        // git のメタデータ上は削除扱いにできなくても、ディレクトリが物理的に残っていれば
        // ディスクリークになるため保険として強制削除する（孤児 worktree 対策）。
        try
        {
            if (Directory.Exists(worktreePath))
            {
                Directory.Delete(worktreePath, recursive: true);
                _logger.LogInformation("Force-deleted leftover worktree directory at {Path}", worktreePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to force-delete leftover worktree directory at {Path}", worktreePath);
        }

        // ディレクトリを手動で消した場合、.git/worktrees/<name> の管理情報が残って
        // 次回同名ブランチでの worktree add 時に衝突しうるため、掃除しておく。
        try
        {
            var psiPrune = new ProcessStartInfo("git", "worktree prune")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = repoRootPath
            };
            using var procPrune = Process.Start(psiPrune);
            if (procPrune != null)
                await procPrune.WaitForExitAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "git worktree prune failed for {RepoRootPath}", repoRootPath);
        }

        if (deleteBranch)
        {
            var psiBranch = new ProcessStartInfo("git", $"branch -D {branchName}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = repoRootPath
            };
            using var procBranch = Process.Start(psiBranch);
            if (procBranch != null)
            {
                var branchError = await procBranch.StandardError.ReadToEndAsync(ct);
                await procBranch.WaitForExitAsync(ct);
                if (procBranch.ExitCode != 0)
                    _logger.LogWarning("git branch -D {Branch} failed: {Error}", branchName, branchError);
            }
        }
    }

    public SemaphoreSlim GetProjectLock(int projectId)
    {
        return _projectLocks.GetOrAdd(projectId, _ => new SemaphoreSlim(1, 1));
    }
}
