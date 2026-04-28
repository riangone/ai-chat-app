using Microsoft.AspNetCore.Mvc;
using AiChatApp.Data;
using AiChatApp.Models;
using AiChatApp.Services;
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

            return Results.Extensions.HtmlWithTrigger(BuildNoteDetailHtml(note), "refreshNoteList");
        }).DisableAntiforgery();

        // PUT /api/notes/{id} → update note, return 200 OK
        group.MapPut("/{id}", async (int id, [FromForm] string title, [FromForm] string content, AppDbContext db, ClaimsPrincipal user, ProactiveBrainService brain) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
            if (note is null) return Results.NotFound();

            note.Title = string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Trim();
            note.Content = content;
            note.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
            
            brain.ProcessNoteChange(note);
            
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

            return Results.Content("<div class='flex flex-col items-center justify-center h-full opacity-20 text-center animate-in fade-in duration-500'><div class='w-20 h-20 mb-4 rounded-3xl bg-base-content/5 flex items-center justify-center'><svg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 24 24' stroke-width='1' stroke='currentColor' class='w-10 h-10'><path stroke-linecap='round' stroke-linejoin='round' d='M14.74 9l-.346 9m-4.788 0L9.26 9m9.968-3.21c.342.052.682.107 1.022.166m-1.022-.165L18.16 19.673a2.25 2.25 0 0 1-2.244 2.077H8.084a2.25 2.25 0 0 1-2.244-2.077L4.772 5.79m14.456 0a48.108 48.108 0 0 0-3.478-.397m-12 .562c.34-.059.68-.114 1.022-.165m0 0a48.11 48.11 0 0 1 3.478-.397m7.5 0v-.916c0-1.18-.91-2.164-2.09-2.201a51.964 51.964 0 0 0-3.32 0c-1.18.037-2.09 1.022-2.09 2.201v.916m7.5 0a48.667 48.667 0 0 0-7.5 0' /></svg></div><p class='text-sm font-medium'>Note deleted</p></div>", "text/html");
        }).DisableAntiforgery();
    }

    private static string BuildNoteListHtml(List<Note> notes)
    {
        if (!notes.Any())
            return "<div class='text-center py-20 opacity-20'><div class='text-4xl mb-4 text-primary/50'>📔</div><p class='text-xs font-bold uppercase tracking-widest' data-i18n='no-notes-yet'>Empty Library</p></div>";

        var items = notes.Select(n => $"""
            <button hx-get="/api/notes/{n.Id}" 
                    hx-target="#note-content" 
                    onclick="selectNote(this)"
                    class="note-item w-full text-left p-3 rounded-xl transition-all hover:bg-base-content/5 group relative mb-1 border border-transparent flex flex-col gap-0.5">
                <div class="flex items-center gap-2">
                    <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor" class="w-3.5 h-3.5 opacity-40 group-[.active]:opacity-100"><path stroke-linecap="round" stroke-linejoin="round" d="M19.5 14.25v-2.625a3.375 3.375 0 00-3.375-3.375h-1.5A1.125 1.125 0 0113.5 7.125v-1.5a3.375 3.375 0 00-3.375-3.375H8.25m2.25 0H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 00-9-9z" /></svg>
                    <div class="font-bold truncate text-sm flex-1 group-[.active]:text-primary">{WebUtility.HtmlEncode(n.Title)}</div>
                </div>
                <div class="text-[10px] opacity-40 pl-5 flex justify-between items-center">
                    <span>{n.UpdatedAt.ToLocalTime():MM/dd HH:mm}</span>
                </div>
                <div class="absolute right-2 top-1/2 -translate-y-1/2 opacity-0 group-hover:opacity-100 transition-opacity">
                    <div class="btn btn-ghost btn-xs btn-circle text-error hover:bg-error/10" 
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
        
        return $$"""
            <div class="flex flex-col h-full animate-in fade-in slide-in-from-right-4 duration-500">
                <!-- Top Toolbar -->
                <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4 mb-6">
                    <div class="flex-1 min-w-0">
                        <input type="text" id="note-title-edit" value="{{title}}" 
                               class="w-full bg-transparent text-2xl sm:text-3xl font-black border-none outline-none focus:ring-0 p-0 placeholder:opacity-10 text-base-content/90" 
                               placeholder="Untitled Note" />
                        <div class="flex items-center gap-3 mt-1.5 opacity-40 text-[10px] font-bold tracking-widest uppercase">
                            <span class="flex items-center gap-1"><svg xmlns="http://www.w3.org/2000/svg" class="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" /></svg> {{note.UpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm}}</span>
                            <span id="word-count-badge" class="badge badge-ghost badge-xs rounded-md">0 words</span>
                        </div>
                    </div>
                    <div class="flex items-center gap-2">
                        <button onclick="copyNoteContent()" class="btn btn-ghost btn-sm rounded-xl px-3 group" title="Copy Content">
                            <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor" class="w-4 h-4 opacity-50 group-hover:opacity-100 transition-opacity"><path stroke-linecap="round" stroke-linejoin="round" d="M15.75 17.25v3.375c0 .621-.504 1.125-1.125 1.125h-9.75a1.125 1.125 0 0 1-1.125-1.125V7.875c0-.621.504-1.125 1.125-1.125H6.75a9.06 9.06 0 0 1 1.5.124m7.5 10.376h3.375c.621 0 1.125-.504 1.125-1.125V11.25c0-4.46-3.243-8.161-7.5-8.876a9.06 9.06 0 0 0-1.5-.124H9.375c-.621 0-1.125.504-1.125 1.125v3.5m7.5 10.375H9.375a1.125 1.125 0 0 1-1.125-1.125v-9.25m12 6.625v-1.875a3.375 3.375 0 0 0-3.375-3.375h-1.5a1.125 1.125 0 0 1-1.125-1.125v-1.5a3.375 3.375 0 0 0-3.375-3.375H9.75" /></svg>
                        </button>
                        <button onclick="saveNote({{note.Id}})" data-note-save="{{note.Id}}" class="btn btn-primary btn-sm rounded-xl px-6 shadow-xl shadow-primary/20 gap-2 border-none">
                            <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2.5" stroke="currentColor" class="w-4 h-4"><path stroke-linecap="round" stroke-linejoin="round" d="M4.5 12.75l6 6 9-13.5" /></svg>
                            <span class="font-bold">Save</span>
                        </button>
                    </div>
                </div>

                <!-- Main Work Area -->
                <div class="flex-1 flex flex-col min-h-0 bg-base-100/50 rounded-[2rem] border border-base-content/5 shadow-2xl overflow-hidden">
                    <!-- Internal Header/Tabs -->
                    <div class="flex items-center justify-between px-6 py-3 border-b border-base-content/5 bg-base-200/50 backdrop-blur-md">
                        <div class="tabs tabs-boxed bg-base-300/50 p-1 rounded-xl gap-1">
                            <button id="ntab-edit" class="tab tab-sm font-bold rounded-lg transition-all" onclick="toggleNoteLayout('edit')">Edit</button>
                            <button id="ntab-split" class="tab tab-sm font-bold rounded-lg transition-all hidden md:flex" onclick="toggleNoteLayout('split')">Split</button>
                            <button id="ntab-preview" class="tab tab-sm font-bold rounded-lg transition-all" onclick="toggleNoteLayout('preview')">Preview</button>
                        </div>
                        <div class="flex items-center gap-3">
                            <div class="w-2 h-2 rounded-full bg-success shadow-[0_0_8px_rgba(34,197,94,0.5)]"></div>
                            <span class="text-[10px] font-black uppercase tracking-tighter opacity-30">Markdown Mode</span>
                        </div>
                    </div>
                    
                    <div id="note-layout-wrapper" class="flex-1 flex min-h-0 divide-x divide-base-content/5">
                        <!-- Editor -->
                        <div id="note-edit-pane" class="flex-1 relative h-full">
                            <textarea id="note-content-edit" 
                                      oninput="handleNoteInput()"
                                      class="w-full h-full bg-transparent p-8 outline-none resize-none font-mono text-sm leading-relaxed custom-scrollbar placeholder:opacity-5 focus:placeholder:opacity-0 transition-all"
                                      placeholder="Pour your thoughts here...">{{WebUtility.HtmlEncode(content)}}</textarea>
                        </div>
                        
                        <!-- Preview -->
                        <div id="note-preview-pane" class="flex-1 hidden p-8 overflow-y-auto custom-scrollbar prose prose-sm max-w-none 
                                    prose-headings:text-base-content/90 prose-headings:font-black prose-p:text-base-content/70
                                    prose-headings:border-b prose-headings:border-base-content/10 prose-headings:pb-2 
                                    prose-pre:bg-base-300/50 prose-pre:text-base-content prose-pre:rounded-2xl
                                    prose-a:text-primary prose-a:no-underline hover:prose-a:underline
                                    prose-img:rounded-3xl prose-img:shadow-lg
                                    prose-blockquote:border-primary/20 prose-blockquote:bg-primary/5 prose-blockquote:py-1 prose-blockquote:px-4 prose-blockquote:rounded-r-xl">
                            <!-- Rendered content -->
                        </div>
                    </div>
                </div>
            </div>

            <script>
                (function() {
                    const editPane = document.getElementById('note-edit-pane');
                    const previewPane = document.getElementById('note-preview-pane');
                    const wrapper = document.getElementById('note-layout-wrapper');
                    const textarea = document.getElementById('note-content-edit');
                    const wcBadge = document.getElementById('word-count-badge');

                    window.handleNoteInput = function() {
                        const val = textarea.value;
                        const words = val.trim() ? val.trim().split(/\s+/).length : 0;
                        if (wcBadge) wcBadge.textContent = words + ' words';
                        
                        if (!previewPane.classList.contains('hidden')) {
                            renderNotePreview();
                        }
                    };

                    window.toggleNoteLayout = function(mode) {
                        // Reset
                        editPane.classList.add('hidden');
                        previewPane.classList.add('hidden');
                        wrapper.classList.remove('flex-row');
                        document.querySelectorAll('.tabs .tab').forEach(t => t.classList.remove('tab-active', 'bg-primary', 'text-primary-content'));
                        
                        const activeTab = document.getElementById('ntab-' + mode);
                        if (activeTab) activeTab.classList.add('tab-active', 'bg-primary', 'text-primary-content');

                        if (mode === 'edit') {
                            editPane.classList.remove('hidden');
                        } else if (mode === 'preview') {
                            previewPane.classList.remove('hidden');
                            renderNotePreview();
                        } else if (mode === 'split') {
                            editPane.classList.remove('hidden');
                            previewPane.classList.remove('hidden');
                            wrapper.classList.add('flex-row');
                            renderNotePreview();
                        }
                    };

                    // Initial view
                    setTimeout(() => {
                        handleNoteInput();
                        if (window.innerWidth > 1024) toggleNoteLayout('split');
                        else toggleNoteLayout('edit');
                    }, 50);
                })();
            </script>
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
