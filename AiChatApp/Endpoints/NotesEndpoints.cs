using Microsoft.AspNetCore.Mvc;
using AiChatApp.Data;
using AiChatApp.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Net;

namespace AiChatApp.Endpoints;

public static class NotesEndpoints
{
    public static void MapNotesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notes").RequireAuthorization();

        // GET /api/notes → return HTML list fragment
        group.MapGet("/", async (AppDbContext db, ClaimsPrincipal user) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var notes = await db.Notes
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.UpdatedAt)
                .ToListAsync();

            var html = BuildNoteListHtml(notes);
            return Results.Content(html, "text/html");
        });

        // GET /api/notes/{id} → return HTML detail/edit fragment
        group.MapGet("/{id}", async (int id, AppDbContext db, ClaimsPrincipal user) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
            if (note is null) return Results.NotFound();

            return Results.Content(BuildNoteDetailHtml(note), "text/html");
        });

        // POST /api/notes → create new note, return the detail fragment
        group.MapPost("/", async ([FromForm] string? title, AppDbContext db, ClaimsPrincipal user) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var note = new Note
            {
                Title = string.IsNullOrWhiteSpace(title) ? "New Note" : title.Trim(),
                Content = "",
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Notes.Add(note);
            await db.SaveChangesAsync();

            // Return the detail view for the newly created note
            // And trigger a refresh of the list
            return Results.Extensions.HtmlWithTrigger(BuildNoteDetailHtml(note), "refreshNoteList");
        }).DisableAntiforgery();

        // PUT /api/notes/{id} → update note, return 200 OK
        group.MapPut("/{id}", async (int id, [FromForm] string title, [FromForm] string content, AppDbContext db, ClaimsPrincipal user) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
            if (note is null) return Results.NotFound();

            note.Title = string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Trim();
            note.Content = content;
            note.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
            return Results.Ok();
        }).DisableAntiforgery();

        // DELETE /api/notes/{id} → delete note
        group.MapDelete("/{id}", async (int id, AppDbContext db, ClaimsPrincipal user) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
            if (note is null) return Results.NotFound();

            db.Notes.Remove(note);
            await db.SaveChangesAsync();

            return Results.Content("<div class='flex items-center justify-center h-full opacity-30'><p>Note deleted</p></div>", "text/html");
        }).DisableAntiforgery();
    }

    private static string BuildNoteListHtml(List<Note> notes)
    {
        if (!notes.Any())
            return "<div class='text-center py-10 opacity-40 text-sm'>No notes yet</div>";

        var items = notes.Select(n => $"""
            <button hx-get="/api/notes/{n.Id}" 
                    hx-target="#note-content" 
                    hx-on:click="document.querySelectorAll('.note-item').forEach(el=>el.classList.remove('bg-base-content/10', 'border-primary/30')); this.classList.add('bg-base-content/10', 'border-primary/30')"
                    class="note-item w-full text-left p-3 rounded-xl transition-all hover:bg-base-content/5 group relative mb-1 border border-transparent">
                <div class="font-medium truncate pr-6 text-sm">{WebUtility.HtmlEncode(n.Title)}</div>
                <div class="text-[10px] opacity-40 mt-1 flex justify-between items-center">
                    <span>{n.UpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm}</span>
                    <span class="opacity-0 group-hover:opacity-100 transition-opacity">
                        <span class="badge badge-ghost badge-xs">{(n.Content?.Length ?? 0)} chars</span>
                    </span>
                </div>
                <div class="absolute right-2 top-3 opacity-0 group-hover:opacity-100 transition-opacity">
                    <div class="btn btn-ghost btn-xs btn-circle text-error" 
                         hx-delete="/api/notes/{n.Id}" 
                         hx-target="#note-content"
                         hx-confirm="Delete this note?"
                         onclick="event.stopPropagation()">
                        <svg xmlns="http://www.w3.org/2000/svg" class="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" /></svg>
                    </div>
                </div>
            </button>
            """);

        return string.Join("", items);
    }

    private static string BuildNoteDetailHtml(Note note)
    {
        var title = WebUtility.HtmlEncode(note.Title);
        var content = note.Content ?? "";
        
        return $"""
            <div class="flex flex-col h-full animate-in fade-in slide-in-from-bottom-2 duration-300">
                <div class="flex items-center gap-4 mb-6">
                    <input type="text" id="note-title-edit" value="{title}" 
                           class="flex-1 bg-transparent text-2xl font-bold border-none outline-none focus:ring-0 p-0 placeholder:opacity-20" 
                           placeholder="Note Title" />
                    <div class="flex gap-2">
                        <button onclick="copyNoteContent()" class="btn btn-ghost btn-sm btn-circle" title="Copy to clipboard">
                            <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor" class="w-4 h-4"><path stroke-linecap="round" stroke-linejoin="round" d="M15.75 17.25v3.375c0 .621-.504 1.125-1.125 1.125h-9.75a1.125 1.125 0 0 1-1.125-1.125V7.875c0-.621.504-1.125 1.125-1.125H6.75a9.06 9.06 0 0 1 1.5.124m7.5 10.376h3.375c.621 0 1.125-.504 1.125-1.125V11.25c0-4.46-3.243-8.161-7.5-8.876a9.06 9.06 0 0 0-1.5-.124H9.375c-.621 0-1.125.504-1.125 1.125v3.5m7.5 10.375H9.375a1.125 1.125 0 0 1-1.125-1.125v-9.25m12 6.625v-1.875a3.375 3.375 0 0 0-3.375-3.375h-1.5a1.125 1.125 0 0 1-1.125-1.125v-1.5a3.375 3.375 0 0 0-3.375-3.375H9.75" /></svg>
                        </button>
                        <button onclick="saveNote({note.Id})" data-note-save="{note.Id}" class="btn btn-primary btn-sm rounded-xl px-6 shadow-lg shadow-primary/20">
                            Save
                        </button>
                    </div>
                </div>
                <div class="flex-1 flex flex-col min-h-0 bg-base-200/30 rounded-3xl border border-base-content/5 overflow-hidden">
                    <div class="flex items-center justify-between px-4 py-2 border-b border-base-content/5 bg-base-300/30">
                        <div class="tabs tabs-boxed bg-transparent p-0 gap-1">
                            <button class="tab tab-sm tab-active rounded-lg transition-all" onclick="switchNoteTab(this, 'edit')">Edit</button>
                            <button class="tab tab-sm rounded-lg transition-all" onclick="switchNoteTab(this, 'preview')">Preview</button>
                        </div>
                        <div class="text-[10px] opacity-30 font-mono">Last updated: {note.UpdatedAt.ToLocalTime():HH:mm:ss}</div>
                    </div>
                    
                    <div id="note-edit-pane" class="flex-1 relative">
                        <textarea id="note-content-edit" 
                                  class="w-full h-full bg-transparent p-6 outline-none resize-none font-mono text-sm leading-relaxed custom-scrollbar placeholder:opacity-10"
                                  placeholder="Write your thoughts in Markdown...">{WebUtility.HtmlEncode(content)}</textarea>
                    </div>
                    
                    <div id="note-preview-pane" class="flex-1 hidden p-8 overflow-y-auto custom-scrollbar prose prose-sm max-w-none prose-headings:border-b prose-headings:pb-2 prose-pre:bg-base-300 prose-pre:text-base-content">
                        <!-- Markdown preview will be rendered here -->
                    </div>
                </div>
            </div>
            """;
    }
}

public static class ResultsExtensions
{
    public static IResult HtmlWithTrigger(this IResultExtensions extensions, string html, string triggerName)
    {
        return new HtmlWithTriggerResult(html, triggerName);
    }
}

public class HtmlWithTriggerResult : IResult
{
    private readonly string _html;
    private readonly string _triggerName;

    public HtmlWithTriggerResult(string html, string triggerName)
    {
        _html = html;
        _triggerName = triggerName;
    }

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.Headers.Add("HX-Trigger", _triggerName);
        httpContext.Response.ContentType = "text/html";
        await httpContext.Response.WriteAsync(_html);
    }
}
