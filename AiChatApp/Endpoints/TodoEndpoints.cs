using Microsoft.AspNetCore.Mvc;
using AiChatApp.Data;
using AiChatApp.Models;
using AiChatApp.Services;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace AiChatApp.Endpoints;

public static class TodoEndpoints
{
    public static void MapTodoEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /todo → serve the SPA page
        app.MapGet("/todo", (ClaimsPrincipal user) => 
        {
            if (user.Identity?.IsAuthenticated != true) return Results.Redirect("/login");
            return Results.File("todo/index.html", "text/html");
        });

        var group = app.MapGroup("/api/todos").RequireAuthorization();

        // GET /api/todos → return HTML list fragment
        group.MapGet("/", async (ClaimsPrincipal user, AppDbContext db, [FromQuery] int? page, [FromQuery] int? pageSize) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var p = page ?? 1;
            var ps = pageSize ?? 20;

            var items = await db.TodoItems
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.CreatedAt)
                .Skip((p - 1) * ps)
                .Take(ps + 1)
                .ToListAsync();

            var hasMore = items.Count > ps;
            var itemsToReturn = items.Take(ps).ToList();

            var html = BuildListHtml(itemsToReturn, p, ps, hasMore);
            return Results.Content(html, "text/html");
        });

        // POST /api/todos → create item, return new item HTML fragment
        group.MapPost("/", async ([FromForm] string title, ClaimsPrincipal user, AppDbContext db, ProactiveBrainService brain) =>
        {
            if (string.IsNullOrWhiteSpace(title))
                return Results.BadRequest();

            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var (cleanTitle, dueDate) = ParseNaturalLanguageDate(title);

            var item = new TodoItem 
            { 
                Title = cleanTitle,
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                DueDate = dueDate
            };
            db.TodoItems.Add(item);
            await db.SaveChangesAsync();

            brain.ProcessTodoChange(item, "created");

            return Results.Content(BuildItemHtml(item), "text/html");
        }).DisableAntiforgery();

        // PUT /api/todos/{id}/toggle → toggle IsCompleted, return updated item HTML
        group.MapPut("/{id}/toggle", async (int id, ClaimsPrincipal user, AppDbContext db, ProactiveBrainService brain) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var item = await db.TodoItems.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
            if (item is null) return Results.NotFound();

            item.IsCompleted = !item.IsCompleted;
            await db.SaveChangesAsync();

            if (item.IsCompleted)
            {
                brain.ProcessTodoChange(item, "completed");
            }

            return Results.Content(BuildItemHtml(item), "text/html");
        }).DisableAntiforgery();

        // DELETE /api/todos/{id} → delete item, return empty string (HTMX removes element)
        group.MapDelete("/{id}", async (int id, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var item = await db.TodoItems.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
            if (item is null) return Results.NotFound();

            db.TodoItems.Remove(item);
            await db.SaveChangesAsync();

            return Results.Content("", "text/html");
        }).DisableAntiforgery();

        // POST /api/todos/{id}/chat → create or load chat session for a todo item
        group.MapPost("/{id}/chat", async (int id, ClaimsPrincipal user, AppDbContext db, AiService ai) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var item = await db.TodoItems.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
            if (item is null) return Results.NotFound();

            // If we already have a ChatSessionId, verify if the session still exists
            if (item.ChatSessionId.HasValue)
            {
                var existingSession = await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == item.ChatSessionId.Value && s.UserId == userId);
                if (existingSession != null)
                {
                    return Results.Ok(new { sessionId = existingSession.Id });
                }
            }

            // Create new ChatSession
            var session = new ChatSession
            {
                UserId = userId,
                Title = $"Todo: {item.Title}",
                PreferredProvider = ai.DefaultProvider,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.ChatSessions.Add(session);
            await db.SaveChangesAsync();

            // Link the session to the todo
            item.ChatSessionId = session.Id;
            await db.SaveChangesAsync();

            // Create initial user message
            var descContext = string.IsNullOrWhiteSpace(item.Description) ? "" : $"\n任务描述：{item.Description}";
            var userMsgContent = $"针对待办任务【{item.Title}】开始作业。{descContext}\n请协助我分析并提供实现或优化方案。";
            var uMsg = new Message
            {
                ChatSessionId = session.Id,
                Content = userMsgContent,
                IsAi = false,
                Timestamp = DateTime.UtcNow
            };
            db.Messages.Add(uMsg);
            await db.SaveChangesAsync();

            // Request AI Response
            string aiResponse;
            try
            {
                aiResponse = await ai.GetResponseAsync(userMsgContent, userId, session.Id, ai.DefaultProvider, null, null, null, false);
            }
            catch (Exception ex)
            {
                aiResponse = ex is TimeoutException
                    ? "[AI 响应超时。已为您创建会话，请在会话中手动发送消息以重试。]"
                    : $"[AI 响应出错：{ex.Message}]";
            }

            var aMsg = new Message
            {
                ChatSessionId = session.Id,
                Content = aiResponse,
                IsAi = true,
                AgentName = ai.DefaultProvider,
                Timestamp = DateTime.UtcNow
            };
            db.Messages.Add(aMsg);
            await db.SaveChangesAsync();

            return Results.Ok(new { sessionId = session.Id });
        }).DisableAntiforgery();
    }

    private static string BuildListHtml(List<TodoItem> items, int page, int pageSize, bool hasMore)
    {
        if (!items.Any() && page == 1)
            return "<li id=\"no-todos-hint\" class=\"text-center py-8 opacity-40\">No todos yet. Add one above!</li>";

        return string.Join("", items.Select((item, index) => {
            var isLast = index == items.Count - 1 && hasMore;
            var scrollAttr = isLast ? $"hx-get='/api/todos?page={page + 1}&pageSize={pageSize}' hx-trigger='revealed' hx-swap='afterend'" : "";
            return BuildItemHtml(item, scrollAttr);
        }));
    }

    private static string BuildItemHtml(TodoItem item, string extraAttrs = "")
    {
        var encodedTitle = WebUtility.HtmlEncode(item.Title);
        var completedClass = item.IsCompleted ? "line-through opacity-50" : "";
        var checkboxChecked = item.IsCompleted ? "checked" : "";

        return $"""
            <li id="todo-{item.Id}" class="flex items-center gap-3 p-3 bg-base-200 rounded-lg" {extraAttrs}>
              <input type="checkbox"
                class="checkbox checkbox-primary"
                {checkboxChecked}
                hx-put="/api/todos/{item.Id}/toggle"
                hx-target="#todo-{item.Id}"
                hx-swap="outerHTML" />
              <span class="flex-1 {completedClass} cursor-pointer hover:text-primary transition-colors duration-200"
                onclick="startChatFromTodo({item.Id})"
                title="与 AI 一起作业">
                {encodedTitle}
              </span>
              <button class="btn btn-ghost btn-xs text-primary"
                onclick="startChatFromTodo({item.Id})"
                title="与 AI 一起作业">
                <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-4 h-4">
                  <path stroke-linecap="round" stroke-linejoin="round" d="M7.5 8.25h9m-9 3H12m-9.75 1.51c0 1.6 1.123 2.994 2.707 3.227 1.129.166 2.27.293 3.423.379.35.026.67.21.865.501L12 21l2.755-4.133a1.14 1.14 0 0 1 .865-.501 48.172 48.172 0 0 0 3.423-.379c1.584-.233 2.707-1.626 2.707-3.228V6.741c0-1.602-1.123-2.995-2.707-3.228A48.394 48.394 0 0 0 12 3c-2.392 0-4.744.175-7.043.513C3.373 3.746 2.25 5.14 2.25 6.741v6.018Z" />
                </svg>
              </button>
              <button class="btn btn-ghost btn-xs text-error"
                hx-delete="/api/todos/{item.Id}"
                hx-target="#todo-{item.Id}"
                hx-swap="outerHTML swap:0.3s"
                hx-confirm="Delete this todo?">
                ✕
              </button>
            """;
    }

    private static (string cleanTitle, DateTime? dueDate) ParseNaturalLanguageDate(string rawTitle)
    {
        string cleanTitle = rawTitle.Trim();
        DateTime? dueDate = null;

        // Convert UTC now to Tokyo standard time (JST) for context
        var nowJst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time"));

        // Match patterns like: "今夜20時", "明日10:30"
        var match = Regex.Match(cleanTitle, @"(今日|今夜|明日|明後日)\s*([0-2]?\d)[時:：]([0-5]?\d)?分?");
        if (match.Success)
        {
            string dayStr = match.Groups[1].Value;
            int hour = int.Parse(match.Groups[2].Value);
            int minute = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;

            DateTime targetDateJst = nowJst.Date;
            if (dayStr == "明日")
            {
                targetDateJst = targetDateJst.AddDays(1);
            }
            else if (dayStr == "明後日")
            {
                targetDateJst = targetDateJst.AddDays(2);
            }

            try
            {
                var dueDateJst = new DateTime(targetDateJst.Year, targetDateJst.Month, targetDateJst.Day, hour, minute, 0, DateTimeKind.Unspecified);
                dueDate = TimeZoneInfo.ConvertTimeToUtc(dueDateJst, TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time"));
                
                var cleanRegex = new Regex(@"(、)?\s*" + Regex.Escape(match.Value) + @"(に?(通知して|リマインドして|教えて|アラーム| reminder)?)*", RegexOptions.IgnoreCase);
                cleanTitle = cleanRegex.Replace(cleanTitle, "").Trim();
                cleanTitle = Regex.Replace(cleanTitle, @"[，,、。！？!?]$", "").Trim();
            }
            catch {}
        }
        else
        {
            // Match time-only pattern: "20時", "15:30"
            var matchTimeOnly = Regex.Match(cleanTitle, @"([0-2]?\d)[時:：]([0-5]?\d)?分?");
            if (matchTimeOnly.Success)
            {
                int hour = int.Parse(matchTimeOnly.Groups[1].Value);
                int minute = matchTimeOnly.Groups[2].Success ? int.Parse(matchTimeOnly.Groups[2].Value) : 0;
                
                DateTime targetDateJst = nowJst.Date;
                var dueDateJst = new DateTime(targetDateJst.Year, targetDateJst.Month, targetDateJst.Day, hour, minute, 0, DateTimeKind.Unspecified);
                if (dueDateJst < nowJst)
                {
                    dueDateJst = dueDateJst.AddDays(1);
                }

                try
                {
                    dueDate = TimeZoneInfo.ConvertTimeToUtc(dueDateJst, TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time"));
                    var cleanRegex = new Regex(@"(、)?\s*" + Regex.Escape(matchTimeOnly.Value) + @"(に?(通知して|リマインドして|教えて|アラーム| reminder)?)*", RegexOptions.IgnoreCase);
                    cleanTitle = cleanRegex.Replace(cleanTitle, "").Trim();
                    cleanTitle = Regex.Replace(cleanTitle, @"[，,、。！？!?]$", "").Trim();
                }
                catch {}
            }
        }

        if (string.IsNullOrWhiteSpace(cleanTitle))
        {
            cleanTitle = rawTitle;
        }

        return (cleanTitle, dueDate);
    }
}
