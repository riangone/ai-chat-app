using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using AiChatApp.Data;
using AiChatApp.Models;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace AiChatApp.Endpoints;

public static class InputHistoryEndpoints
{
    public static void MapInputHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/input-history").RequireAuthorization();

        group.MapGet("/", async (AppDbContext db, ClaimsPrincipal user, ILogger<AppDbContext> logger, [FromQuery] int? page, [FromQuery] int? pageSize) => {
            try 
            {
                var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userIdClaim))
                {
                    return Results.Unauthorized();
                }

                var userId = int.Parse(userIdClaim);
                var p = page ?? 1;
                var ps = pageSize ?? 20;

                var histories = await db.InputHistories
                    .Where(h => h.UserId == userId)
                    .OrderByDescending(h => h.UsedAt)
                    .Skip((p - 1) * ps)
                    .Take(ps + 1)
                    .ToListAsync();
                
                if (!histories.Any() && p == 1)
                {
                    return Results.Content("<li class='px-4 py-3 text-sm opacity-40 text-center'>No history yet</li>", "text/html");
                }

                var hasMore = histories.Count > ps;
                var historiesToReturn = histories.Take(ps).ToList();

                // HTMX用の一覧表示を生成
                var html = string.Join("", historiesToReturn.Select((h, index) => {
                    var isLast = index == historiesToReturn.Count - 1 && hasMore;
                    var scrollAttr = isLast ? $"hx-get='/api/input-history?page={p + 1}&pageSize={ps}' hx-trigger='revealed' hx-swap='afterend'" : "";

                    // JavaScriptで安全に扱えるようにエスケープ。改行、バックスラッシュ、引用符を処理。
                    var escaped = h.Content
                        .Replace("\\", "\\\\")
                        .Replace("'", "\\'")
                        .Replace("\"", "\\\"")
                        .Replace("\n", "\\n")
                        .Replace("\r", "\\r");
                    
                    var displayContent = WebUtility.HtmlEncode(h.Content);
                    if (displayContent.Length > 100) displayContent = displayContent.Substring(0, 97) + "...";

                    return $"<li {scrollAttr}><button type='button' class='btn btn-ghost btn-sm w-full text-left justify-start overflow-hidden whitespace-nowrap' onclick=\"const input=document.getElementById('chat-input'); input.value='{escaped}'; input.dispatchEvent(new Event('input')); document.getElementById('history-modal').close();\">{displayContent}</button></li>";
                }));
                
                return Results.Content(html, "text/html");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching input history");
                return Results.Content($"<li class='px-4 py-3 text-sm text-error text-center'>Error loading history</li>", "text/html");
            }
        });

        group.MapPost("/", async ([FromForm] string content, AppDbContext db, ClaimsPrincipal user, ILogger<AppDbContext> logger) => {
            if (string.IsNullOrWhiteSpace(content)) return Results.BadRequest();
            
            try
            {
                var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userIdClaim)) return Results.Unauthorized();

                var userId = int.Parse(userIdClaim);
                var existing = await db.InputHistories.FirstOrDefaultAsync(h => h.UserId == userId && h.Content == content);
                
                if (existing != null)
                {
                    existing.UsedAt = DateTime.UtcNow;
                }
                else
                {
                    db.InputHistories.Add(new InputHistory { UserId = userId, Content = content, UsedAt = DateTime.UtcNow });
                }
                
                await db.SaveChangesAsync();
                return Results.Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error saving input history");
                return Results.Problem("Error saving history");
            }
        }).DisableAntiforgery();
    }
}
