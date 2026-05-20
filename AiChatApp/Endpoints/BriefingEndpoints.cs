using AiChatApp.Data;
using AiChatApp.Services;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Markdig;

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

        app.MapGet("/api/assistant/briefing/html", async (ClaimsPrincipal user, AppDbContext db, AiService ai, bool? force) =>
        {
            var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdStr is null) return Results.Unauthorized();
            var userId = int.Parse(userIdStr);

            var dbUser = await db.Users.FindAsync(userId);
            if (dbUser == null) return Results.NotFound();

            // Check Cache (30 minutes) - Bypass if force is true
            if (force != true && 
                dbUser.BriefingUpdatedAt.HasValue && 
                dbUser.BriefingUpdatedAt.Value.AddMinutes(30) > DateTime.UtcNow && 
                !string.IsNullOrEmpty(dbUser.LastBriefingContent))
            {
                return Results.Content(dbUser.LastBriefingContent, "text/html");
            }

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
            
            var localTime = DateTime.Now;
            var dateStr = localTime.ToString("dddd, MMMM dd");
            var timeOfDay = localTime.Hour switch
            {
                < 12 => "早上好",
                < 18 => "下午好",
                _ => "晚上好"
            };

            // Call Gemini for a dynamic greeting
            string aiGreeting = "";
            try 
            {
                var prompt = $@"
你是一个智能助理。请为用户的首页生成一个温馨且具有启发性的每日简报。
当前时间: {localTime:yyyy-MM-dd HH:mm} ({timeOfDay})
今日待办: 总计 {total} 个，已完成 {completed} 个。
未读洞察/建议: {unreadInsights} 条。

要求:
1. 包含一个亲切的问候语。
2. 简要总结今日任务进度或提供一条积极的建议。
3. 语言使用中文，语气自然友好。
4. 使用 Markdown 格式（如加粗、列表等）使其易于阅读。
5. 保持简洁，总长度控制在 100 字左右。
";
                var response = await ai.ExecuteCliDirectAsync(prompt, ai.DefaultProvider, "你是 AI 每日简报助手。", outputFormat: "plain");
                aiGreeting = response ?? "";
            }
            catch (Exception)
            {
                aiGreeting = $"**{timeOfDay}！** 准备好开始新的一天了吗？当前有 {total} 个待办事项。";
            }

            var html = $@"
<div class=""flex flex-col items-center justify-center min-h-full p-6 animate-fadeIn"">
    <div class=""max-w-2xl w-full space-y-8"">
        <div class=""card bg-base-100/50 backdrop-blur-sm border border-base-content/5 shadow-2xl"">
            <div class=""card-body items-center text-center space-y-4"">
                <div class=""prose prose-sm max-w-none text-base-content/80"">
                    {Markdown.ToHtml(aiGreeting)}
                </div>
                <p class=""text-[10px] opacity-30 font-bold uppercase tracking-widest"">{dateStr}</p>
            </div>
        </div>

        <div class=""grid grid-cols-1 md:grid-cols-2 gap-6"">
            <!-- Todo Progress Card -->
            <div class=""card bg-base-100 shadow-xl border border-base-content/5"">
                <div class=""card-body"">
                    <h2 class=""card-title text-sm uppercase tracking-widest opacity-60"">今日进度</h2>
                    <div class=""flex items-end justify-between mt-2"">
                        <span class=""text-4xl font-black"">{completed}<span class=""text-xl opacity-30"">/{total}</span></span>
                        <span class=""text-sm font-bold opacity-50"">{progress}% 完成</span>
                    </div>
                    <progress class=""progress progress-primary w-full mt-4"" value=""{progress}"" max=""100""></progress>
                    <div class=""card-actions justify-end mt-4"">
                        <button class=""btn btn-ghost btn-sm text-primary"" onclick=""openSlide('todo')"">查看待办</button>
                    </div>
                </div>
            </div>

            <!-- Inbox Card -->
            <div class=""card bg-base-100 shadow-xl border border-base-content/5"">
                <div class=""card-body"">
                    <h2 class=""card-title text-sm uppercase tracking-widest opacity-60"">智能洞察</h2>
                    <div class=""flex items-center gap-4 mt-2"">
                        <div class=""p-3 bg-indigo-500/10 rounded-2xl"">
                            <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""2"" stroke=""currentColor"" class=""w-8 h-8 text-indigo-500""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""M9 12.75 11.25 15 15 9.75M21 12c0 1.268-.63 2.39-1.593 3.068a3.745 3.745 0 0 1-1.043 3.296 3.745 3.745 0 0 1-3.296 1.043A3.745 3.745 0 0 1 12 21c-1.268 0-2.39-.63-3.068-1.593a3.746 3.746 0 0 1-3.296-1.043 3.745 3.745 0 0 1-1.043-3.296A3.745 3.745 0 0 1 3 12c0-1.268.63-2.39 1.593-3.068a3.745 3.745 0 0 1 1.043-3.296 3.746 3.746 0 0 1 3.296-1.043A3.746 3.746 0 0 1 12 3c1.268 0 2.39.63 3.068 1.593a3.746 3.746 0 0 1 3.296 1.043 3.746 3.746 0 0 1 1.043 3.296A3.745 3.745 0 0 1 21 12Z"" /></svg>
                        </div>
                        <div>
                            <span class=""text-4xl font-black"">{unreadInsights}</span>
                            <p class=""text-xs font-bold uppercase opacity-40"">未读条目</p>
                        </div>
                    </div>
                    <div class=""card-actions justify-end mt-4"">
                        <button class=""btn btn-ghost btn-sm text-indigo-500"" onclick=""openInbox()"">打开收件箱</button>
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
            // Update Cache
            dbUser.LastBriefingContent = html;
            dbUser.BriefingUpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return Results.Content(html, "text/html");
        }).RequireAuthorization();
    }
}
