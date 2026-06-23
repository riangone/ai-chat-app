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

        app.MapGet("/api/assistant/briefing/html", async (ClaimsPrincipal user, AppDbContext db, AiService ai, NewsCacheService newsCache, bool? force) =>
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

            var financeAssets = await db.FinancialAssets
                .Where(a => a.UserId == userId)
                .ToListAsync();

            var financeCount = financeAssets.Count;
            var totalMarketValue = financeAssets.Sum(a => a.Quantity * a.CurrentPrice);
            var totalCost = financeAssets.Sum(a => a.Quantity * a.AverageCost);
            var totalProfitLoss = totalMarketValue - totalCost;
            var profitColor = totalProfitLoss >= 0 ? "text-success" : "text-error";
            var profitPrefix = totalProfitLoss >= 0 ? "+" : "";

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

            // Call Antigravity for a dynamic greeting
            string aiGreeting = "";
            try 
            {
                var prompt = $@"
你是一个智能助理。请为用户的首页生成一个温馨且具有启发性的每日简报。
当前时间: {localTime:yyyy-MM-dd HH:mm} ({timeOfDay})
今日待办: 总计 {total} 个，已完成 {completed} 个。
未读洞察/建议: {unreadInsights} 条。
资产概况: 持仓 {financeCount} 个资产，当前总市值 {totalMarketValue:N2}，总盈亏 {totalProfitLoss:N2}。

要求:
1. 包含一个亲切的问候语。
2. 简要总结今日任务进度，并对投资组合表现做一个简短点评（如表现不错或需要关注风险）。
3. 语言使用中文，语气自然友好。
4. 使用 Markdown 格式（如加粗、列表等）使其易于阅读。
5. 保持简洁，总长度控制在 120 字左右。
";
                var response = await ai.ExecuteCliDirectAsync(prompt, ai.DefaultProvider, "你是 AI 每日简报助手。", outputFormat: "text");
                aiGreeting = response ?? "";
            }
            catch (Exception)
            {
                aiGreeting = $"**{timeOfDay}！** 准备好开始新的一天了吗？当前有 {total} 个待办事项。";
            }

            // 共有ニュースキャッシュ（毎朝7時自動収集 + force=trueで手動更新）から取得
            var allNews = await newsCache.GetNewsAsync(ai, force == true);
            var newsCardsHtml = BuildNewsCardsHtml(allNews.Take(3).ToList());

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
                            <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""2"" stroke=""currentColor"" class=""w-8 h-8 text-indigo-500""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""M9 12.75 11.25 15 15 9.75M21 12c0 1.268-.63 2.39-1.593 3.068a3.745 3.745 0 0 1-1.043 3.296 3.745 3.745 0 0 1-3.296 1.043A3.745 3.745 0 0 1 12 21c-1.268 0-2.39-.63-3.068-1.593a3.746 3.746 0 0 1-3.296-1.043 3.745 3.745 0 0 1-1.043-3.296a3.745 3.745 0 0 1 3 12c0-1.268.63-2.39 1.593-3.068a3.745 3.745 0 0 1 1.043-3.296 3.746 3.746 0 0 1 3.296-1.043A3.746 3.746 0 0 1 12 3c1.268 0 3.068 1.593 3.068 1.593a3.746 3.746 0 0 1 3.296 1.043 3.746 3.746 0 0 1 1.043 3.296A3.745 3.745 0 0 1 21 12Z"" /></svg>
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

            <!-- Finance Card -->
            <div class=""card bg-base-100 shadow-xl border border-base-content/5"">
                <div class=""card-body"">
                    <h2 class=""card-title text-sm uppercase tracking-widest opacity-60"">财经管理</h2>
                    <div class=""flex items-start justify-between mt-2"">
                        <div class=""flex flex-col"">
                            <span class=""text-3xl font-black"">{totalMarketValue:N0}</span>
                            <span class=""text-[10px] font-bold uppercase opacity-40"">当前总市值</span>
                        </div>
                        <div class=""text-right flex flex-col"">
                            <span class=""text-lg font-black {profitColor}"">{profitPrefix}{totalProfitLoss:N2}</span>
                            <span class=""text-[10px] font-bold uppercase opacity-40"">累计盈亏</span>
                        </div>
                    </div>
                    <div class=""card-actions justify-end mt-4"">
                        <button class=""btn btn-ghost btn-sm text-emerald-500"" onclick=""openSlide('finance')"">管理 {financeCount} 个资产</button>
                    </div>
                </div>
            </div>

            <!-- News Briefing Card -->
            <div class=""card bg-base-100 shadow-xl border border-base-content/5 md:col-span-2 overflow-hidden"">
                <div class=""card-body p-0"">
                    <div class=""px-6 pt-6 flex justify-between items-center"">
                        <h2 class=""card-title text-sm uppercase tracking-widest opacity-60 flex items-center gap-2 cursor-pointer hover:text-primary transition-colors"" onclick=""openSlide('news')"">
                            <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""2"" stroke=""currentColor"" class=""w-4 h-4 text-primary""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""M12 7.5h1.5m-1.5 3h1.5m-7.5 3h7.5m-7.5 3h7.5m3-9h3.375c.621 0 1.125.504 1.125 1.125V18a2.25 2.25 0 0 1-2.25 2.25H5.25A2.25 2.25 0 0 1 3 18V6c0-1.1.9-2 2-2h9v3.75c0 .621.504 1.125 1.125 1.125H18M3 18V6c0-1.1.9-2 2-2h9"" /></svg>
                            今日热点新闻
                            <span class=""text-[10px] font-bold text-primary normal-case ml-1 opacity-70 hover:opacity-100"">查看全部 ➔</span>
                        </h2>
                        <div class=""flex gap-1 items-center"">
                            <button onclick=""document.getElementById('news-carousel').scrollBy({{left: -240, behavior: 'smooth'}}); event.stopPropagation();"" class=""btn btn-circle btn-xs btn-ghost hover:bg-base-300"">❮</button>
                            <button onclick=""document.getElementById('news-carousel').scrollBy({{left: 240, behavior: 'smooth'}}); event.stopPropagation();"" class=""btn btn-circle btn-xs btn-ghost hover:bg-base-300"">❯</button>
                        </div>
                    </div>
                    
                    <!-- Swipeable Carousel (news/list-htmlでの手動更新と同期するため、最新キャッシュを再取得) -->
                    <div id=""news-carousel"" class=""carousel carousel-center w-full p-6 space-x-4 bg-transparent overflow-x-auto scroll-smooth snap-x snap-mandatory no-scrollbar no-swiping"" style=""scrollbar-width: none; -ms-overflow-style: none;""
                         hx-get=""/api/assistant/briefing/news-cards-html"" hx-trigger=""load, newsUpdated from:body"" hx-swap=""innerHTML"">
                        {newsCardsHtml}
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

        // ホーム画面のニュースカードのみを再取得する（ニュースページでの手動更新後の同期に使用）
        app.MapGet("/api/assistant/briefing/news-cards-html", async (ClaimsPrincipal user, AiService ai, NewsCacheService newsCache, bool? force) =>
        {
            var allNews = await newsCache.GetNewsAsync(ai, force == true);
            return Results.Content(BuildNewsCardsHtml(allNews.Take(3).ToList()), "text/html");
        }).RequireAuthorization();

        app.MapGet("/api/assistant/news/list-html", async (ClaimsPrincipal user, AppDbContext db, AiService ai, NewsCacheService newsCache, bool? force) =>
        {
            var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdStr is null) return Results.Unauthorized();

            // 共有ニュースキャッシュ（毎朝7時自動収集 + force=trueで手動更新）から取得
            var newsList = await newsCache.GetNewsAsync(ai, force == true);

            var slidesHtml = string.Join("\n", newsList.Select((item, idx) => $@"
            <div class=""swiper-slide flex items-center justify-center p-2 md:p-4"">
                <div class=""card w-full max-w-xl bg-base-100/95 border border-base-content/10 shadow-2xl rounded-3xl overflow-hidden hover:border-primary/20 hover:shadow-primary/5 transition-all duration-300 h-[380px] md:h-[400px] flex flex-col"">
                    <div class=""p-6 md:p-8 flex flex-col justify-between flex-1"">
                        <div class=""space-y-4"">
                            <div class=""flex justify-between items-center"">
                                <div class=""flex items-center gap-1"">
                                    <span class=""badge badge-primary badge-md font-bold uppercase tracking-wider text-[10px]"">{item.Category}</span>
                                    {(string.IsNullOrWhiteSpace(item.Country) ? "" : $@"<span class=""badge badge-outline badge-md font-bold uppercase tracking-wider text-[10px]"">{item.Country}</span>")}
                                </div>
                                <span class=""text-xs opacity-50 font-bold"">{item.Time}</span>
                            </div>
                            <h2 class=""text-lg md:text-2xl font-black text-base-content leading-snug mt-2"">{item.Title}</h2>
                            <p class=""text-xs md:text-sm text-base-content/70 leading-relaxed font-medium mt-3"">{item.Summary}</p>
                            <a href=""{item.GetSourceUrl()}"" target=""_blank"" rel=""noopener noreferrer"" class=""text-[10px] text-base-content/40 hover:text-primary hover:underline inline-flex items-center gap-1 no-swiping"" onclick=""event.stopPropagation()"">🔗 查看原文</a>
                        </div>
                        <div class=""pt-4 border-t border-base-content/5 flex justify-between items-center no-swiping mt-4"">
                            <span class=""text-[9px] opacity-30 font-bold uppercase tracking-widest"">Slide for more ➔</span>
                            <button class=""btn btn-primary btn-sm rounded-xl gap-2 shadow-lg shadow-primary/20"" onclick=""showNewsDecode('{EscapeJsString(item.Title)}', '{EscapeJsString(item.Category)}', '{EscapeJsString(item.Summary)}')"">
                                <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""2.5"" stroke=""currentColor"" class=""w-4 h-4""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""M9.813 15.904L9 21l8.982-11.795m-8.982 6.195c.983-1.004 2.608-1.004 3.592 0L18 18M18 9L9.813 15.904M18 9l-3.328-5.325a1.125 1.125 0 0 0-1.944 0L9.813 15.904m8.187-6.904c-.983 1.004-2.608 1.004-3.592 0L9 9M9 9l3.328-5.325a1.125 1.125 0 0 1 1.944 0L18 9"" /></svg>
                                AI 深度解读
                            </button>
                        </div>
                    </div>
                </div>
            </div>
            "));

            var swiperHtml = $@"
            <div class=""swiper news-detail-swiper h-full w-full min-h-0 flex-1 relative p-4"">
                <div class=""swiper-wrapper"" flex-1>
                    {slidesHtml}
                </div>
                <div class=""swiper-pagination !bottom-2""></div>
            </div>
            ";

            return Results.Content(swiperHtml, "text/html");
        }).RequireAuthorization();

        app.MapPost("/api/assistant/news/decode", async (NewsDecodeRequest req, AiService ai) =>
        {
            if (string.IsNullOrEmpty(req.Title)) return Results.BadRequest("Title is required.");

            var prompt = $@"
你是一个全球顶尖科技和商业分析师。请对以下新闻进行深度的、多角度的行家级解读和剖析：
新闻标题：{req.Title}
新闻分类：{req.Category}
新闻摘要：{req.Summary}

请用 Markdown 格式输出以下结构化的深度内容（中文，语气务必专业、严谨且前瞻性）：
1. **🚀 行业震撼与核心影响**
   简明阐述这则消息对相关产业链、行业格局或竞品带来的中短期和长期海啸般的影响。
2. **🔮 未来演进与前瞻趋势**
   预测接下来一到两年内，该领域最有可能发生的重要事件或演进方向，有哪些新技术或新商业模式会随之崛起。
3. **💡 决策建议与行动指南**
   提供针对开发者（学习与技术栈方向）、投资者（资金流向与机会评估）以及相关企业的明确且极具实操价值的策略性建议。

要求：
- 结构清晰，排版优美，加粗重点。
- 长度控制在 300 字左右，输出纯 Markdown 内容。
";
            try
            {
                var response = await ai.ExecuteCliDirectAsync(prompt, ai.DefaultProvider, "你是 AI 商业深度剖析专家，请用专业 Markdown 格式输出。", outputFormat: "text");
                return Results.Json(new { content = response ?? "暂无深度解读。" });
            }
            catch (Exception ex)
            {
                return Results.Json(new { content = $"生成解读失败: {ex.Message}" });
            }
        }).RequireAuthorization();
    }

    private static string BuildNewsCardsHtml(List<NewsItem> newsList)
    {
        return string.Join("\n", newsList.Select((item, idx) => $@"
            <div class=""carousel-item snap-center w-[85%] md:w-[48%] shrink-0 flex flex-col justify-between p-5 rounded-2xl bg-base-200/40 border border-base-content/5 relative group hover:border-primary/20 hover:bg-base-200/70 transition-all duration-300 shadow-sm"">
                <div class=""no-swiping"">
                    <div class=""flex justify-between items-center mb-2"">
                        <div class=""flex items-center gap-1"">
                            <span class=""badge badge-primary badge-sm font-semibold text-[10px]"">{item.Category}</span>
                            {(string.IsNullOrWhiteSpace(item.Country) ? "" : $@"<span class=""badge badge-outline badge-sm font-semibold text-[10px]"">{item.Country}</span>")}
                        </div>
                        <span class=""text-[10px] opacity-40 font-medium"">{item.Time}</span>
                    </div>
                    <h3 class=""font-bold text-sm text-base-content group-hover:text-primary transition-colors line-clamp-1"">{item.Title}</h3>
                    <p class=""text-xs text-base-content/70 mt-2 line-clamp-3 leading-relaxed"">{item.Summary}</p>
                </div>
                <div class=""flex justify-between items-center mt-4 pt-2 border-t border-base-content/5 no-swiping"">
                    <a href=""{item.GetSourceUrl()}"" target=""_blank"" rel=""noopener noreferrer"" class=""text-[10px] text-base-content/40 hover:text-primary hover:underline"" onclick=""event.stopPropagation()"">🔗 查看原文</a>
                    <button class=""btn btn-link btn-xs p-0 text-primary no-underline hover:underline hover:text-primary-focus"" onclick=""showNewsDecode('{EscapeJsString(item.Title)}', '{EscapeJsString(item.Category)}', '{EscapeJsString(item.Summary)}')"">AI 解读 ➔</button>
                </div>
            </div>
            "));
    }

    private static string EscapeJsString(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value
            .Replace("\\", "\\\\")
            .Replace("'", "\\'")
            .Replace("\"", "\\\"")
            .Replace("\r", "")
            .Replace("\n", "\\n");
    }
}

public class NewsItem
{
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string Country { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Time { get; set; } = "";
    public string Url { get; set; } = "";

    public NewsItem() { }
    public NewsItem(string title, string category, string summary, string time)
    {
        Title = title;
        Category = category;
        Summary = summary;
        Time = time;
    }

    // AIが実在のURLを返さなかった場合は、見出しで検索できるリンクにフォールバックする
    public string GetSourceUrl()
    {
        if (!string.IsNullOrWhiteSpace(Url) && Uri.TryCreate(Url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return Url;
        }
        return "https://www.google.com/search?q=" + Uri.EscapeDataString(Title);
    }
}

public record NewsDecodeRequest(string Title, string Category, string Summary);
