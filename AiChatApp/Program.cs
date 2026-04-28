using AiChatApp.Extensions;
using AiChatApp.Endpoints;
using AiChatApp.Services;
using AiChatApp.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add Services
builder.Services.AddProjectServices(builder.Configuration);

var app = builder.Build();

// Configure Middleware
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles();

// Initialize Database and Pipelines
await app.InitializeDatabaseAsync();

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

app.MapHub<ProactiveAgentHub>("/hub/proactive-agent");

app.Run();
