using System.Text.Json;
using System.Text.Json.Serialization;
using AiChatApp.Models.Harness;
using Microsoft.Extensions.Logging;

namespace AiChatApp.Services.Harness;

public class PipelineLoaderService
{
    private readonly string _pipelinesDir;
    private readonly string _promptsDir;
    private readonly ILogger<PipelineLoaderService> _logger;
    private Dictionary<string, PipelineConfig> _cache = new();
    private FileSystemWatcher? _watcher;
    private readonly object _lockObj = new();

    public PipelineLoaderService(ILogger<PipelineLoaderService> logger)
    {
        _logger = logger;
        _pipelinesDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "pipelines");
        _promptsDir = Path.Combine(_pipelinesDir, "prompts");
    }

    public async Task LoadAllAsync()
    {
        try
        {
            lock (_lockObj)
            {
                _cache.Clear();
            }

            if (!Directory.Exists(_pipelinesDir))
            {
                _logger.LogWarning($"Pipelines directory not found: {_pipelinesDir}");
                return;
            }

            var jsonFiles = Directory.GetFiles(_pipelinesDir, "*.json");
            foreach (var file in jsonFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                    };
                    var config = JsonSerializer.Deserialize<PipelineConfig>(json, options);
                    if (config != null && !string.IsNullOrEmpty(config.Name))
                    {
                        lock (_lockObj)
                        {
                            _cache[config.Name] = config;
                        }
                        _logger.LogInformation($"Loaded pipeline: {config.Name}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to load pipeline from {file}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in LoadAllAsync: {ex.Message}");
        }
    }

    public PipelineConfig? Get(string name)
    {
        lock (_lockObj)
        {
            return _cache.GetValueOrDefault(name);
        }
    }

    public IReadOnlyList<string> GetNames()
    {
        lock (_lockObj)
        {
            return _cache.Keys.ToList().AsReadOnly();
        }
    }

    public async Task<string> GetPromptTemplateAsync(string templatePath)
    {
        if (string.IsNullOrEmpty(templatePath))
        {
            throw new ArgumentException("Template path cannot be empty", nameof(templatePath));
        }

        var fullPath = Path.Combine(_promptsDir, templatePath);
        
        if (!File.Exists(fullPath))
        {
            _logger.LogError($"Prompt template not found: {fullPath}");
            throw new FileNotFoundException($"Prompt template not found: {templatePath}");
        }

        return await File.ReadAllTextAsync(fullPath);
    }

    public void WatchForChanges(Action<string> onFileChanged)
    {
        if (!Directory.Exists(_pipelinesDir))
        {
            _logger.LogWarning($"Cannot watch non-existent directory: {_pipelinesDir}");
            return;
        }

        _watcher = new FileSystemWatcher(_pipelinesDir)
        {
            Filter = "*.json",
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
        };

        _watcher.Changed += (s, e) =>
        {
            _logger.LogInformation($"Pipeline file changed: {e.Name}");
            Task.Delay(100).ContinueWith(_ => onFileChanged?.Invoke(e.Name ?? string.Empty));
        };

        _watcher.Created += (s, e) =>
        {
            _logger.LogInformation($"Pipeline file created: {e.Name}");
            Task.Delay(100).ContinueWith(_ => onFileChanged?.Invoke(e.Name ?? string.Empty));
        };

        _watcher.EnableRaisingEvents = true;
        _logger.LogInformation("Pipeline file watcher started");
    }

    public async Task ReloadPipelineAsync(string pipelineFileName)
    {
        var filePath = Path.Combine(_pipelinesDir, pipelineFileName);
        
        if (!File.Exists(filePath))
        {
            _logger.LogWarning($"Pipeline file not found for reload: {filePath}");
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            var config = JsonSerializer.Deserialize<PipelineConfig>(json, options);
            if (config != null && !string.IsNullOrEmpty(config.Name))
            {
                lock (_lockObj)
                {
                    _cache[config.Name] = config;
                }
                _logger.LogInformation($"Reloaded pipeline: {config.Name}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to reload pipeline {pipelineFileName}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
