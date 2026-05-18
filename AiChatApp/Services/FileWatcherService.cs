using AiChatApp.Hubs;
using Microsoft.AspNetCore.SignalR;
using System.IO;

namespace AiChatApp.Services;

public class FileWatcherService : BackgroundService
{
    private readonly IHubContext<ProactiveAgentHub> _hubContext;
    private readonly ILogger<FileWatcherService> _logger;
    private readonly IConfiguration _configuration;
    private FileSystemWatcher? _watcher;
    private readonly SemaphoreSlim _throttle = new(1, 1);
    private DateTime _lastNotification = DateTime.MinValue;
    private readonly TimeSpan _throttleInterval = TimeSpan.FromSeconds(2);

    public FileWatcherService(
        IHubContext<ProactiveAgentHub> hubContext,
        ILogger<FileWatcherService> logger,
        IConfiguration configuration)
    {
        _hubContext = hubContext;
        _logger = logger;
        _configuration = configuration;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var watchPath = _configuration.GetValue<string>("FileWatcher:Path") ?? "/home/ubuntu/ws/ai-chat-app";
        
        if (!Directory.Exists(watchPath))
        {
            _logger.LogWarning("Watch path does not exist: {Path}", watchPath);
            return Task.CompletedTask;
        }

        _watcher = new FileSystemWatcher(watchPath)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Renamed += OnFileRenamed;

        _logger.LogInformation("FileWatcher started monitoring: {Path}", watchPath);

        stoppingToken.Register(() =>
        {
            _watcher?.Dispose();
            _throttle.Dispose();
        });

        return Task.CompletedTask;
    }

    private async void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        await NotifyFileChange(e.FullPath, e.ChangeType.ToString());
    }

    private async void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        await NotifyFileChange(e.FullPath, $"Renamed from {e.OldFullPath}");
    }

    private async Task NotifyFileChange(string filePath, string changeType)
    {
        try
        {
            await _throttle.WaitAsync();

            var now = DateTime.UtcNow;
            if ((now - _lastNotification) < _throttleInterval)
            {
                return;
            }
            _lastNotification = now;

            var fileName = Path.GetFileName(filePath);
            var relativePath = GetRelativePath(filePath);

            _logger.LogDebug("File change detected: {File} - {Type}", relativePath, changeType);

            var message = new
            {
                type = "file-change",
                filePath = relativePath,
                fileName,
                changeType,
                timestamp = DateTime.UtcNow
            };

            await _hubContext.Clients.All.SendAsync("FileChanged", message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error notifying file change");
        }
        finally
        {
            _throttle.Release();
        }
    }

    private string GetRelativePath(string fullPath)
    {
        var basePath = _configuration.GetValue<string>("FileWatcher:Path") ?? "/home/ubuntu/ws/ai-chat-app";
        return Path.GetRelativePath(basePath, fullPath);
    }

    public override void Dispose()
    {
        _watcher?.Dispose();
        _throttle.Dispose();
        base.Dispose();
    }
}
