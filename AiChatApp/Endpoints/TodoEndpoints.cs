using Microsoft.AspNetCore.Mvc;
using AiChatApp.Data;
using AiChatApp.Models;
using AiChatApp.Services;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Security.Claims;

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
            var item = new TodoItem 
            { 
                Title = title.Trim(),
                UserId = userId,
                CreatedAt = DateTime.UtcNow
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
              <span class="flex-1 {completedClass}">{encodedTitle}</span>
              <button class="btn btn-ghost btn-xs text-error"
                hx-delete="/api/todos/{item.Id}"
                hx-target="#todo-{item.Id}"
                hx-swap="outerHTML swap:0.3s"
                hx-confirm="Delete this todo?">
                ✕
              </button>
            </li>
            """;
    }
}
