using AiChatApp.Data;
using AiChatApp.Hubs;
using AiChatApp.Services;
using AiChatApp.Services.Harness;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.SignalR;
using AiChatApp.Services.Infrastructure;

namespace AiChatApp.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddProjectServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        services.AddDbContext<AppDbContext>(options => 
            options.UseSqlite(connectionString));
        
        services.AddMemoryCache();
        services.AddSingleton<IBackgroundTaskTracker, BackgroundTaskTracker>();
        services.AddSingleton<ICliExecutor, CliExecutorService>();
        services.AddSingleton<MemoryFileService>();
        services.AddSingleton<MemoryGraphService>();
        services.AddScoped<MemorySearchService>();
        services.AddScoped<MemoryConsolidationService>(); // fire-and-forget用にScopedのまま
        services.AddScoped<ProjectService>();
        services.AddScoped<SessionMemoryService>();
        services.AddScoped<SkillLearningService>();
        services.AddSingleton<SkillManagerService>();
        services.AddSingleton<PipelineLoaderService>();
        services.AddScoped<SchemaValidationService>();
        services.AddScoped<ToolExecutorService>();
        services.AddScoped<EvalService>();
        services.AddScoped<AiPromptService>();
        services.AddScoped<AiResponseProcessor>();
        services.AddScoped<AiCollaborationService>();
        services.AddScoped<AiService>();
        services.AddScoped<AssistantToolService>();
        services.AddSingleton<ProactiveBrainService>();
        services.AddSingleton<WebPushService>();

        services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options => {
            options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        });

        services.AddAntiforgery();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options => {
                options.LoginPath = "/login";
                options.AccessDeniedPath = "/login";
            });
        services.AddAuthorization(options => {
            options.AddPolicy("AdminOnly", policy => policy.RequireClaim("IsAdmin", "true"));
        });

        services.AddSignalR();
        
        // ENABLED: Proactive Scanning (Sentinel Phase)
        services.AddHostedService<FileWatcherService>();
        services.AddHostedService<ProjectPulseService>();
        services.AddHostedService<ReminderService>();

        return services;
    }
}
