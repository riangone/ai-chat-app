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
                
                string content = $"您有一个即将到期的任务：**{task.Title}**";

                // Hyperion Optimization: If the task is related to timesheet submission, run the timesheet generator to include a summary
                if (task.Title.Contains("勤務表") || task.Title.ToLower().Contains("timesheet"))
                {
                    try
                    {
                        var processInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "python3",
                            Arguments = "/home/ubuntu/ws/ai-chat-app/AiChatApp/Scripts/generate_timesheet.py",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var process = System.Diagnostics.Process.Start(processInfo);
                        if (process != null)
                        {
                            string output = await process.StandardOutput.ReadToEndAsync();
                            await process.WaitForExitAsync();
                            if (!string.IsNullOrWhiteSpace(output))
                            {
                                content += $"\n\n### 📊 今日自动生成的工作简报建议：\n{output}";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to run generate_timesheet.py dynamically for task reminder");
                    }
                }

                // 1. Send via ProactiveBrain (SignalR + Web Push)
                await brain.SendSuggestionAsync(new ProactiveSuggestion
                {
                    UserId = task.UserId,
                    Title = "任务提醒",
                    Content = content,
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
