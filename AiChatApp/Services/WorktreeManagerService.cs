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

    public async Task<string> CreateAsync(string repoRootPath, string branchName, CancellationToken ct)
    {
        var worktreesDir = Path.Combine(Path.GetDirectoryName(repoRootPath) ?? ".", ".worktrees");
        var projectName = Path.GetFileName(repoRootPath);
        var worktreePath = Path.Combine(worktreesDir, $"{projectName}-{branchName.Replace('/', '-')}");

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

    public async Task RemoveAsync(string worktreePath, string branchName, bool deleteBranch, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("git", $"worktree remove --force \"{worktreePath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var proc = Process.Start(psi);
        if (proc == null)
        {
            _logger.LogWarning("Failed to start git worktree remove process.");
            return;
        }

        var error = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
            _logger.LogWarning("git worktree remove failed (may already be removed): {Error}", error);
        else
            _logger.LogInformation("Worktree removed at {Path}", worktreePath);

        if (deleteBranch)
        {
            var psiBranch = new ProcessStartInfo("git", $"branch -D {branchName}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
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
