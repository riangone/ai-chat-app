using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Net;
using AiChatApp.Data;
using AiChatApp.Models;
using AiChatApp.Services;
using AiChatApp.Extensions;
using Microsoft.EntityFrameworkCore;

namespace AiChatApp.Endpoints;

public static class ChatEndpoints
{
    public static void MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").RequireAuthorization();

        // Agents
        group.MapGet("/agents/list", async (AiService ai, ClaimsPrincipal user) => {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var agents = await ai.GetAvailableAgentsAsync(userId);
            return Results.Ok(agents);
        });

        // Chat Sessions
        group.MapPost("/chat/new", () => {
            return Results.Content($@"<div id='chat-box' data-session-id='' class='flex-1 overflow-y-auto p-4 md:p-6 space-y-8'>
                <div class='flex flex-col items-center justify-center h-full text-base-content/30 space-y-4'>
                    <div class='w-16 h-16 border-4 border-dashed border-current rounded-full opacity-20'></div>
                    <p class='text-xl font-medium text-center'>Ready for your questions.</p>
                </div>
            </div>", "text/html");
        }).DisableAntiforgery();

        group.MapGet("/chat/list", async (int? projectId, AppDbContext db, ClaimsPrincipal user) => {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var query = db.ChatSessions.Where(s => s.UserId == userId && db.Messages.Any(m => m.ChatSessionId == s.Id));
            if (projectId.HasValue)
                query = query.Where(s => s.ProjectId == projectId);
            else
                query = query.Where(s => s.ProjectId == null);
                
            var sessions = await query.OrderByDescending(s => s.UpdatedAt).ToListAsync();
            return Results.Content(string.Concat(sessions.Select(s => $@"
                <div class='flex items-center group w-full mb-1 border-b border-base-200 pb-1'>
                    <button onclick='loadChatSession({s.Id})' class='btn btn-ghost btn-sm flex-1 justify-start overflow-hidden text-ellipsis whitespace-nowrap font-normal'>
                        <div class='flex flex-col items-start overflow-hidden'>
                            <span class='w-full text-ellipsis overflow-hidden text-left'>{s.Title}</span>
                            <span class='text-[10px] opacity-40 uppercase'>{s.UpdatedAt.GetRelativeTime()}</span>
                        </div>
                    </button>
                    <div class='flex flex-none gap-0.5 ml-1'>
                        <button onclick='editTitle({s.Id}, ""{s.Title}"")' class='btn btn-ghost btn-xs px-1 text-primary' title='Rename'>
                            <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""1.5"" stroke=""currentColor"" class=""w-4 h-4""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""m16.862 4.487 1.687-1.688a1.875 1.875 0 1 1 2.652 2.652L10.582 16.07a4.5 4.5 0 0 1-1.897 1.13L6 18l.8-2.685a4.5 4.5 0 0 1 1.13-1.897l8.932-8.931Zm0 0L19.5 7.125M18 14v4.75A2.25 2.25 0 0 1 15.75 21H5.25A2.25 2.25 0 0 1 3 18.75V8.25A2.25 2.25 0 0 1 5.25 6H10"" /></svg>
                        </button>
                        <button onclick='deleteChat({s.Id})' class='btn btn-ghost btn-xs px-1 text-error' title='Delete'>
                            <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""1.5"" stroke=""currentColor"" class=""w-4 h-4""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""m14.74 9-.346 9m-4.788 0L9.26 9m9.968-3.21c.342.052.682.107 1.022.166m-1.022-.165L18.16 19.673a2.25 2.25 0 0 1-2.244 2.077H8.084a2.25 2.25 0 0 1-2.244-2.077L4.772 5.79m14.456 0a48.108 48.172 0 0 0-3.478-.397m-12 .562c.34-.059.68-.114 1.022-.165m0 0a48.11 48.11 0 0 1 3.478-.397m7.5 0v-.916c0-1.18-.91-2.164-2.09-2.201a51.964 51.964 0 0 0-3.32 0c-1.18.037-2.09 1.022-2.09 2.201v.916m7.5 0a48.667 48.667 0 0 0-7.5 0"" /></svg>
                        </button>
                    </div>
                </div>")), "text/html");
        });

        group.MapDelete("/chat/{id}", async (int id, AppDbContext db, ClaimsPrincipal user) => {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var session = await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
            if (session != null) {
                db.ChatSessions.Remove(session);
                await db.SaveChangesAsync();
            }
            return Results.Ok();
        });

        group.MapGet("/chat/load/{id}", async (int id, AppDbContext db, ClaimsPrincipal user) => {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var session = await db.ChatSessions
                .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
            if (session == null) return Results.NotFound();

            const int pageSize = 20;
            var messages = await db.Messages
                .Where(m => m.ChatSessionId == id)
                .OrderByDescending(m => m.Timestamp)
                .Take(pageSize)
                .ToListAsync();
            
            messages = messages.OrderBy(m => m.Timestamp).ToList();

            var messageIds = messages.Select(m => m.Id).ToList();
            var allSteps = await db.AgentSteps
                .Where(s => messageIds.Contains(s.MessageId))
                .ToListAsync();

            var messagesHtml = string.Concat(messages.Select(m => {
                var steps = allSteps.Where(s => s.MessageId == m.Id).ToList();
                return RenderMessage(m, steps.Any() ? steps : null);
            }));

            var loadMoreBtn = "";
            if (messages.Count == pageSize) {
                var oldestId = messages.First().Id;
                var hasMore = await db.Messages.AnyAsync(m => m.ChatSessionId == id && m.Id < oldestId);
                if (hasMore) {
                    loadMoreBtn = $@"
                    <div id='load-more-container' class='flex justify-center py-4'>
                        <button class='btn btn-ghost btn-xs opacity-50 hover:opacity-100' 
                                hx-get='/api/chat/{id}/older-messages?beforeId={oldestId}' 
                                hx-target='#load-more-container' 
                                hx-swap='outerHTML'>
                            Load Older Messages...
                        </button>
                    </div>";
                }
            }

            return Results.Content($@"<div id='chat-box' data-session-id='{id}' data-provider='{session.PreferredProvider}' class='flex-1 overflow-y-auto p-4 md:p-6 space-y-8 custom-scrollbar'>
                {loadMoreBtn}
                <div id='message-list' class='space-y-8'>
                    {messagesHtml}
                </div>
                <script>renderMarkdown(); syncActiveSessionFromDom(); scrollToBottom();</script>
            </div>", "text/html");
        });

        group.MapGet("/chat/{id}/older-messages", async (int id, int beforeId, AppDbContext db, ClaimsPrincipal user) => {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var session = await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
            if (session == null) return Results.NotFound();

            const int pageSize = 20;
            var messages = await db.Messages
                .Where(m => m.ChatSessionId == id && m.Id < beforeId)
                .OrderByDescending(m => m.Timestamp)
                .Take(pageSize)
                .ToListAsync();

            messages = messages.OrderBy(m => m.Timestamp).ToList();

            var messageIds = messages.Select(m => m.Id).ToList();
            var allSteps = await db.AgentSteps
                .Where(s => messageIds.Contains(s.MessageId))
                .ToListAsync();

            var messagesHtml = string.Concat(messages.Select(m => {
                var steps = allSteps.Where(s => s.MessageId == m.Id).ToList();
                return RenderMessage(m, steps.Any() ? steps : null);
            }));

            var loadMoreBtn = "";
            if (messages.Count == pageSize) {
                var oldestId = messages.First().Id;
                var hasMore = await db.Messages.AnyAsync(m => m.ChatSessionId == id && m.Id < oldestId);
                if (hasMore) {
                    loadMoreBtn = $@"
                    <div id='load-more-container' class='flex justify-center py-4'>
                        <button class='btn btn-ghost btn-xs opacity-50 hover:opacity-100' 
                                hx-get='/api/chat/{id}/older-messages?beforeId={oldestId}' 
                                hx-target='#load-more-container' 
                                hx-swap='outerHTML'>
                            Load Older Messages...
                        </button>
                    </div>";
                }
            }

            return Results.Content($@"
                {loadMoreBtn}
                <div hx-swap-oob='afterbegin:#message-list'>
                    {messagesHtml}
                </div>
                <script>renderMarkdown();</script>
            ", "text/html");
        });

        group.MapPost("/chat/rename", async (HttpContext context, AppDbContext db, ClaimsPrincipal user) => {
            var form = await context.Request.ReadFormAsync();
            var id = int.Parse(form["id"]!);
            var title = form["title"].ToString();
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            
            var session = await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
            if (session != null) {
                session.Title = title;
                await db.SaveChangesAsync();
                context.Response.Headers.Append("HX-Trigger", "loadChatList");
            }
            return Results.Ok();
        }).DisableAntiforgery();

        group.MapPost("/user/settings/provider", async (HttpContext context, AppDbContext db, ClaimsPrincipal user) => {
            var form = await context.Request.ReadFormAsync();
            var provider = form["provider"].ToString();
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var u = await db.Users.FindAsync(userId);
            if (u != null) {
                u.DefaultProvider = provider;
                await db.SaveChangesAsync();
            }
            return Results.Ok();
        }).DisableAntiforgery();

        group.MapPost("/chat/settings/provider", async (HttpContext context, AppDbContext db, ClaimsPrincipal user) => {
            var form = await context.Request.ReadFormAsync();
            var id = int.Parse(form["id"]!);
            var provider = form["provider"].ToString();
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var session = await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
            if (session != null) {
                session.PreferredProvider = provider;
                await db.SaveChangesAsync();
            }
            return Results.Ok();
        }).DisableAntiforgery();

        group.MapPost("/chat", async (HttpContext context, AppDbContext db, AiService ai, 
            MemoryConsolidationService consolidation, ClaimsPrincipal user) => {
            var form = await context.Request.ReadFormAsync();
            var content = form["content"].ToString();
            var sessionIdStr = form["sessionId"].ToString();
            int? projectId = int.TryParse(form["projectId"].ToString(), out var postedProjectId) ? postedProjectId : null;
            var provider = form["provider"].ToString();
            var selectedAgents = form["selectedAgents"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            var agentIdStr = form["agentId"].ToString();
            int? agentId = int.TryParse(agentIdStr, out var aid) ? aid : null;
            int? sessionId = int.TryParse(sessionIdStr, out var id) ? id : null;
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var u = await db.Users.FindAsync(userId);
            var isCooperative = form["mode"] == "cooperative";

            ChatSession? session;
            if (sessionId.HasValue) {
                session = await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId);
            } else {
                session = await db.ChatSessions
                    .Where(s => s.UserId == userId && s.ProjectId == projectId)
                    .OrderByDescending(s => s.CreatedAt)
                    .FirstOrDefaultAsync();
            }

            if (session == null) {
                session = new ChatSession { 
                    UserId = userId, 
                    ProjectId = projectId, 
                    Title = content.Length > 20 ? content[..20] + "..." : content,
                    PreferredProvider = string.IsNullOrEmpty(provider) ? (u?.DefaultProvider ?? "gemini") : provider
                };
                db.ChatSessions.Add(session);
                await db.SaveChangesAsync();
            }

            if (string.IsNullOrEmpty(provider)) {
                provider = session.PreferredProvider ?? u?.DefaultProvider ?? "gemini";
            }

            var uMsg = new Message { ChatSessionId = session.Id, Content = content, IsAi = false };
            db.Messages.Add(uMsg);
            await db.SaveChangesAsync();

            string aiResponse;
            if (isCooperative || selectedAgents.Any()) {
                var aMsg = new Message { ChatSessionId = session.Id, Content = "", IsAi = true };
                db.Messages.Add(aMsg);
                await db.SaveChangesAsync();

                var (html, _) = await ai.CooperateAsync(content, userId, aMsg.Id, session.Id, provider, selectedAgents.Any() ? selectedAgents : null);
                aMsg.Content = html;
                await db.SaveChangesAsync();
                aiResponse = html;

                _ = Task.Run(() => consolidation.TryConsolidateAsync(content, html, userId));

                session.UpdatedAt = DateTime.UtcNow;
                if (session.Title.StartsWith("New Chat") || session.Title == content[..Math.Min(content.Length, 20)] + (content.Length > 20 ? "..." : "")) {
                    session.Title = await ai.GenerateTitleAsync(content, html, provider);
                }
                await db.SaveChangesAsync();

                return Results.Content(RenderMessage(uMsg) + RenderMessage(aMsg), "text/html");
            } else {
                aiResponse = await ai.GetResponseAsync(content, userId, session.Id, provider, agentId);
                var aMsg = new Message { ChatSessionId = session.Id, Content = aiResponse, IsAi = true };
                db.Messages.Add(aMsg);
                await db.SaveChangesAsync();

                _ = Task.Run(() => consolidation.TryConsolidateAsync(content, aiResponse, userId));

                session.UpdatedAt = DateTime.UtcNow;
                if (session.Title.StartsWith("New Chat") || session.Title == content[..Math.Min(content.Length, 20)] + (content.Length > 20 ? "..." : "")) {
                    session.Title = await ai.GenerateTitleAsync(content, aiResponse, provider);
                }
                await db.SaveChangesAsync();

                return Results.Content(RenderMessage(uMsg) + RenderMessage(aMsg), "text/html");
            }
        }).DisableAntiforgery();

        group.MapPost("/chat/stream", async (HttpContext context, AppDbContext db, AiService ai, ClaimsPrincipal user) => {
            var form = await context.Request.ReadFormAsync();
            var content = form["content"].ToString();
            var provider = form["provider"].ToString();
            var sessionIdStr = form["sessionId"].ToString();
            var agentIdStr = form["agentId"].ToString();
            int? agentId = int.TryParse(agentIdStr, out var aid) ? aid : null;
            int? projectId = int.TryParse(form["projectId"].ToString(), out var postedProjectId) ? postedProjectId : null;
            int? sessionId = int.TryParse(sessionIdStr, out var id) ? id : null;
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var u = await db.Users.FindAsync(userId);

            ChatSession? session = sessionId.HasValue
                ? await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId)
                : null;

            if (session == null) {
                session = new ChatSession { 
                    UserId = userId, 
                    ProjectId = projectId, 
                    Title = content.Length > 20 ? content[..20] + "..." : content,
                    PreferredProvider = string.IsNullOrEmpty(provider) ? (u?.DefaultProvider ?? "gemini") : provider
                };
                db.ChatSessions.Add(session);
                await db.SaveChangesAsync();
            }

            if (string.IsNullOrEmpty(provider)) {
                provider = session.PreferredProvider ?? u?.DefaultProvider ?? "gemini";
            }

            var uMsg = new Message { ChatSessionId = session.Id, Content = content, IsAi = false };
            db.Messages.Add(uMsg);
            await db.SaveChangesAsync();

            context.Response.Headers.Append("Content-Type", "text/event-stream");
            context.Response.Headers.Append("Cache-Control", "no-cache");
            context.Response.Headers.Append("X-Accel-Buffering", "no");
            context.Response.Headers.Append("X-Session-Id", session.Id.ToString());

            var fullResponse = new StringBuilder();
            await foreach (var chunk in ai.GetResponseStreamAsync(content, userId, session.Id, provider, agentId))
            {
                fullResponse.Append(chunk);
                var data = chunk.Replace("\n", "\\n").Replace("\r", "\\r");
                await context.Response.WriteAsync($"data: {data}\n\n");
                await context.Response.Body.FlushAsync();
            }

            var aMsg = new Message { ChatSessionId = session.Id, Content = fullResponse.ToString(), IsAi = true };
            db.Messages.Add(aMsg);
            
            session.UpdatedAt = DateTime.UtcNow;
            if (session.Title.StartsWith("New Chat")) {
                session.Title = await ai.GenerateTitleAsync(content, fullResponse.ToString(), provider);
            }
            
            await db.SaveChangesAsync();
            await context.Response.WriteAsync("data: [DONE]\n\n");
        }).DisableAntiforgery();

        group.MapPost("/chat/cooperate/stream", async (
            HttpContext context, AppDbContext db, AiService ai,
            MemoryConsolidationService consolidation, ClaimsPrincipal user) =>
        {
            var form = await context.Request.ReadFormAsync();
            var content = form["content"].ToString();
            var provider = form["provider"].ToString();
            int? projectId = int.TryParse(form["projectId"].ToString(), out var postedProjectId) ? postedProjectId : null;
            var sessionIdStr = form["sessionId"].ToString();
            int? sessionId = int.TryParse(sessionIdStr, out var sid) ? sid : null;
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var u = await db.Users.FindAsync(userId);

            context.Response.Headers.Append("Content-Type", "text/event-stream");
            context.Response.Headers.Append("Cache-Control", "no-cache");
            context.Response.Headers.Append("X-Accel-Buffering", "no");

            async Task SendEvent(string eventName, string data)
            {
                await context.Response.WriteAsync($"event: {eventName}\ndata: {data.Replace("\n", "\\n")}\n\n");
                await context.Response.Body.FlushAsync();
            }

            ChatSession? session = sessionId.HasValue
                ? await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId)
                : null;
            if (session == null)
            {
                session = new ChatSession { 
                    UserId = userId, 
                    ProjectId = projectId, 
                    Title = content.Length > 20 ? content[..20] + "..." : content,
                    PreferredProvider = string.IsNullOrEmpty(provider) ? (u?.DefaultProvider ?? "gemini") : provider
                };
                db.ChatSessions.Add(session);
                await db.SaveChangesAsync();
            }

            if (string.IsNullOrEmpty(provider)) {
                provider = session.PreferredProvider ?? u?.DefaultProvider ?? "gemini";
            }

            context.Response.Headers.Append("X-Session-Id", session.Id.ToString());

            var uMsg = new Message { ChatSessionId = session.Id, Content = content, IsAi = false };
            db.Messages.Add(uMsg);
            var aMsg = new Message { ChatSessionId = session.Id, Content = "", IsAi = true };
            db.Messages.Add(aMsg);
            await db.SaveChangesAsync();

            var sessionWithProject = await db.ChatSessions
                .Include(s => s.Project)
                    .ThenInclude(p => p!.Agents)
                .FirstOrDefaultAsync(s => s.Id == session.Id);
            
            var customAgents = sessionWithProject?.Project?.Agents?
                .Where(a => a.IsActive)
                .OrderBy(a => a.Id)
                .Select(a => a.RoleName)
                .ToList();
            
            var agentRoles = (customAgents != null && customAgents.Any())
                ? customAgents
                : new List<string> { "Orchestrator", "Executor", "Reviewer" };

            var sessionPayload = JsonSerializer.Serialize(new { sessionId = session.Id, agents = agentRoles });
            await SendEvent("session", sessionPayload);

            var (html, _) = await ai.CooperateAsync(content, userId, aMsg.Id, session.Id, provider,
                onStepComplete: async (role, stepHtml) =>
                {
                    var payload = JsonSerializer.Serialize(new { role, html = stepHtml });
                    await SendEvent("step-complete", payload);
                });

            aMsg.Content = html;
            
            session.UpdatedAt = DateTime.UtcNow;
            if (session.Title.StartsWith("New Chat")) {
                session.Title = await ai.GenerateTitleAsync(content, html, provider);
            }
            
            await db.SaveChangesAsync();
            await SendEvent("final", html);
            await SendEvent("done", "");

            _ = Task.Run(() => consolidation.TryConsolidateAsync(content, html, userId));
        }).DisableAntiforgery();
    }

    public static string RenderMessage(Message m, List<AgentStep>? steps = null) => $@"
    <div class='chat {(m.IsAi ? "chat-start" : "chat-end")} group message-bubble-container'>
        <div class='chat-bubble shadow-sm {(m.IsAi ? "bg-base-200 text-base-content border border-base-300" : "bg-primary text-primary-content")} markdown leading-relaxed p-3 md:p-4 rounded-[18px] {(m.IsAi ? "rounded-bl-none" : "rounded-tr-none")}'>
            {(steps != null && steps.Any() ? $@"
                <div class='mb-2 border-b border-base-content/5 pb-2'>
                    <div class='text-[10px] font-bold opacity-40 uppercase mb-2'>Execution Steps</div>
                    <div class='space-y-1'>{string.Concat(steps.Select(s => $@"
                        <div class='flex items-center gap-2 text-xs opacity-60'>
                            <div class='w-4 h-4 rounded-full bg-base-300 flex items-center justify-center text-[10px]'>✓</div>
                            <span>{s.Role}</span>
                        </div>"))}
                    </div>
                </div>" : "")}
            <div class='content-body'>{WebUtility.HtmlEncode(m.Content)}</div>
        </div>
        <div class='chat-footer opacity-0 group-hover:opacity-50 transition-opacity flex gap-3 pt-2 px-1'>
            <button class='hover:text-primary transition-colors' onclick='copyText(this)' title='Copy'>
                <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""1.5"" stroke=""currentColor"" class=""w-3.5 h-3.5""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""M15.75 17.25v3.375c0 .621-.504 1.125-1.125 1.125h-9.75a1.125 1.125 0 0 1-1.125-1.125V7.875c0-.621.504-1.125 1.125-1.125H6.75a9.06 9.06 0 0 1 1.5 1.5h6.375a1.125 1.125 0 0 1 1.125 1.125v9.375Zm3 3V6.75a1.125 1.125 0 0 0-1.125-1.125h-1.5a3.375 3.375 0 0 1-3.375-3.375V2.125c0-.621-.504-1.125-1.125-1.125H9.75a1.125 1.125 0 0 0-1.125 1.125V4.5a9.06 9.06 0 0 1 1.5 1.5h6.75a1.125 1.125 0 0 1 1.125 1.125v13.125a1.125 1.125 0 0 1-1.125 1.125H15"" /></svg>
            </button>
            <button class='hover:text-primary transition-colors' onclick='forwardText(this)' title='Forward'>
                <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""1.5"" stroke=""currentColor"" class=""w-3.5 h-3.5""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""M9 15 3 9m0 0 6-6M3 9h12a6 6 0 0 1 0 12h-3"" /></svg>
            </button>
            {(m.IsAi ? $@"<button class='hover:text-primary transition-colors' onclick='saveToMemory(this)' title='Save to Memory'>
                <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""1.5"" stroke=""currentColor"" class=""w-3.5 h-3.5""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""M12 18.75a6 6 0 0 0 6-6v-1.5m-6 7.5a6 6 0 0 1-6-6v-1.5m6 7.5v3.75m-3.75 0h7.5M12 15.75a3 3 0 0 1-3-3V4.5a3 3 0 1 1 6 0v8.25a3 3 0 0 1-3 3Z"" /></svg>
            </button>" : "")}
            {(steps != null && steps.Any() ? "<span class='ml-auto text-[10px] opacity-30 font-semibold'>MULTI-AGENT</span>" : "")}
        </div>
    </div>";
}
