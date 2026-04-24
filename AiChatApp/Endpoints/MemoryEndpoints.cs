using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using AiChatApp.Data;
using AiChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace AiChatApp.Endpoints;

public static class MemoryEndpoints
{
    public static void MapMemoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/memories").RequireAuthorization();

        group.MapGet("/", async (AppDbContext db, ClaimsPrincipal user) => {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var memories = await db.LongTermMemories.Where(m => m.UserId == userId).OrderByDescending(m => m.CreatedAt).ToListAsync();
            return Results.Content(string.Concat(memories.Select(m => $@"
                <div class='flex flex-col gap-1 p-3 bg-base-200 rounded-lg group'>
                    <div class='flex justify-between items-start'>
                        <span class='text-xs font-bold text-primary'>{m.Tags}</span>
                        <div class='flex items-center gap-2'>
                            <div class='w-12 bg-base-300 rounded-full h-1'>
                                <div class='bg-primary h-1 rounded-full' style='width:{m.RelevanceScore}%'></div>
                            </div>
                            <button hx-delete='/api/memories/{m.Id}' hx-target='closest div' hx-swap='outerHTML' 
                                    class='btn btn-ghost btn-xs text-error opacity-0 group-hover:opacity-100'>Delete</button>
                        </div>
                    </div>
                    <p class='text-sm'>{m.Content}</p>
                    <p class='text-xs opacity-30'>参照 {m.AccessCount}回 · {m.CreatedAt:yyyy/MM/dd}</p>
                </div>")), "text/html");
        });

        group.MapPost("/", async (HttpContext context, AppDbContext db, ClaimsPrincipal user) => {
            var form = await context.Request.ReadFormAsync();
            var content = form["content"].ToString();
            var tags = form["tags"].ToString();
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            
            var memory = new LongTermMemory { UserId = userId, Content = content, Tags = tags, RelevanceScore = 100 };
            db.LongTermMemories.Add(memory);
            await db.SaveChangesAsync();
            return Results.Ok();
        }).DisableAntiforgery();

        group.MapDelete("/{id}", async (int id, AppDbContext db, ClaimsPrincipal user) => {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var memory = await db.LongTermMemories.FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
            if (memory != null) {
                db.LongTermMemories.Remove(memory);
                await db.SaveChangesAsync();
            }
            return Results.Ok();
        });
    }
}
