using AiChatApp.Data;
using AiChatApp.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AiChatApp.Endpoints;

public static class VocabEndpoints
{
    public static void MapVocabEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vocab").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var cards = await db.VocabCards
                .Where(v => v.UserId == userId)
                .OrderByDescending(v => v.UpdatedAt)
                .ToListAsync();

            var html = string.Join("", cards.Select(c => $@"
                <div class=""vocab-list-item flex items-center justify-between p-3 bg-base-200/50 rounded-xl border border-base-content/5 hover:border-primary/30 transition-all group"" data-id=""{c.Id}"">
                    <div class=""flex-1 min-w-0"">
                        <div class=""flex items-center gap-2"">
                            <span class=""font-bold text-sm"">{EscapeHtml(c.Word)}</span>
                            <span class=""badge badge-xs {LevelBadgeClass(c.Level)}"">L{c.Level}</span>
                            {(c.Category != null ? $"<span class=\"badge badge-ghost badge-xs text-[10px]\">{EscapeHtml(c.Category)}</span>" : "")}
                        </div>
                        <div class=""text-xs opacity-60 mt-0.5 truncate"">{EscapeHtml(c.Translation)}</div>
                    </div>
                    <div class=""flex items-center gap-1 ml-2 opacity-0 group-hover:opacity-100 transition-opacity"">
                        <button onclick=""editVocabCard({c.Id})"" class=""btn btn-ghost btn-xs px-1.5"" title=""Edit"">
                            <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""1.5"" stroke=""currentColor"" class=""w-3.5 h-3.5""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""m16.862 4.487 1.687-1.688a1.875 1.875 0 1 1 2.652 2.652L10.582 16.07a4.5 4.5 0 0 1-1.897 1.13L6 18l.8-2.685a4.5 4.5 0 0 1 1.13-1.897l8.932-8.931Zm0 0L19.5 7.125M18 14v4.75A2.25 2.25 0 0 1 15.75 21H5.25A2.25 2.25 0 0 1 3 18.75V8.25A2.25 2.25 0 0 1 5.25 6H10""/></svg>
                        </button>
                        <button hx-delete=""/api/vocab/{c.Id}""
                                hx-target=""closest .vocab-list-item""
                                hx-swap=""outerHTML swap:200ms""
                                hx-confirm=""Delete this card?""
                                class=""btn btn-ghost btn-xs px-1.5 hover:text-error"" title=""Delete"">
                            <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""1.5"" stroke=""currentColor"" class=""w-3.5 h-3.5""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""m14.74 9-.346 9m-4.788 0L9.26 9m9.968-3.21c.342.052.682.107 1.022.166m-1.022-.165L18.16 19.673a2.25 2.25 0 0 1-2.244 2.077H8.084a2.25 2.25 0 0 1-2.244-2.077L4.772 5.79m14.456 0a48.108 48.108 0 0 0-3.478-.397m-12 .562c.34-.059.68-.114 1.022-.165m0 0a48.11 48.11 0 0 1 3.478-.397m7.5 0v-.916c0-1.18-.91-2.164-2.09-2.201a51.964 51.964 0 0 0-3.32 0c-1.18.037-2.09 1.022-2.09 2.201v.916m7.5 0a48.667 48.667 0 0 0-7.5 0""/></svg>
                        </button>
                    </div>
                </div>"));
            return Results.Content(html, "text/html");
        });

