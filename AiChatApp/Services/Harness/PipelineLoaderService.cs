using System.Text.Json;
using System.Text.Json.Serialization;
using AiChatApp.Data;
using AiChatApp.Models.Harness;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiChatApp.Services.Harness;

public record ResolvedPrompt(string Content, int? VariantId);

public class PipelineLoaderService
{
    private readonly string _pipelinesDir;
    private readonly string _promptsDir;
    private readonly double _challengerTraffic;
    private readonly ILogger<PipelineLoaderService> _logger;
    private Dictionary<string, PipelineConfig> _cache = new();
    private FileSystemWatcher? _watcher;
    private readonly object _lockObj = new();

    public PipelineLoaderService(ILogger<PipelineLoaderService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _challengerTraffic = configuration.GetValue<double?>("AiSettings:Evolution:ChallengerTraffic") ?? 0.3;
        
        // Try to find pipelines directory in several potential locations
        var baseDir = AppContext.BaseDirectory;
        var searchPaths = new[]
        {
            Path.Combine(baseDir, "..", "..", "pipelines"),     // bin/Debug/net10.0/../../pipelines -> bin/pipelines
            Path.Combine(baseDir, "..", "..", "..", "pipelines"), // bin/Debug/net10.0/../../../pipelines -> project root/pipelines
            Path.Combine(baseDir, "pipelines"),                 // same dir (published)
            Path.Combine(Directory.GetCurrentDirectory(), "AiChatApp", "pipelines"), // relative to workspace root
            Path.Combine(Directory.GetCurrentDirectory(), "pipelines")              // relative to project root
        };

        _pipelinesDir = searchPaths.FirstOrDefault(Directory.Exists) ?? searchPaths[1];
        _promptsDir = Path.Combine(_pipelinesDir, "prompts");
        
        _logger.LogInformation($"Using pipelines directory: {_pipelinesDir}");
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

    /// <summary>変体ファイルの置き場所。ベースラインの prompts/ は決して書き換えない。</summary>
    public string VariantsDir => Path.Combine(_promptsDir, "variants");

    public IReadOnlyList<PipelineConfig> GetAllPipelines()
    {
        lock (_lockObj)
        {
            return _cache.Values.ToList();
        }
    }

    /// <summary>
    /// Loop 2 (prompt evolution) の変体解決。champion 変体があればそれを、
    /// 有効な challenger があれば ChallengerTraffic の確率でそちらを返す。
    /// いかなるエラーでもベースラインにフォールバックし、サービングを壊さない。
    /// </summary>
    public async Task<ResolvedPrompt> ResolvePromptAsync(string templatePath, AppDbContext db, Random rng)
    {
        try
        {
            var active = await db.PromptVariants
                .Where(v => v.TemplatePath == templatePath && (v.Status == "champion" || v.Status == "challenger"))
                .ToListAsync();

            var champion = active.FirstOrDefault(v => v.Status == "champion");
            var challenger = active.FirstOrDefault(v => v.Status == "challenger");

            var chosen = challenger != null && rng.NextDouble() < _challengerTraffic ? challenger : champion;
            if (chosen != null)
            {
                var variantPath = Path.Combine(VariantsDir, chosen.FileName);
                if (File.Exists(variantPath))
                {
                    return new ResolvedPrompt(await File.ReadAllTextAsync(variantPath), chosen.Id);
                }
                _logger.LogWarning("Prompt variant file missing, falling back to baseline: {Path}", variantPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ResolvePromptAsync failed for {Template}, falling back to baseline", templatePath);
        }

        return new ResolvedPrompt(await GetPromptTemplateAsync(templatePath), null);
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
