using AiChatApp.Extensions;
using AiChatApp.Endpoints;
using AiChatApp.Services;

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

app.Run("http://0.0.0.0:5000");
