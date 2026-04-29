using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using AiChatApp.Models;

namespace AiChatApp.Services
{
    /// <summary>
    /// 项目脉搏服务：定期扫描项目状态（哨兵阶段），并驱动主动分析。
    /// </summary>
    public class ProjectPulseService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ProjectPulseService> _logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(15);

        public ProjectPulseService(
            IServiceProvider serviceProvider,
            ILogger<ProjectPulseService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ProjectPulseService (Sentinel) started.");

            // 启动时延迟一小会儿，等待系统稳定
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

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
            _logger.LogDebug("Performing sentinel pulse check...");

            using var scope = _serviceProvider.CreateScope();
            var proactiveBrain = scope.ServiceProvider.GetRequiredService<ProactiveBrainService>();
            
            // 1. 哨兵扫描 (Sentinel Phase)
            // TODO: 实现具体的增量扫描逻辑（检查 Todo 状态、编译日志等）
            // 目前先实现一个简单的触发器作为演示
            
            _logger.LogDebug("Sentinel scan complete. No significant changes detected (Placeholder).");
            
            // 未来这里会对比快照，如果发现重要变动：
            // await proactiveBrain.AnalyzeProjectPulseAsync(changes);
        }
    }
}
