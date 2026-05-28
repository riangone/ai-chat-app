using System.Security.Claims;
using System.Text.Json;
using AiChatApp.Data;
using AiChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace AiChatApp.Endpoints;

public static class CrewEndpoints
{
    public static void MapCrewEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/crews").RequireAuthorization();

        // List all Crews for the current user
        group.MapGet("/", async (AppDbContext db, ClaimsPrincipal user) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var crews = await db.Crews.Where(c => c.UserId == userId).OrderByDescending(c => c.UpdatedAt).ToListAsync();
            return Results.Ok(crews);
        });

        // Get a single Crew
        group.MapGet("/{id:int}", async (int id, AppDbContext db, ClaimsPrincipal user) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var crew = await db.Crews.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
            return crew is null ? Results.NotFound() : Results.Ok(crew);
        });

        // Create a Crew
        group.MapPost("/", async (HttpContext context, AppDbContext db, ClaimsPrincipal user) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var form = await context.Request.ReadFormAsync();
            var name = form["name"].ToString();
            if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest("name is required");

            var crew = new Crew
            {
                Name = name,
                Description = form["description"].ToString(),
                Process = form["process"].ToString() is "sequential" ? "sequential" : "hierarchical",
                AgentIds = form["agentIds"].ToString() is { Length: > 0 } a ? a : "[]",
                TaskTemplates = form["taskTemplates"].ToString() is { Length: > 0 } t ? t : "[]",
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Crews.Add(crew);
            await db.SaveChangesAsync();
            return Results.Ok(crew);
        }).DisableAntiforgery();

        // Update a Crew
        group.MapPut("/{id:int}", async (int id, HttpContext context, AppDbContext db, ClaimsPrincipal user) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var crew = await db.Crews.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
            if (crew is null) return Results.NotFound();

            var form = await context.Request.ReadFormAsync();
            if (form["name"].ToString() is { Length: > 0 } n) crew.Name = n;
            if (form["description"].ToString() is { } d) crew.Description = d;
            var p = form["process"].ToString();
            if (p is "sequential" or "hierarchical") crew.Process = p;
            if (form["agentIds"].ToString() is { Length: > 0 } a) crew.AgentIds = a;
            if (form["taskTemplates"].ToString() is { } tt) crew.TaskTemplates = tt;
            crew.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(crew);
        }).DisableAntiforgery();

        // Delete a Crew
        group.MapDelete("/{id:int}", async (int id, AppDbContext db, ClaimsPrincipal user) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var crew = await db.Crews.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
            if (crew is null) return Results.NotFound();
            db.Crews.Remove(crew);
            await db.SaveChangesAsync();
            return Results.Ok();
        });
    }
}
