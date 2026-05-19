using Microsoft.AspNetCore.Mvc;
using AiChatApp.Data;
using AiChatApp.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AiChatApp.Endpoints;

public static class InboxEndpoints
{
    public static void MapInboxEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/inbox").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdStr is null) return Results.Unauthorized();
            var userId = int.Parse(userIdStr);

            var items = await db.ProactiveSuggestions
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => !s.IsRead)
                .ThenByDescending(s => s.Timestamp)
                .Take(50)
                .ToListAsync();

            return Results.Json(items);
        });

        group.MapPost("/{id}/read", async (string id, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdStr is null) return Results.Unauthorized();
            var userId = int.Parse(userIdStr);

            var item = await db.ProactiveSuggestions
                .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
            if (item is null) return Results.NotFound();

            item.IsRead = true;
            await db.SaveChangesAsync();

            return Results.Ok();
        });

        group.MapPost("/read-all", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdStr is null) return Results.Unauthorized();
            var userId = int.Parse(userIdStr);

            await db.ProactiveSuggestions
                .Where(s => s.UserId == userId && !s.IsRead)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsRead, true));

            return Results.Ok();
        });

        group.MapDelete("/{id}", async (string id, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdStr is null) return Results.Unauthorized();
            var userId = int.Parse(userIdStr);

            var item = await db.ProactiveSuggestions
                .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
            if (item is null) return Results.NotFound();

            db.ProactiveSuggestions.Remove(item);
            await db.SaveChangesAsync();

            return Results.Ok();
        });

        group.MapGet("/unread-count", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdStr is null) return Results.Unauthorized();
            var userId = int.Parse(userIdStr);

            var count = await db.ProactiveSuggestions
                .CountAsync(s => s.UserId == userId && !s.IsRead);

            return Results.Json(new { count });
        });
    }
}
