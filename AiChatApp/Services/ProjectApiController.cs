using AiChatApp.Models;
using AiChatApp.Services;
using AiChatApp.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AiChatApp.Services;

public static class ProjectApiController
{
    public static void MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects").RequireAuthorization();

        group.MapGet("/", async (ProjectService projectService, ClaimsPrincipal user) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var projects = await projectService.GetUserProjectsAsync(userId);
            return Results.Ok(projects);
        });

        group.MapPost("/", async ([FromForm] string name, [FromForm] string rootPath, ProjectService projectService, ClaimsPrincipal user) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var p = await projectService.CreateProjectAsync(name, rootPath, userId);
            
            var html = $@"
                <div class='flex items-center group w-full mb-1'>
                    <button onclick='selectProject({p.Id}, ""{p.Name}"", ""{p.RootPath}"")' 
                            class='btn btn-ghost btn-sm h-auto py-2 flex-1 justify-start font-normal text-left whitespace-normal break-all'>
                        <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""1.5"" stroke=""currentColor"" class=""w-4 h-4 mr-2 shrink-0""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""M2.25 12.75V12A2.25 2.25 0 0 1 4.5 9.75h15A2.25 2.25 0 0 1 21.75 12v.75m-19.5 0A2.25 2.25 0 0 0 4.5 15h15a2.25 2.25 0 0 0 2.25-2.25m-19.5 0v.25A2.25 2.25 0 0 0 4.5 17.5h15a2.25 2.25 0 0 0 2.25-2.25v-.25"" /></svg>
                        <span class='flex-1'>{p.Name}</span>
                    </button>
                    <button hx-delete='/api/projects/{p.Id}' hx-target='closest div' hx-swap='outerHTML' class='btn btn-ghost btn-xs opacity-0 group-hover:opacity-60 px-1'>
                        <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""1.5"" stroke=""currentColor"" class=""w-4 h-4""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""m14.74 9-.346 9m-4.788 0L9.26 9m9.968-3.21c.342.052.682.107 1.022.166m-1.022-.165L18.16 19.673a2.25 2.25 0 0 1-2.244 2.077H8.084a2.25 2.25 0 0 1-2.244-2.077L4.772 5.79m14.456 0a48.108 48.108 0 0 0-3.478-.397m-12 .562c.34-.059.68-.114 1.022-.165m0 0a48.11 48.11 0 0 1 3.478-.397m7.5 0v-.916c0-1.18-.91-2.164-2.09-2.201a51.964 51.964 0 0 0-3.32 0c-1.18.037-2.09 1.022-2.09 2.201v.916m7.5 0a48.667 48.667 0 0 0-7.5 0"" /></svg>
                    </button>
                </div>";
            return Results.Content(html, "text/html");
        }).DisableAntiforgery();

        group.MapDelete("/{id}", async (int id, ProjectService projectService, ClaimsPrincipal user) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await projectService.DeleteProjectAsync(id, userId);
            return Results.Ok();
        });

        group.MapPost("/{id}/agents", async (int id, [FromForm] string roleName, [FromForm] string systemPrompt, [FromForm] string color, [FromForm] string? preferredProvider, ProjectService projectService, ClaimsPrincipal user) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var project = await projectService.GetProjectAsync(id, userId);
            if (project == null) return Results.NotFound();

            var agent = await projectService.AddAgentAsync(id, roleName, systemPrompt, color, preferredProvider);
            return Results.Ok(agent);
        }).DisableAntiforgery();

        group.MapDelete("/{projectId}/agents/{agentId}", async (
            int projectId, int agentId, ProjectService projectService, ClaimsPrincipal user) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var project = await projectService.GetProjectAsync(projectId, userId);
            if (project == null) return Results.NotFound();
            await projectService.DeleteAgentAsync(agentId, userId);
            return Results.Ok();
        });

        group.MapPut("/{projectId}/agents/{agentId}", async (
            int projectId, int agentId, 
            [FromForm] string roleName, [FromForm] string systemPrompt, [FromForm] string color, [FromForm] string? preferredProvider,
            ProjectService projectService, ClaimsPrincipal user) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var agent = await projectService.UpdateAgentAsync(agentId, roleName, systemPrompt, color, preferredProvider, userId);
            return agent != null ? Results.Ok(agent) : Results.NotFound();
        }).DisableAntiforgery();
        
        // HTMX components for UI
        group.MapGet("/list-html", async (ProjectService projectService, ClaimsPrincipal user) => 
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var projects = await projectService.GetUserProjectsAsync(userId);
            
            var html = string.Concat(projects.Select(p => $@"
                <div class='flex items-center group w-full mb-1'>
                    <button onclick='selectProject({p.Id}, ""{p.Name}"", ""{p.RootPath}"")' 
                            class='btn btn-ghost btn-sm h-auto py-2 flex-1 justify-start font-normal text-left whitespace-normal break-all'>
                        <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""1.5"" stroke=""currentColor"" class=""w-4 h-4 mr-2 shrink-0""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""M2.25 12.75V12A2.25 2.25 0 0 1 4.5 9.75h15A2.25 2.25 0 0 1 21.75 12v.75m-19.5 0A2.25 2.25 0 0 0 4.5 15h15a2.25 2.25 0 0 0 2.25-2.25m-19.5 0v.25A2.25 2.25 0 0 0 4.5 17.5h15a2.25 2.25 0 0 0 2.25-2.25v-.25"" /></svg>
                        <span class='flex-1'>{p.Name}</span>
                    </button>
                    <button hx-delete='/api/projects/{p.Id}' hx-target='closest div' hx-swap='outerHTML' class='btn btn-ghost btn-xs opacity-0 group-hover:opacity-60 px-1'>
                        <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""1.5"" stroke=""currentColor"" class=""w-4 h-4""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""m14.74 9-.346 9m-4.788 0L9.26 9m9.968-3.21c.342.052.682.107 1.022.166m-1.022-.165L18.16 19.673a2.25 2.25 0 0 1-2.244 2.077H8.084a2.25 2.25 0 0 1-2.244-2.077L4.772 5.79m14.456 0a48.108 48.108 0 0 0-3.478-.397m-12 .562c.34-.059.68-.114 1.022-.165m0 0a48.11 48.11 0 0 1 3.478-.397m7.5 0v-.916c0-1.18-.91-2.164-2.09-2.201a51.964 51.964 0 0 0-3.32 0c-1.18.037-2.09 1.022-2.09 2.201v.916m7.5 0a48.667 48.667 0 0 0-7.5 0"" /></svg>
                    </button>
                </div>"));
            return Results.Content(html, "text/html");
        });

        group.MapGet("/{projectId}/pipeline-logs", async (int projectId, AppDbContext db, ClaimsPrincipal user) =>
        {
            var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return Results.Unauthorized();
            var userId = int.Parse(userIdStr);
            
            var sessions = await db.ChatSessions
                .Where(s => s.ProjectId == projectId && s.UserId == userId)
                .OrderByDescending(s => s.UpdatedAt)
                .Select(s => new {
                    sessionId = s.Id,
                    firstMessage = db.Messages.Where(m => m.ChatSessionId == s.Id && !m.IsAi).OrderBy(m => m.Timestamp).Select(m => m.Content).FirstOrDefault(),
                    stepCount = db.Messages.Where(m => m.ChatSessionId == s.Id).SelectMany(m => db.AgentSteps.Where(as2 => as2.MessageId == m.Id)).Count(),
                    createdAt = s.CreatedAt
                })
                .Where(s => s.stepCount > 0)
                .ToListAsync();

            return Results.Ok(sessions);
        });

        app.MapGet("/api/pipeline-logs/session/{sessionId}", async (int sessionId, AppDbContext db, ClaimsPrincipal user) =>
        {
            var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return Results.Unauthorized();
            var userId = int.Parse(userIdStr);

            var session = await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId);
            if (session == null) return Results.NotFound();

            var steps = await db.AgentSteps
                .Where(as1 => db.Messages.Any(m => m.ChatSessionId == sessionId && m.Id == as1.MessageId))
                .OrderBy(as1 => as1.CreatedAt)
                .ToListAsync();

            return Results.Ok(new {
                createdAt = session.CreatedAt,
                steps = steps.Select(s => new {
                    role = s.Role,
                    output = s.Output,
                    durationMs = s.DurationMs,
                    createdAt = s.CreatedAt,
                    wasAccepted = s.WasAccepted
                })
            });
        }).RequireAuthorization();
    }
}

