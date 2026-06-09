using AiChatApp.Extensions;
using AiChatApp.Endpoints;
using AiChatApp.Services;
using AiChatApp.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MinResponseDataRate = null;
    options.Limits.MinRequestBodyDataRate = null;
});

// Add Services
builder.Services.AddProjectServices(builder.Configuration);

var app = builder.Build();

// Configure Middleware
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.File.Name;
        if (path.EndsWith(".html") || path == "sw.js")
        {
            ctx.Context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            ctx.Context.Response.Headers["Pragma"] = "no-cache";
        }
        else if (path.EndsWith(".js") || path.EndsWith(".css"))
        {
            ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=3600";
        }
    }
});

// Initialize Database and Pipelines
await app.InitializeDatabaseAsync();
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<MemoryFileService>();
}

// Map Endpoints
app.MapAuthEndpoints();
app.MapChatEndpoints();
app.MapMemoryEndpoints();
app.MapSkillEndpoints();
app.MapCliEndpoints();
app.MapHarnessEndpoints();
app.MapProjectEndpoints();
app.MapInputHistoryEndpoints();
app.MapTodoEndpoints();
app.MapNotesEndpoints();
app.MapFileManagerEndpoints();
app.MapStatsEndpoints();
app.MapInboxEndpoints();
app.MapBriefingEndpoints();
app.MapNotificationEndpoints();
app.MapAttachmentEndpoints();
app.MapFinanceEndpoints();
app.MapCrewEndpoints();

app.MapHub<ProactiveAgentHub>("/hub/proactive-agent");

app.Run();
