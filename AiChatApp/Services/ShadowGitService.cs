using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AiChatApp.Services;

public class SnapshotInfo
{
    public string CommitHash { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime Date { get; set; }
}

public class ShadowGitService
{
    private readonly ILogger<ShadowGitService> _logger;

    public ShadowGitService(ILogger<ShadowGitService> logger)
    {
        _logger = logger;
    }

    public async Task<string?> CreateSnapshotAsync(string projectPath, string message)
    {
        try
        {
            var shadowDir = Path.Combine(projectPath, ".shadow-git");

            if (!Directory.Exists(shadowDir))
            {
                var initResult = await RunGitCommandAsync(projectPath, $"init --separate-git-dir={shadowDir}", useShadowDir: true);
                if (!string.IsNullOrEmpty(initResult))
                    _logger.LogInformation("Shadow git repo initialized at {ShadowDir}", shadowDir);
            }

            await RunGitCommandAsync(projectPath, "add -A");
            var output = await RunGitCommandAsync(projectPath, $"commit -m \"{message} [hyperion-snapshot]\"");
            var hash = ParseCommitHash(output);
            return hash;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create snapshot for {ProjectPath}", projectPath);
            return null;
        }
    }

    public async Task<List<SnapshotInfo>> ListSnapshotsAsync(string projectPath)
    {
        var snapshots = new List<SnapshotInfo>();
        try
        {
            var output = await RunGitCommandAsync(projectPath, "log --oneline --grep=\"[hyperion-snapshot]\" --format=\"%H|%ct|%s\"");
            if (string.IsNullOrEmpty(output)) return snapshots;

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('|', 3);
                if (parts.Length >= 3 && long.TryParse(parts[1], out var unixTs))
                {
                    snapshots.Add(new SnapshotInfo
                    {
                        CommitHash = parts[0],
                        Message = parts[2],
                        Date = DateTimeOffset.FromUnixTimeSeconds(unixTs).UtcDateTime
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list snapshots for {ProjectPath}", projectPath);
        }
        return snapshots;
    }

    public async Task<string> GetSnapshotDiffAsync(string projectPath, string commitHash)
    {
        try
        {
            var output = await RunGitCommandAsync(projectPath, $"show {commitHash} --stat");
            return output ?? "No diff available.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get diff for {CommitHash}", commitHash);
            return "Error retrieving diff.";
        }
    }

    public async Task<bool> RevertToSnapshotAsync(string projectPath, string commitHash)
    {
        try
        {
            var output = await RunGitCommandAsync(projectPath, $"reset --hard {commitHash}");
            return output != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revert to {CommitHash}", commitHash);
            return false;
        }
    }

    private static string? ParseCommitHash(string? output)
    {
        if (string.IsNullOrEmpty(output)) return null;
        var match = Regex.Match(output, @"\[master \(root-commit\) [a-f0-9]+\]");
        if (match.Success) return match.Value.Split(' ').LastOrDefault()?.TrimEnd(']');
        match = Regex.Match(output, @"\[master [a-f0-9]+\]");
        if (match.Success) return match.Value.Split(' ').LastOrDefault()?.TrimEnd(']');
        return output.Trim().Split('\n').LastOrDefault();
    }

    private async Task<string?> RunGitCommandAsync(string projectPath, string arguments, bool useShadowDir = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"-C \"{projectPath}\" {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (useShadowDir)
        {
            psi.Arguments = $"-C \"{projectPath}\" {arguments}";
        }

        var process = new Process { StartInfo = psi };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 && !string.IsNullOrEmpty(error))
        {
            _logger.LogWarning("Git command failed: git {Args}: {Error}", arguments, error);
            if (string.IsNullOrEmpty(output)) return null;
        }

        return output;
    }
}
