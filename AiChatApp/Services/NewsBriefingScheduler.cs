using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace AiChatApp.Services;

/// <summary>
/// 毎朝7時にニュースキャッシュを自動更新するスケジューラー。
/// </summary>
public class NewsBriefingScheduler : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NewsBriefingScheduler> _logger;
    private static readonly TimeSpan DailyRefreshTime = new(7, 0, 0);

    public NewsBriefingScheduler(IServiceProvider serviceProvider, ILogger<NewsBriefingScheduler> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NewsBriefingScheduler started. Daily refresh at {Time}.", DailyRefreshTime);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var nextRun = now.Date + DailyRefreshTime;
            if (nextRun <= now) nextRun = nextRun.AddDays(1);

            var delay = nextRun - now;
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var ai = scope.ServiceProvider.GetRequiredService<AiService>();
                var newsCache = scope.ServiceProvider.GetRequiredService<NewsCacheService>();
                _logger.LogInformation("Refreshing daily news briefing...");
                await newsCache.RefreshAsync(ai);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing daily news briefing.");
            }
        }
    }
}
