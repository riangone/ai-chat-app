using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AiChatApp.Data;
using AiChatApp.Models;

namespace AiChatApp.Endpoints;

public static class StatsEndpoints
{
    public static void MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/stats").RequireAuthorization();

        group.MapGet("/models", async (AppDbContext db) =>
        {
            var steps = await db.AgentSteps.ToListAsync();

            var stats = steps
                .GroupBy(s => s.Model)
                .Select(g => new
                {
                    Model = g.Key,
                    Count = g.Count(),
                    TotalTokens = g.Sum(s => s.TotalTokens),
                    PromptTokens = g.Sum(s => s.PromptTokens),
                    CompletionTokens = g.Sum(s => s.CompletionTokens),
                    AvgDurationMs = g.Average(s => s.DurationMs),
                    SuccessRate = (double)g.Count(s => s.WasAccepted) / g.Count() * 100,
                    LastUsed = g.Max(s => s.CreatedAt)
                })
                .OrderByDescending(s => s.Count)
                .ToList();

            return Results.Ok(stats);
        });

        group.MapGet("/agents", async (AppDbContext db) =>
        {
            var steps = await db.AgentSteps.ToListAsync();

            var stats = steps
                .GroupBy(s => s.Role)
                .Select(g => new
                {
                    Role = g.Key,
                    Count = g.Count(),
                    AvgDurationMs = g.Average(s => s.DurationMs),
                    Models = g.GroupBy(s => s.Model).Select(mg => new { Model = mg.Key, Count = mg.Count() }).ToList()
                })
                .OrderByDescending(s => s.Count)
                .ToList();

            return Results.Ok(stats);
        });
    }
}
