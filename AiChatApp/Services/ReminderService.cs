using AiChatApp.Data;
using AiChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace AiChatApp.Services;

public class ReminderService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ReminderService> _logger;

    public ReminderService(IServiceProvider serviceProvider, ILogger<ReminderService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Reminder Service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckRemindersAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while checking reminders.");
            }

            // Check every minute
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }

        _logger.LogInformation("Reminder Service is stopping.");
    }

    private async Task CheckRemindersAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var webPushService = scope.ServiceProvider.GetRequiredService<WebPushService>();
        var brain = scope.ServiceProvider.GetRequiredService<ProactiveBrainService>();

        var now = DateTime.UtcNow;
        
        // Find tasks that are due now or in the next minute, and haven't been notified yet
        var upcomingTasks = await db.TodoItems
            .Where(t => t.DueDate != null && !t.IsCompleted && !t.IsNotified && t.DueDate <= now.AddMinutes(1))
            .ToListAsync();

        foreach (var task in upcomingTasks)
        {
            if (task.UserId.HasValue)
            {
                _logger.LogInformation("Sending reminder for task: {Title} to user {UserId}", task.Title, task.UserId);
                
                // 1. Send via ProactiveBrain (SignalR + Web Push)
                await brain.SendSuggestionAsync(new ProactiveSuggestion
                {
                    UserId = task.UserId,
                    Title = "任务提醒",
                    Content = $"您有一个即将到期的任务：**{task.Title}**",
                    Type = "task",
                    Actions = new List<SuggestionAction>
                    {
                        new() { Label = "查看任务", Command = "dismiss", Style = "btn-primary" }
                    }
                });

                task.IsNotified = true;
            }
        }

        if (upcomingTasks.Any())
        {
            await db.SaveChangesAsync();
        }
    }
}
