using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AiChatApp.Data;
using AiChatApp.Models;
using System.Security.Claims;

namespace AiChatApp.Endpoints;

public static class StatsEndpoints
{
    public static void MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/stats").RequireAuthorization();

        group.MapGet("/models", async (AppDbContext db, ClaimsPrincipal user) =>
        {
            var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
            var isAdmin = user.HasClaim("IsAdmin", "true");

            IQueryable<AgentStep> query = db.AgentSteps;

            if (!isAdmin && userIdStr != null)
            {
                var userId = int.Parse(userIdStr);
                var userSessionIds = db.ChatSessions.Where(cs => cs.UserId == userId).Select(cs => cs.Id);
                query = query.Where(s => userSessionIds.Contains(s.Message!.ChatSessionId));
            }

            // Push aggregation to the database instead of loading all rows into C# memory
            var stats = await query
                .GroupBy(s => s.Model)
                .Select(g => new
                {
                    Model = g.Key,
                    Count = g.Count(),
                    TotalTokens = g.Sum(s => s.TotalTokens),
                    PromptTokens = g.Sum(s => s.PromptTokens),
                    CompletionTokens = g.Sum(s => s.CompletionTokens),
                    AvgDurationMs = g.Average(s => s.DurationMs),
                    AcceptedCount = g.Count(s => s.WasAccepted),
                    LastUsed = g.Max(s => s.CreatedAt)
                })
                .OrderByDescending(s => s.Count)
                .ToListAsync();

            // Compute SuccessRate in C# (division not easily expressible in EF GroupBy projection)
            var result = stats.Select(s => new
            {
                s.Model, s.Count, s.TotalTokens, s.PromptTokens, s.CompletionTokens,
                s.AvgDurationMs, s.LastUsed,
                SuccessRate = s.Count > 0 ? (double)s.AcceptedCount / s.Count * 100 : 0
            });

            return Results.Ok(result);
        });

        group.MapGet("/agents", async (AppDbContext db, ClaimsPrincipal user) =>
        {
            var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
            var isAdmin = user.HasClaim("IsAdmin", "true");

            IQueryable<AgentStep> query = db.AgentSteps;

            if (!isAdmin && userIdStr != null)
            {
                var userId = int.Parse(userIdStr);
                var userSessionIds = db.ChatSessions.Where(cs => cs.UserId == userId).Select(cs => cs.Id);
                query = query.Where(s => userSessionIds.Contains(s.Message!.ChatSessionId));
            }

            // Outer aggregation in DB; nested model breakdown in C# (nested GroupBy not translatable)
            var byRole = await query
                .GroupBy(s => s.Role)
                .Select(g => new { Role = g.Key, Count = g.Count(), AvgDurationMs = g.Average(s => s.DurationMs) })
                .OrderByDescending(g => g.Count)
                .ToListAsync();

            var modelBreakdown = await query
                .GroupBy(s => new { s.Role, s.Model })
                .Select(g => new { g.Key.Role, g.Key.Model, Count = g.Count() })
                .ToListAsync();

            var stats = byRole.Select(r => new
            {
                r.Role,
                r.Count,
                r.AvgDurationMs,
                Models = modelBreakdown.Where(m => m.Role == r.Role)
                    .Select(m => new { m.Model, m.Count }).ToList()
            });

            return Results.Ok(stats);
        });
    }
}
