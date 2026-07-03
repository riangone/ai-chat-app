using System.Diagnostics;
using AiChatApp.Models;
using Microsoft.Extensions.Logging;

namespace AiChatApp.Services.Pulse;

public class GitCommitPulseSource : IPulseSource
{
    private readonly ILogger<GitCommitPulseSource> _logger;

    public string SourceType => "git_commit";

    public GitCommitPulseSource(ILogger<GitCommitPulseSource> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<PulseItem>> DiscoverAsync(Project project, CancellationToken ct)
    {
        var repoRoot = project.RootPath;

        var currentHash = await GetCurrentCommitHashAsync(repoRoot);
        if (string.IsNullOrEmpty(currentHash))
        {
            _logger.LogDebug("Project {ProjectId} ({Name}) is not a git repository or has no commits.", project.Id, project.Name);
            return Array.Empty<PulseItem>();
        }

        return new List<PulseItem>
        {
            new PulseItem(currentHash, $"Current HEAD: {currentHash[..Math.Min(8, currentHash.Length)]}", null)
        };
    }

    public async Task<string?> GetCurrentCommitHashAsync(string workingDir)
    {
        try
        {
            var psi = new ProcessStartInfo("git", "rev-parse HEAD")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WorkingDirectory = workingDir
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return output.Trim();
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> GetDiffSummaryAsync(string workingDir, string oldHash, string newHash)
    {
        try
        {
            var psi = new ProcessStartInfo("git", $"diff --stat {oldHash} {newHash}")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WorkingDirectory = workingDir
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            if (output.Split('\n').Length < 20)
            {
                var psiDetail = new ProcessStartInfo("git", $"diff -U1 {oldHash} {newHash}")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    WorkingDirectory = workingDir
                };
                using var procDetail = Process.Start(psiDetail);
                var detail = await procDetail!.StandardOutput.ReadToEndAsync();
                await procDetail.WaitForExitAsync();

                const int maxDiffChars = 3000;
                return detail.Length > maxDiffChars
                    ? detail[..maxDiffChars] + "\n... [diff truncated]"
                    : detail;
            }

            return output;
        }
        catch
        {
            return null;
        }
    }
}
