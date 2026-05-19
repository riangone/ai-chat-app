using AiChatApp.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AiChatApp.Endpoints;

public static class BriefingEndpoints
{
    public static void MapBriefingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/assistant/briefing", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdStr is null) return Results.Unauthorized();
            var userId = int.Parse(userIdStr);

            var todayStart = DateTime.UtcNow.Date;

            var todayTodos = await db.TodoItems
                .Where(t => t.UserId == userId && t.CreatedAt >= todayStart)
                .GroupBy(t => 1)
                .Select(g => new
                {
                    total = g.Count(),
                    completed = g.Count(t => t.IsCompleted)
                })
                .FirstOrDefaultAsync();

            var unreadInsights = await db.ProactiveSuggestions
                .CountAsync(s => s.UserId == userId && !s.IsRead);

            return Results.Json(new
            {
                greeting = $"Today is {DateTime.UtcNow:yyyy-MM-dd}.",
                todayTodos = todayTodos ?? new { total = 0, completed = 0 },
                unreadInsights
            });
        }).RequireAuthorization();

        app.MapGet("/api/assistant/briefing/html", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdStr is null) return Results.Unauthorized();
            var userId = int.Parse(userIdStr);

            var todayStart = DateTime.UtcNow.Date;

            var todayTodos = await db.TodoItems
                .Where(t => t.UserId == userId && t.CreatedAt >= todayStart)
                .GroupBy(t => 1)
                .Select(g => new
                {
                    total = g.Count(),
                    completed = g.Count(t => t.IsCompleted)
                })
                .FirstOrDefaultAsync();

            var unreadInsights = await db.ProactiveSuggestions
                .CountAsync(s => s.UserId == userId && !s.IsRead);

            var total = todayTodos?.total ?? 0;
            var completed = todayTodos?.completed ?? 0;
            var progress = total > 0 ? (int)((double)completed / total * 100) : 0;
            var dateStr = DateTime.UtcNow.ToString("dddd, MMMM dd");

            var html = $@"
<div class=""flex flex-col items-center justify-center min-h-full p-6 animate-fadeIn"">
    <div class=""max-w-2xl w-full space-y-8"">
        <div class=""text-center space-y-2"">
            <h1 class=""text-4xl font-black tracking-tight"">Good morning!</h1>
            <p class=""text-xl opacity-50 font-medium"">{dateStr}</p>
        </div>

        <div class=""grid grid-cols-1 md:grid-cols-2 gap-6"">
            <!-- Todo Progress Card -->
            <div class=""card bg-base-100 shadow-xl border border-base-content/5"">
                <div class=""card-body"">
                    <h2 class=""card-title text-sm uppercase tracking-widest opacity-60"">Today's Progress</h2>
                    <div class=""flex items-end justify-between mt-2"">
                        <span class=""text-4xl font-black"">{completed}<span class=""text-xl opacity-30"">/{total}</span></span>
                        <span class=""text-sm font-bold opacity-50"">{progress}% Done</span>
                    </div>
                    <progress class=""progress progress-primary w-full mt-4"" value=""{progress}"" max=""100""></progress>
                    <div class=""card-actions justify-end mt-4"">
                        <button class=""btn btn-ghost btn-sm text-primary"" onclick=""switchMainTab(1)"">View Todos</button>
                    </div>
                </div>
            </div>

            <!-- Inbox Card -->
            <div class=""card bg-base-100 shadow-xl border border-base-content/5"">
                <div class=""card-body"">
                    <h2 class=""card-title text-sm uppercase tracking-widest opacity-60"">Proactive Insights</h2>
                    <div class=""flex items-center gap-4 mt-2"">
                        <div class=""p-3 bg-indigo-500/10 rounded-2xl"">
                            <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""2"" stroke=""currentColor"" class=""w-8 h-8 text-indigo-500""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""M9 12.75 11.25 15 15 9.75M21 12c0 1.268-.63 2.39-1.593 3.068a3.745 3.745 0 0 1-1.043 3.296 3.745 3.745 0 0 1-3.296 1.043A3.745 3.745 0 0 1 12 21c-1.268 0-2.39-.63-3.068-1.593a3.746 3.746 0 0 1-3.296-1.043 3.745 3.745 0 0 1-1.043-3.296A3.745 3.745 0 0 1 3 12c0-1.268.63-2.39 1.593-3.068a3.745 3.745 0 0 1 1.043-3.296 3.746 3.746 0 0 1 3.296-1.043A3.746 3.746 0 0 1 12 3c1.268 0 2.39.63 3.068 1.593a3.746 3.746 0 0 1 3.296 1.043 3.746 3.746 0 0 1 1.043 3.296A3.745 3.745 0 0 1 21 12Z"" /></svg>
                        </div>
                        <div>
                            <span class=""text-4xl font-black"">{unreadInsights}</span>
                            <p class=""text-xs font-bold uppercase opacity-40"">Unread Insights</p>
                        </div>
                    </div>
                    <div class=""card-actions justify-end mt-4"">
                        <button class=""btn btn-ghost btn-sm text-indigo-500"" onclick=""openInbox()"">Open Inbox</button>
                    </div>
                </div>
            </div>
        </div>
        
        <div class=""flex justify-center"">
            <div class=""badge badge-ghost opacity-30 text-[10px] font-bold uppercase tracking-[0.2em]"">Powered by Hyperion Intelligence</div>
        </div>
    </div>
</div>
";
            return Results.Content(html, "text/html");
        }).RequireAuthorization();
    }
}
