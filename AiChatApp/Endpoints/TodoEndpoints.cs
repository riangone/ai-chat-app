using Microsoft.AspNetCore.Mvc;
using AiChatApp.Data;
using AiChatApp.Models;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace AiChatApp.Endpoints;

public static class TodoEndpoints
{
    public static void MapTodoEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /todo → serve the SPA page
        app.MapGet("/todo", () => Results.File(
            Path.Combine("wwwroot", "todo", "index.html"), "text/html"));

        var group = app.MapGroup("/api/todos");

        // GET /api/todos → return HTML list fragment
        group.MapGet("/", async (AppDbContext db) =>
        {
            var items = await db.TodoItems
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            var html = BuildListHtml(items);
            return Results.Content(html, "text/html");
        });

        // POST /api/todos → create item, return new item HTML fragment
        group.MapPost("/", async ([FromForm] string title, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(title))
                return Results.BadRequest();

            var item = new TodoItem { Title = title.Trim() };
            db.TodoItems.Add(item);
            await db.SaveChangesAsync();

            return Results.Content(BuildItemHtml(item), "text/html");
        }).DisableAntiforgery();

        // PUT /api/todos/{id}/toggle → toggle IsCompleted, return updated item HTML
        group.MapPut("/{id}/toggle", async (int id, AppDbContext db) =>
        {
            var item = await db.TodoItems.FindAsync(id);
            if (item is null) return Results.NotFound();

            item.IsCompleted = !item.IsCompleted;
            await db.SaveChangesAsync();

            return Results.Content(BuildItemHtml(item), "text/html");
        }).DisableAntiforgery();

        // DELETE /api/todos/{id} → delete item, return empty string (HTMX removes element)
        group.MapDelete("/{id}", async (int id, AppDbContext db) =>
        {
            var item = await db.TodoItems.FindAsync(id);
            if (item is null) return Results.NotFound();

            db.TodoItems.Remove(item);
            await db.SaveChangesAsync();

            return Results.Content("", "text/html");
        }).DisableAntiforgery();
    }

    private static string BuildListHtml(List<TodoItem> items)
    {
        if (!items.Any())
            return "<li class=\"text-center py-8 opacity-40\">No todos yet. Add one above!</li>";

        return string.Join("", items.Select(BuildItemHtml));
    }

    private static string BuildItemHtml(TodoItem item)
    {
        var encodedTitle = WebUtility.HtmlEncode(item.Title);
        var completedClass = item.IsCompleted ? "line-through opacity-50" : "";
        var checkboxChecked = item.IsCompleted ? "checked" : "";

        return $"""
            <li id="todo-{item.Id}" class="flex items-center gap-3 p-3 bg-base-200 rounded-lg">
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
