using AiChatApp.Data;
using AiChatApp.Models;
using AiChatApp.Services.Pulse;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiChatApp.Services;

public class ProjectPulseService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProjectPulseService> _logger;
    private readonly IConfiguration _configuration;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(10);

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
        _logger.LogInformation("ProjectPulseService (Loop) started with multi-project Git scanning.");

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

    private async Task PerformPulseCheckAsync(CancellationToken ct)
    {
        if (!_configuration.GetValue<bool>("ProactiveSettings:Enabled"))
        {
            _logger.LogDebug("ProjectPulse is disabled via configuration.");
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var gitPulseSource = scope.ServiceProvider.GetRequiredService<GitCommitPulseSource>();
        var pulseAction = scope.ServiceProvider.GetRequiredService<PulseActionService>();

        var projects = await db.Projects.Include(p => p.Agents).ToListAsync(ct);
        var maxConcurrent = _configuration.GetValue<int>("ProactiveSettings:MaxConcurrentProjects");
        if (maxConcurrent <= 0) maxConcurrent = 2;

        using var semaphore = new SemaphoreSlim(maxConcurrent);

        var tasks = projects.Select(async project =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await ProcessProjectAsync(project, db, gitPulseSource, pulseAction, ct);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task ProcessProjectAsync(
        Project project,
        AppDbContext db,
        GitCommitPulseSource gitPulseSource,
        PulseActionService pulseAction,
        CancellationToken ct)
    {
        try
        {
            var currentHash = await gitPulseSource.GetCurrentCommitHashAsync(project.RootPath);
            if (string.IsNullOrEmpty(currentHash))
            {
                _logger.LogDebug("Project {ProjectId} ({Name}) is not a git repository, skipping.", project.Id, project.Name);
                return;
            }

            var cursor = await db.ProjectPulseCursors
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ProjectId == project.Id, ct);

            if (cursor == null)
            {
                cursor = new ProjectPulseCursor
                {
                    ProjectId = project.Id,
                    LastKnownHash = currentHash,
                    UpdatedAt = DateTime.UtcNow
                };
                db.ProjectPulseCursors.Add(cursor);
                await db.SaveChangesAsync(ct);
                _logger.LogDebug("Initialized cursor for project {Name} at {Hash}", project.Name, currentHash[..8]);
                return;
            }

            if (cursor.LastKnownHash == currentHash)
            {
                _logger.LogDebug("No new commits for project {Name}.", project.Name);
                return;
            }

            var sourceKey = $"{cursor.LastKnownHash}..{currentHash}";
            _logger.LogInformation("Detected changes for project {Name}: {Key}", project.Name, sourceKey);

            await pulseAction.ExecuteAsync(project, sourceKey, cursor.LastKnownHash, currentHash, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing project {Name} (Id={Id})", project.Name, project.Id);
        }
    }
}
