using System.Security.Claims;
using System.Text;
using System.Web;
using AiChatApp.Data;
using AiChatApp.Models;
using AiChatApp.Extensions;
using Microsoft.EntityFrameworkCore;

namespace AiChatApp.Endpoints;

public static class NoteEndpoints
{
    public static void MapNoteEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notes").RequireAuthorization();

        group.MapGet("/", async (AppDbContext db, ClaimsPrincipal user) => {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var notes = await db.Notes.Where(n => n.UserId == userId).OrderByDescending(n => n.UpdatedAt).ToListAsync();
            
            var html = new StringBuilder();
            foreach (var note in notes) {
                html.Append($@"<div class='group flex items-center justify-between p-2 hover:bg-base-200 rounded-lg cursor-pointer transition-all duration-200' 
                                     hx-get='/api/notes/{note.Id}' hx-target='#note-content' hx-push-url='false'>
                    <div class='flex flex-col overflow-hidden'>
                        <span class='text-sm font-medium truncate'>{note.Title}</span>
                        <span class='text-[10px] opacity-50'>{note.UpdatedAt.GetRelativeTime()}</span>
                    </div>
                    <button class='btn btn-ghost btn-xs opacity-0 group-hover:opacity-100 text-error' 
                            hx-delete='/api/notes/{note.Id}' hx-confirm='Are you sure?' hx-target='closest div' hx-swap='outerHTML'>
                        <svg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 24 24' stroke-width='1.5' stroke='currentColor' class='w-4 h-4'><path stroke-linecap='round' stroke-linejoin='round' d='m14.74 9-.346 9m-4.788 0L9.26 9m9.968-3.21c.342.052.682.107 1.022.166m-1.022-.165L18.16 19.673a2.25 2.25 0 0 1-2.244 2.077H8.084a2.25 2.25 0 0 1-2.244-2.077L4.772 5.79m14.456 0a48.108 48.108 0 0 0-3.478-.397m-12 .562c.34-.059.68-.114 1.022-.165m0 0a48.11 48.11 0 0 1 3.478-.397m7.5 0v-.916c0-1.18-.91-2.164-2.09-2.201a51.964 51.964 0 0 0-3.32 0c-1.18.037-2.09 1.022-2.09 2.201v.916m7.5 0a48.667 48.667 0 0 0-7.5 0' /></svg>
                    </button>
                </div>");
            }
            return Results.Content(html.ToString(), "text/html");
        });

        group.MapGet("/{id}", async (int id, AppDbContext db, ClaimsPrincipal user) => {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
            if (note == null) return Results.NotFound();

            var encodedContent = Convert.ToBase64String(Encoding.UTF8.GetBytes(note.Content ?? ""));
            return Results.Content($@"<div id='note-main-container' class='flex flex-col h-full bg-base-100 p-6 rounded-xl shadow-sm border border-base-200'>
                <div class='flex items-center justify-between mb-6 border-b border-base-200 pb-4'>
                    <div class='flex-1'>
                        <h2 id='note-view-title' class='text-2xl font-bold truncate'>{HttpUtility.HtmlEncode(note.Title)}</h2>
                        <input type='text' id='note-title-edit' name='title' value='{HttpUtility.HtmlEncode(note.Title)}' 
                               class='input input-bordered w-full font-bold text-xl hidden' placeholder='Note Title' />
                    </div>
                    <div class='flex gap-2 ml-4'>
                        <div id='note-view-actions' class='flex gap-2'>
                            <button class='btn btn-ghost btn-sm' onclick=""toggleNoteMode('edit')"">
                                <i class='fas fa-edit mr-1'></i> Edit
                            </button>
                        </div>
                        <div id='note-edit-actions' class='flex gap-2 hidden'>
                            <button class='btn btn-ghost btn-sm' onclick=""toggleNoteMode('view')"">
                                <i class='fas fa-eye mr-1'></i> View
                            </button>
                            <button class='btn btn-primary btn-sm' data-note-save='{note.Id}' onclick='saveNote({note.Id})'>
                                <i class='fas fa-save mr-1'></i> Save
                            </button>
                        </div>
                    </div>
                </div>

                <div class='flex-1 overflow-hidden relative'>
                    <div id='note-preview-view' class='absolute inset-0 overflow-y-auto'>
                        <div id='markdown-render-area' class='prose prose-sm max-w-none'>
                        </div>
                    </div>

                    <div id='note-editor-view' class='absolute inset-0 hidden'>
                        <textarea id='note-content-edit' name='content'
                                  class='textarea textarea-bordered w-full h-full font-mono text-base resize-none focus:outline-none bg-base-200/30'
                                  placeholder='Write your markdown here...'>{HttpUtility.HtmlEncode(note.Content)}</textarea>
                    </div>
                </div>
            </div>
            <script>renderNotePreview('{encodedContent}');</script>", "text/html");
        });

        group.MapPost("/", async (HttpRequest request, AppDbContext db, ClaimsPrincipal user) => {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var form = await request.ReadFormAsync();
            var title = form["title"].ToString();

            var note = new Note { Title = string.IsNullOrWhiteSpace(title) ? "Untitled Note" : title, Content = "", UserId = userId };
            db.Notes.Add(note);
            await db.SaveChangesAsync();

            return Results.Content($@"<div id='note-main-container' class='flex flex-col h-full bg-base-100 p-6 rounded-xl shadow-sm border border-base-200'
                hx-on::after-settle=""toggleNoteMode('edit')"">
                <div hx-get='/api/notes' hx-trigger='load' hx-target='#notes-list' hx-swap='innerHTML'></div>
                <div class='flex items-center justify-between mb-6 border-b border-base-200 pb-4'>
                    <div class='flex-1'>
                        <h2 id='note-view-title' class='text-2xl font-bold truncate hidden'>{HttpUtility.HtmlEncode(note.Title)}</h2>
                        <input type='text' id='note-title-edit' name='title' value='{HttpUtility.HtmlEncode(note.Title)}' 
                               class='input input-bordered w-full font-bold text-xl' placeholder='Note Title' />
                    </div>
                    <div class='flex gap-2 ml-4'>
                        <div id='note-view-actions' class='flex gap-2 hidden'>
                            <button class='btn btn-ghost btn-sm' onclick=""toggleNoteMode('edit')"">
                                <i class='fas fa-edit mr-1'></i> Edit
                            </button>
                        </div>
                        <div id='note-edit-actions' class='flex gap-2'>
                            <button class='btn btn-ghost btn-sm' onclick=""toggleNoteMode('view')"">
                                <i class='fas fa-eye mr-1'></i> View
                            </button>
                            <button class='btn btn-primary btn-sm' data-note-save='{note.Id}' onclick='saveNote({note.Id})'>
                                <i class='fas fa-save mr-1'></i> Save
                            </button>
                        </div>
                    </div>
                </div>

                <div class='flex-1 overflow-hidden relative'>
                    <div id='note-preview-view' class='absolute inset-0 overflow-y-auto hidden'>
                        <div id='markdown-render-area' class='prose prose-sm max-w-none'>
                        </div>
                    </div>
                    <div id='note-editor-view' class='absolute inset-0'>
                        <textarea id='note-content-edit' name='content' 
                                  class='textarea textarea-bordered w-full h-full font-mono text-base resize-none focus:outline-none bg-base-200/30' 
                                  placeholder='Write your markdown here...'></textarea>
                    </div>
                </div>
            </div>", "text/html");
        }).DisableAntiforgery();

        group.MapPut("/{id}", async (int id, HttpRequest request, AppDbContext db, ClaimsPrincipal user, HttpResponse response) => {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
            if (note == null) return Results.NotFound();

            var form = await request.ReadFormAsync();
            note.Title = form["title"].ToString();
            note.Content = form["content"].ToString();
            note.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            response.Headers.Append("HX-Trigger", "refreshNoteList");

            return Results.Content(@"
                <div class='alert alert-success py-2 shadow-lg'>
                    <i class='fas fa-check-circle'></i>
                    <span class='text-sm'>Saved!</span>
                </div>
                <script>setTimeout(() => document.getElementById('notes-toast-area').innerHTML = '', 2000);</script>
            ", "text/html");
        }).DisableAntiforgery();

        group.MapDelete("/{id}", async (int id, AppDbContext db, ClaimsPrincipal user) => {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
            if (note == null) return Results.NotFound();

            db.Notes.Remove(note);
            await db.SaveChangesAsync();
            return Results.Ok();
        });
    }
}