        group.MapGet("/review", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var now = DateTime.UtcNow;
            var cards = await db.VocabCards
                .Where(v => v.UserId == userId && (v.NextReviewAt == null || v.NextReviewAt <= now))
                .OrderBy(v => v.Level)
                .ThenBy(v => v.NextReviewAt)
                .Select(v => new
                {
                    v.Id,
                    v.Word,
                    v.Translation,
                    v.Reading,
                    v.Example,
                    v.ExampleTranslation,
                    v.Category,
                    v.Level
                })
                .ToListAsync();
            return Results.Json(cards);
        });

        group.MapPost("/", async (ClaimsPrincipal user, AppDbContext db,
            string word, string translation, string? reading, string? example, string? exampleTranslation,
            string? category, string? tags) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var card = new VocabCard
            {
                UserId = userId,
                Word = word,
                Translation = translation,
                Reading = reading,
                Example = example,
                ExampleTranslation = exampleTranslation,
                Category = category,
                Tags = tags,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.VocabCards.Add(card);
            await db.SaveChangesAsync();
            return Results.Ok(new { card.Id });
        }).DisableAntiforgery();

        group.MapPut("/{id}", async (int id, ClaimsPrincipal user, AppDbContext db,
            string word, string translation, string? reading, string? example, string? exampleTranslation,
            string? category, string? tags) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var card = await db.VocabCards.FirstOrDefaultAsync(v => v.Id == id && v.UserId == userId);
            if (card == null) return Results.NotFound();

            card.Word = word;
            card.Translation = translation;
            card.Reading = reading;
            card.Example = example;
            card.ExampleTranslation = exampleTranslation;
            card.Category = category;
            card.Tags = tags;
            card.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok();
        }).DisableAntiforgery();

        group.MapDelete("/{id}", async (int id, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var card = await db.VocabCards.FirstOrDefaultAsync(v => v.Id == id && v.UserId == userId);
            if (card == null) return Results.NotFound();
            db.VocabCards.Remove(card);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        group.MapPost("/{id}/result", async (int id, ClaimsPrincipal user, AppDbContext db, string result) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var card = await db.VocabCards.FirstOrDefaultAsync(v => v.Id == id && v.UserId == userId);
            if (card == null) return Results.NotFound();

            var now = DateTime.UtcNow;
            if (result == "correct")
            {
                card.TimesCorrect++;
                card.Level = Math.Min(card.Level + 1, 5);
                card.NextReviewAt = card.Level switch
                {
                    0 => now,
                    1 => now.AddDays(1),
                    2 => now.AddDays(3),
                    3 => now.AddDays(7),
                    4 => now.AddDays(14),
                    5 => now.AddDays(30),
                    _ => now
                };
            }
            else
            {
                card.TimesWrong++;
                card.Level = 0;
                card.NextReviewAt = now;
            }
            card.UpdatedAt = now;
            await db.SaveChangesAsync();
            return Results.Json(new { card.Level, card.NextReviewAt });
        }).DisableAntiforgery();

        group.MapGet("/stats", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var now = DateTime.UtcNow;
            var cards = await db.VocabCards.Where(v => v.UserId == userId).ToListAsync();

            var total = cards.Count;
            var dueCount = cards.Count(v => v.NextReviewAt == null || v.NextReviewAt <= now);
            var mastered = cards.Count(v => v.Level >= 4);
            var todayLearned = cards.Count(v => v.UpdatedAt.Date == now.Date);
            var avgLevel = total > 0 ? cards.Average(v => v.Level) : 0;
            var mastery = total > 0 ? (int)((double)mastered / total * 100) : 0;

            var html = $@"
                <div class=""grid grid-cols-2 md:grid-cols-4 gap-3 mb-4"">
                    <div class=""bg-base-200/50 rounded-xl p-3 text-center"">
                        <div class=""text-2xl font-black text-primary"">{total}</div>
                        <div class=""text-[10px] uppercase font-bold opacity-50"">Total</div>
                    </div>
                    <div class=""bg-base-200/50 rounded-xl p-3 text-center"">
                        <div class=""text-2xl font-black {(dueCount > 0 ? "text-warning" : "text-success")}"">{dueCount}</div>
                        <div class=""text-[10px] uppercase font-bold opacity-50"">Due</div>
                    </div>
                    <div class=""bg-base-200/50 rounded-xl p-3 text-center"">
                        <div class=""text-2xl font-black text-success"">{mastery}%</div>
                        <div class=""text-[10px] uppercase font-bold opacity-50"">Mastery</div>
                    </div>
                    <div class=""bg-base-200/50 rounded-xl p-3 text-center"">
                        <div class=""text-2xl font-black text-info"">{todayLearned}</div>
                        <div class=""text-[10px] uppercase font-bold opacity-50"">Today</div>
                    </div>
                </div>
                <div class=""w-full bg-base-200 rounded-full h-2 overflow-hidden"">
                    <div class=""h-full bg-gradient-to-r from-warning via-primary to-success rounded-full transition-all duration-500"" style=""width:{mastery}%""></div>
                </div>
                <div class=""text-[10px] opacity-40 text-center mt-1"">Average Level: {avgLevel:F1}</div>";
            return Results.Content(html, "text/html");
        });

        group.MapGet("/{id}", async (int id, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var card = await db.VocabCards.FirstOrDefaultAsync(v => v.Id == id && v.UserId == userId);
            if (card == null) return Results.NotFound();
            return Results.Json(new
            {
                card.Id,
                card.Word,
                card.Translation,
                card.Reading,
                card.Example,
                card.ExampleTranslation,
                card.Category,
                card.Tags,
                card.Level
            });
        });
    }

    private static string EscapeHtml(string? s) =>
        (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string LevelBadgeClass(int level) => level switch
    {
        0 => "badge-ghost",
        1 => "badge-info",
        2 => "badge-primary",
        3 => "badge-warning",
        4 => "badge-success",
        5 => "badge-accent",
        _ => "badge-ghost"
    };
}
