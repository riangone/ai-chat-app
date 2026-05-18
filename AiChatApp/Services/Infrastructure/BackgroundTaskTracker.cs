using Microsoft.Extensions.Logging;

namespace AiChatApp.Services.Infrastructure;

public interface IBackgroundTaskTracker
{
    void FireAndForget(Func<Task> task, string description, Action<Exception>? onError = null);
}

public class BackgroundTaskTracker : IBackgroundTaskTracker
{
    private readonly ILogger<BackgroundTaskTracker> _logger;

    public BackgroundTaskTracker(ILogger<BackgroundTaskTracker> logger)
    {
        _logger = logger;
    }

    public void FireAndForget(Func<Task> task, string description, Action<Exception>? onError = null)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                _logger.LogDebug("Starting background task: {Description}", description);
                await task();
                _logger.LogDebug("Completed background task: {Description}", description);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background task failed: {Description}", description);
                onError?.Invoke(ex);
            }
        });
    }
}
