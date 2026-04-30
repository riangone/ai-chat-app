using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using AiChatApp.Models;
using AiChatApp.Data;
using Microsoft.EntityFrameworkCore;

namespace AiChatApp.Services
{
    /// <summary>
    /// 项目脉搏服务：使用 Git 增量扫描项目状态（哨兵阶段），并驱动主动分析。
    /// </summary>
    public class ProjectPulseService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ProjectPulseService> _logger;
        private readonly IConfiguration _configuration;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(10);
        private string? _lastProcessedCommit;

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
            _logger.LogInformation("ProjectPulseService (Sentinel) started with Git incremental scanning.");

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

        private async Task PerformPulseCheckAsync(CancellationToken stoppingToken)
        {
            var watchPath = _configuration.GetValue<string>("FileWatcher:Path") ?? Directory.GetCurrentDirectory();
            
            // 1. 获取当前 Git Hash
            var currentCommit = await GetCurrentCommitHashAsync(watchPath);
            if (string.IsNullOrEmpty(currentCommit)) return;

            // 2. 如果是首次运行或 Hash 未变，跳过分析
            if (_lastProcessedCommit == null)
            {
                _lastProcessedCommit = currentCommit;
                _logger.LogDebug("Sentinel initialized at commit {Hash}", currentCommit);
                return;
            }

            if (_lastProcessedCommit == currentCommit)
            {
                _logger.LogDebug("No new commits detected. Sentinel sleeping.");
                return;
            }

            // 3. 获取增量内容 (Git Diff Summary)
            var diffSummary = await GetDiffSummaryAsync(watchPath, _lastProcessedCommit, currentCommit);
            
            if (!string.IsNullOrWhiteSpace(diffSummary))
            {
                _logger.LogInformation("Significant changes detected between {Old} and {New}. Triggering AI analysis.", _lastProcessedCommit, currentCommit);
                
                using var scope = _serviceProvider.CreateScope();
                var proactiveBrain = scope.ServiceProvider.GetRequiredService<ProactiveBrainService>();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // 获取一个活跃用户 ID 来进行通知 (演示逻辑)
                var activeUserId = (await db.Users.Select(u => (int?)u.Id).FirstOrDefaultAsync()) ?? 1;
                
                // 触发 AI 分析 (使用 opencode 免费模型)
                await proactiveBrain.AnalyzeProjectPulseAsync(diffSummary, activeUserId);
            }

            _lastProcessedCommit = currentCommit;
        }

        private async Task<string?> GetCurrentCommitHashAsync(string workingDir)
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
            catch { return null; }
        }

        private async Task<string?> GetDiffSummaryAsync(string workingDir, string oldHash, string newHash)
        {
            try
            {
                // 只看文件统计和摘要，避免内容过长超出 Token 限制（虽然 opencode 免费，但保持效率更好）
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
                
                // 如果变更较小，可以尝试获取具体的 diff 内容（限制行数）
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
                     return detail;
                }

                return output;
            }
            catch { return null; }
        }
    }
}
