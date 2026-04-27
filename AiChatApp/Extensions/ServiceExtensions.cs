using AiChatApp.Data;
using AiChatApp.Services;
using AiChatApp.Services.Harness;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace AiChatApp.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddProjectServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options => options.UseSqlite("Data Source=chat.db"));
        
        services.AddSingleton<MemoryFileService>();
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
        services.AddScoped<AiService>();

        services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options => {
            options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        });

        services.AddAntiforgery();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options => {
                options.LoginPath = "/login";
            });
        services.AddAuthorization();

        return services;
    }
}
