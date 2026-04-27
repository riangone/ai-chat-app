using System.Security.Claims;
using AiChatApp.Data;
using AiChatApp.Models;
using AiChatApp.Services;
using Microsoft.EntityFrameworkCore;

namespace AiChatApp.Endpoints;

public static class MemoryEndpoints
{
    public static void MapMemoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/memories").RequireAuthorization();

        group.MapGet("/", async (AppDbContext db, ClaimsPrincipal user, MemoryFileService fileService) => {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var dbMemories = await db.LongTermMemories
                .Where(m => m.UserId == userId)
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();

            var fileOnlyMemories = fileService.GetFileOnlyMemories(dbMemories.Select(m => m.Id));

            if (!dbMemories.Any() && !fileOnlyMemories.Any())
            {
                return Results.Content(@"
                    <div class='flex flex-col items-center justify-center py-20 opacity-40'>
                        <svg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 24 24' stroke-width='1.5' stroke='currentColor' class='w-16 h-16 mb-4'>
                          <path stroke-linecap='round' stroke-linejoin='round' d='M12 6.042A8.967 8.967 0 006 3.75c-1.052 0-2.062.18-3 .512v14.25A8.987 8.987 0 016 18c2.305 0 4.408.867 6 2.292m0-14.25a8.966 8.966 0 016-2.292c1.052 0 2.062.18 3 .512v14.25A8.987 8.987 0 0018 18a8.967 8.967 0 00-6 2.292m0-14.25v14.25' />
                        </svg>
                        <p class='text-lg font-medium'>No memories yet</p>
                        <p class='text-sm'>The AI will automatically remember important facts from your conversations.</p>
                    </div>", "text/html");
            }

            var dbHtml = string.Concat(dbMemories.Select(m => RenderMemoryCard(m, isFile: false)));
            var fileHtml = string.Concat(fileOnlyMemories.Select(m => RenderMemoryCard(m, isFile: true)));

            return Results.Content(dbHtml + fileHtml, "text/html");
        });

        group.MapPost("/", async (HttpContext context, AppDbContext db, ClaimsPrincipal user, MemoryFileService fileService) => {
            var form = await context.Request.ReadFormAsync();
            var content = form["content"].ToString();
            var tags = form["tags"].ToString();
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var memory = new LongTermMemory { UserId = userId, Content = content, Tags = tags, RelevanceScore = 100 };
            db.LongTermMemories.Add(memory);
            await db.SaveChangesAsync();
            await fileService.WriteAsync(memory);
            return Results.Ok();
        }).DisableAntiforgery();

        group.MapDelete("/{id:int}", async (int id, AppDbContext db, ClaimsPrincipal user, MemoryFileService fileService) => {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var memory = await db.LongTermMemories.FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
            if (memory != null)
            {
                db.LongTermMemories.Remove(memory);
                await db.SaveChangesAsync();
                fileService.Delete(id);
            }
            return Results.Ok();
        });

        // ファイル専用記憶の削除
        group.MapDelete("/file/{fileName}", (string fileName, MemoryFileService fileService) => {
            // パストラバーサル防止
            if (fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains(".."))
                return Results.BadRequest();
            fileService.DeleteFile(fileName);
            return Results.Ok();
        });
    }

    private static string RenderMemoryCard(LongTermMemory m, bool isFile)
    {
        // ファイル記憶はTags末尾の |file:xxx を取り出す
        var tags = m.Tags ?? "";
        var deleteUrl = $"/api/memories/{m.Id}";

        if (isFile)
        {
            var sep = tags.LastIndexOf("|file:", StringComparison.Ordinal);
            var fileName = sep >= 0 ? tags[(sep + 6)..] : "";
            tags = sep >= 0 ? tags[..sep] : tags;
            deleteUrl = $"/api/memories/file/{Uri.EscapeDataString(fileName)}";
        }

        var tagBadges = string.Join(" ", tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => $"<div class='badge badge-outline badge-sm opacity-60'>{System.Net.WebUtility.HtmlEncode(t.Trim())}</div>"));

        if (isFile)
            tagBadges += "<div class='badge badge-sm badge-info opacity-70'>claude</div>";

        var content = System.Net.WebUtility.HtmlEncode(m.Content);

        return $@"
        <div class='card bg-base-200 shadow-sm border border-base-300 group hover:border-primary/30 transition-all duration-200'>
            <div class='card-body p-4'>
                <div class='flex justify-between items-start mb-2'>
                    <div class='flex flex-wrap gap-1'>
                        {tagBadges}
                    </div>
                    <div class='flex items-center gap-3'>
                        <div class='tooltip' data-tip='Relevance Score: {m.RelevanceScore}%'>
                            <div class='w-16 bg-base-300 rounded-full h-1.5 overflow-hidden'>
                                <div class='bg-primary h-full rounded-full transition-all' style='width:{m.RelevanceScore}%'></div>
                            </div>
                        </div>
                        <button hx-delete='{deleteUrl}' hx-target='closest .card' hx-swap='outerHTML swap:0.3s'
                                class='btn btn-ghost btn-xs btn-circle text-error opacity-0 group-hover:opacity-100 transition-opacity'>
                            <svg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 24 24' stroke-width='2' stroke='currentColor' class='w-4 h-4'><path stroke-linecap='round' stroke-linejoin='round' d='M6 18L18 6M6 6l12 12' /></svg>
                        </button>
                    </div>
                </div>
                <p class='text-sm leading-relaxed mb-3'>{content}</p>
                <div class='flex items-center justify-between mt-2 pt-2 border-t border-base-300/50'>
                    <span class='text-[10px] uppercase tracking-wider opacity-40 font-bold'>
                        {(isFile ? "<i class='opacity-50'>Claude Code</i>" : $"<i class='opacity-50'>Ref:</i> {m.AccessCount}")} &nbsp; | &nbsp; {m.CreatedAt:yyyy-MM-dd HH:mm}
                    </span>
                </div>
            </div>
        </div>";
    }
}
