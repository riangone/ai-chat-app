using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Net;
using AiChatApp.Data;
using AiChatApp.Models;
using AiChatApp.Services;
using AiChatApp.Extensions;
using Microsoft.EntityFrameworkCore;

using AiChatApp.Services.Infrastructure;

namespace AiChatApp.Endpoints;

public static class ChatEndpoints
{
    private static async Task<ChatSession> GetOrCreateSessionAsync(AppDbContext db, AiService ai, int? sessionId, int? projectId, int userId, string requestedProvider, string userDefaultProvider, string content)
    {
        ChatSession? session = sessionId.HasValue
            ? await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId)
            : null;

        if (session == null) {
            session = new ChatSession {
                UserId = userId,
                ProjectId = projectId,
                Title = content.Length > 20 ? content[..20] + "..." : content,
                PreferredProvider = string.IsNullOrEmpty(requestedProvider)
                    ? (string.IsNullOrEmpty(userDefaultProvider) ? ai.DefaultProvider : userDefaultProvider)
                    : requestedProvider
            };
            db.ChatSessions.Add(session);
            await db.SaveChangesAsync();
        }
        return session;
    }

    private static string ResolveProvider(ChatSession session, string requestedProvider, string userDefaultProvider, string aiDefaultProvider)
    {
        if (!string.IsNullOrEmpty(requestedProvider)) return requestedProvider;
        return session.PreferredProvider 
            ?? (string.IsNullOrEmpty(userDefaultProvider) ? aiDefaultProvider : userDefaultProvider);
    }

    private static async Task StartKeepAliveAsync(HttpContext context, CancellationToken token)
    {
        try {
            while (!token.IsCancellationRequested) {
                await Task.Delay(15000, token).ContinueWith(_ => { });
                if (!token.IsCancellationRequested) {
                    await context.Response.WriteAsync(": ping\n\n");
                    await context.Response.Body.FlushAsync();
                }
            }
        } catch { }
    }

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

        group.MapGet("/chat/list", async (int? projectId, [FromQuery] int? page, [FromQuery] int? pageSize, AppDbContext db, ClaimsPrincipal user) => {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var p = page ?? 1;
            var ps = pageSize ?? 20;

            var query = db.ChatSessions.AsNoTracking().Where(s => s.UserId == userId);
            if (projectId.HasValue)
                query = query.Where(s => s.ProjectId == projectId);
            else
                query = query.Where(s => s.ProjectId == null);

            var sessions = await query.OrderByDescending(s => s.UpdatedAt)
                                     .Skip((p - 1) * ps)
                                     .Take(ps + 1)
                                     .ToListAsync();

            var hasMore = sessions.Count > ps;
            var sessionsToReturn = sessions.Take(ps).ToList();

            var html = string.Concat(sessionsToReturn.Select((s, index) => {
                var isLast = index == sessionsToReturn.Count - 1 && hasMore;
                var scrollAttr = isLast ? $@" hx-get='/api/chat/list?{(projectId.HasValue ? $"projectId={projectId}&" : "")}page={p + 1}&pageSize={ps}' hx-trigger='revealed' hx-swap='afterend'" : "";

                return $@"
                <div class='flex items-center group w-full mb-1 border-b border-base-200 pb-1' {scrollAttr}>
                    <button onclick='loadChatSession({s.Id})' class='btn btn-ghost btn-sm flex-1 justify-start overflow-hidden text-ellipsis whitespace-nowrap font-normal'>
                        <div class='flex flex-col items-start overflow-hidden'>
                            <span class='w-full text-ellipsis overflow-hidden text-left'>{s.Title}</span>
                            <span class='text-[10px] opacity-40 uppercase'>{s.UpdatedAt.GetRelativeTime()}</span>
                        </div>
                    </button>
                    <div class='flex flex-none gap-0.5 ml-1'>
                        <button onclick='editTitle({s.Id}, ""{s.Title.Replace("\"", "&quot;")}"")' class='btn btn-ghost btn-xs px-1 text-primary' title='Rename'>
                            <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""1.5"" stroke=""currentColor"" class=""w-4 h-4""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""m16.862 4.487 1.687-1.688a1.875 1.875 0 1 1 2.652 2.652L10.582 16.07a4.5 4.5 0 0 1-1.897 1.13L6 18l.8-2.685a4.5 4.5 0 0 1 1.13-1.897l8.932-8.931Zm0 0L19.5 7.125M18 14v4.75A2.25 2.25 0 0 1 15.75 21H5.25A2.25 2.25 0 0 1 3 18.75V8.25A2.25 2.25 0 0 1 5.25 6H10"" /></svg>
                        </button>
                        <button onclick='deleteChat({s.Id})' class='btn btn-ghost btn-xs px-1 text-error' title='Delete'>
                            <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""1.5"" stroke=""currentColor"" class=""w-4 h-4""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""m14.74 9-.346 9m-4.788 0L9.26 9m9.968-3.21c.342.052.682.107 1.022.166m-1.022-.165L18.16 19.673a2.25 2.25 0 0 1-2.244 2.077H8.084a2.25 2.25 0 0 1-2.244-2.077L4.772 5.79m14.456 0a48.108 48.172 0 0 0-3.478-.397m-12 .562c.34-.059.68-.114 1.022-.165m0 0a48.11 48.11 0 0 1 3.478-.397m7.5 0v-.916c0-1.18-.91-2.164-2.09-2.201a51.964 51.964 0 0 0-3.32 0c-1.18.037-2.09 1.022-2.09 2.201v.916m7.5 0a48.667 48.667 0 0 0-7.5 0"" /></svg>
                        </button>
                    </div>
                </div>";
            }));

            return Results.Content(html, "text/html");
        });

        group.MapGet("/chat/sessions", async (int? limit, AppDbContext db, ClaimsPrincipal user) => {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var take = limit ?? 20;
            var sessions = await db.ChatSessions
                .AsNoTracking()
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.UpdatedAt)
                .Take(take)
                .Select(s => new { s.Id, s.Title, provider = s.PreferredProvider, updatedAt = s.UpdatedAt })
                .ToListAsync();
            return Results.Ok(sessions);
        }).RequireAuthorization();

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
            var session = await db.ChatSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
            if (session == null) return Results.NotFound();

            const int pageSize = 20;
            var messages = await db.Messages.AsNoTracking()
                .Where(m => m.ChatSessionId == id)
                .Include(m => m.AgentSteps)
                .Include(m => m.Attachments)
                .OrderByDescending(m => m.Timestamp)
                .Take(pageSize)
                .ToListAsync();

            messages = messages.OrderBy(m => m.Timestamp).ToList();

            var messagesHtml = string.Concat(messages.Select((m, idx) => {
                List<AgentStep>? steps = null;
                if (m.IsAi) {
                    steps = m.AgentSteps.Any() ? m.AgentSteps : null;
                    if (steps == null && idx > 0)
                        steps = messages[idx - 1].AgentSteps.Any() ? messages[idx - 1].AgentSteps : null;
                }
                return HtmlUtils.RenderMessage(m, steps);
            }));

            var loadMoreBtn = "";
            if (messages.Count == pageSize) {
                var oldestId = messages.First().Id;
                var hasMore = await db.Messages.AnyAsync(m => m.ChatSessionId == id && m.Id < oldestId);
                if (hasMore) {
                    loadMoreBtn = HtmlUtils.RenderLoadMoreButton(id, oldestId);
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
            var session = await db.ChatSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
            if (session == null) return Results.NotFound();

            const int pageSize = 20;
            var messages = await db.Messages.AsNoTracking()
                .Where(m => m.ChatSessionId == id && m.Id < beforeId)
                .Include(m => m.AgentSteps)
                .Include(m => m.Attachments)
                .OrderByDescending(m => m.Timestamp)
                .Take(pageSize)
                .ToListAsync();

            messages = messages.OrderBy(m => m.Timestamp).ToList();

            var messagesHtml = string.Concat(messages.Select((m, idx) => {
                List<AgentStep>? steps = null;
                if (m.IsAi) {
                    steps = m.AgentSteps.Any() ? m.AgentSteps : null;
                    if (steps == null && idx > 0)
                        steps = messages[idx - 1].AgentSteps.Any() ? messages[idx - 1].AgentSteps : null;
                }
                return HtmlUtils.RenderMessage(m, steps);
            }));

            var loadMoreBtn = "";
            if (messages.Count == pageSize) {
                var oldestId = messages.First().Id;
                var hasMore = await db.Messages.AnyAsync(m => m.ChatSessionId == id && m.Id < oldestId);
                if (hasMore) {
                    loadMoreBtn = HtmlUtils.RenderLoadMoreButton(id, oldestId);
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

        group.MapGet("/lmstudio/models", async (IServiceProvider sp) => {
            try {
                var lmStudio = sp.GetService<LmStudioProvider>();
                if (lmStudio == null) return Results.Ok(new List<string>());
                var models = await lmStudio.GetModelsAsync();
                return Results.Ok(models);
            } catch {
                return Results.Ok(new List<string>());
            }
        });

        group.MapPost("/user/settings/provider", async (HttpContext context, AppDbContext db, ClaimsPrincipal user) => {
            var form = await context.Request.ReadFormAsync();
            var provider = form["provider"].ToString();
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var u = await db.Users.FindAsync(userId);
            if (u != null) {
                u.DefaultProvider = provider;
                await db.SaveChangesAsync();

                // Refresh cookie so DefaultProvider claim stays current in subsequent requests
                var claims = new List<System.Security.Claims.Claim> {
                    new(System.Security.Claims.ClaimTypes.Name, u.Username),
                    new(System.Security.Claims.ClaimTypes.NameIdentifier, u.Id.ToString()),
                    new("IsAdmin", u.IsAdmin.ToString().ToLower()),
                    new("DefaultProvider", provider)
                };
                await context.SignInAsync(
                    Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme,
                    new System.Security.Claims.ClaimsPrincipal(
                        new System.Security.Claims.ClaimsIdentity(claims,
                            Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme)));
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
            MemoryConsolidationService consolidation, IBackgroundTaskTracker tracker, AttachmentService attachmentSvc, ClaimsPrincipal user) => {
            var form = await context.Request.ReadFormAsync();
            var content = form["content"].ToString();
            if (string.IsNullOrWhiteSpace(content))
                return Results.BadRequest("Content is required.");
            var sessionIdStr = form["sessionId"].ToString();
            int? projectId = int.TryParse(form["projectId"].ToString(), out var postedProjectId) ? postedProjectId : null;
            var provider = form["provider"].ToString();
            var model = form["model"].ToString();
            var variant = form["variant"].ToString();
            var thinkingStr = form["thinking"].ToString();
            bool thinking = thinkingStr == "true" || thinkingStr == "1";
            var selectedAgents = form["selectedAgents"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            var agentIdStr = form["agentId"].ToString();
            int? agentId = int.TryParse(agentIdStr, out var aid) ? aid : null;
            int? sessionId = int.TryParse(sessionIdStr, out var id) ? id : null;
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var userDefaultProvider = user.FindFirstValue("DefaultProvider") ?? "";
            var isCooperative = form["mode"] == "cooperative";
            var processTypeStr = form["processType"].ToString();
            var processType = processTypeStr == "sequential" ? CrewProcessType.Sequential : CrewProcessType.Hierarchical;

            var attachmentIds = form["attachmentIds"].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out var n) ? n : 0)
                .Where(n => n > 0).ToList();
            var attachmentContext = attachmentIds.Count > 0
                ? await attachmentSvc.BuildPromptContextAsync(attachmentIds, userId) : "";

            var session = await GetOrCreateSessionAsync(db, ai, sessionId, projectId, userId, provider, userDefaultProvider, content);
            provider = ResolveProvider(session, provider, userDefaultProvider, ai.DefaultProvider);

            var uMsg = new Message { ChatSessionId = session.Id, Content = content, IsAi = false };
            db.Messages.Add(uMsg);

            var enrichedContent = string.IsNullOrEmpty(attachmentContext) ? content : content + "\n" + attachmentContext;

            string aiResponse;
            if (isCooperative || selectedAgents.Any()) {
                var aMsg = new Message { ChatSessionId = session.Id, Content = "", IsAi = true, AgentName = "Multi-Agent" };
                db.Messages.Add(aMsg);
                await db.SaveChangesAsync();
                if (attachmentIds.Count > 0) await attachmentSvc.LinkToMessageAsync(attachmentIds, uMsg.Id, userId);

                try {
                    var (html, _) = await ai.CooperateAsync(enrichedContent, userId, aMsg.Id, session.Id, provider, selectedAgents.Any() ? selectedAgents : null, null, processType);
                    aMsg.Content = html;
                    aiResponse = html;
                    tracker.FireAndForget(() => consolidation.TryConsolidateAsync(content, html, userId), "Memory Consolidation");
                    session.UpdatedAt = DateTime.UtcNow;
                    if (session.Title.StartsWith("New Chat") || session.Title == content[..Math.Min(content.Length, 20)] + (content.Length > 20 ? "..." : ""))
                        session.Title = await ai.GenerateTitleAsync(content, html, provider);
                } catch (Exception ex) {
                    aMsg.Content = ex is TimeoutException ? "[AI タイムアウト。もう一度お試しください。]" : "[AI 処理エラー。もう一度お試しください。]";
                    session.UpdatedAt = DateTime.UtcNow;
                    aiResponse = aMsg.Content;
                }
                await db.SaveChangesAsync();

                var aSteps1 = db.ChangeTracker.Entries<AgentStep>().Select(e => e.Entity)
                    .Where(s => s.MessageId == aMsg.Id || s.MessageId == uMsg.Id).ToList();
                context.Response.Headers.Append("X-Session-Id", session.Id.ToString());
                return Results.Content(HtmlUtils.RenderMessage(uMsg) + HtmlUtils.RenderMessage(aMsg, aSteps1), "text/html");
            } else {
                await db.SaveChangesAsync();
                if (attachmentIds.Count > 0) await attachmentSvc.LinkToMessageAsync(attachmentIds, uMsg.Id, userId);

                Message aMsg;
                try {
                    aiResponse = await ai.GetResponseAsync(enrichedContent, userId, session.Id, provider, agentId, string.IsNullOrEmpty(model) ? null : model, string.IsNullOrEmpty(variant) ? null : variant, thinking);
                    string agentName = provider;
                    if (agentId.HasValue) {
                        var agentProfile = await db.AgentProfiles.FindAsync(agentId.Value);
                        if (agentProfile != null) agentName = agentProfile.RoleName;
                    }
                    aMsg = new Message { ChatSessionId = session.Id, Content = aiResponse, IsAi = true, AgentName = agentName };
                    tracker.FireAndForget(() => consolidation.TryConsolidateAsync(content, aiResponse, userId), "Memory Consolidation");
                    session.UpdatedAt = DateTime.UtcNow;
                    if (session.Title.StartsWith("New Chat") || session.Title == content[..Math.Min(content.Length, 20)] + (content.Length > 20 ? "..." : ""))
                        session.Title = await ai.GenerateTitleAsync(content, aiResponse, provider);
                } catch (Exception ex) {
                    aiResponse = ex is TimeoutException ? "[AI タイムアウト。もう一度お試しください。]" : "[AI 処理エラー。もう一度お試しください。]";
                    aMsg = new Message { ChatSessionId = session.Id, Content = aiResponse, IsAi = true, AgentName = provider };
                    session.UpdatedAt = DateTime.UtcNow;
                }
                db.Messages.Add(aMsg);
                await db.SaveChangesAsync();

                var aSteps2 = db.ChangeTracker.Entries<AgentStep>().Select(e => e.Entity)
                    .Where(s => s.MessageId == uMsg.Id).ToList();
                context.Response.Headers.Append("X-Session-Id", session.Id.ToString());
                return Results.Content(HtmlUtils.RenderMessage(uMsg) + HtmlUtils.RenderMessage(aMsg, aSteps2), "text/html");
            }
        }).DisableAntiforgery();

        group.MapPost("/chat/stream", async (HttpContext context, AppDbContext db, AiService ai,
            MemoryConsolidationService consolidation, AttachmentService attachmentSvc, ClaimsPrincipal user) => {
            var form = await context.Request.ReadFormAsync();
            var content = form["content"].ToString();
            if (string.IsNullOrWhiteSpace(content))
                content = "";
            var provider = form["provider"].ToString();
            var model = form["model"].ToString();
            var variant = form["variant"].ToString();
            var thinkingStr = form["thinking"].ToString();
            bool thinking = thinkingStr == "true" || thinkingStr == "1";
            var sessionIdStr = form["sessionId"].ToString();
            var agentIdStr = form["agentId"].ToString();
            int? agentId = int.TryParse(agentIdStr, out var aid) ? aid : null;
            int? projectId = int.TryParse(form["projectId"].ToString(), out var postedProjectId) ? postedProjectId : null;
            int? sessionId = int.TryParse(sessionIdStr, out var id) ? id : null;
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var userDefaultProvider = user.FindFirstValue("DefaultProvider") ?? "";
            var attachmentIds = form["attachmentIds"].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out var n) ? n : 0)
                .Where(n => n > 0).ToList();
            var attachmentContext = attachmentIds.Count > 0
                ? await attachmentSvc.BuildPromptContextAsync(attachmentIds, userId) : "";
            var enrichedContent = string.IsNullOrEmpty(attachmentContext) ? content : content + "\n" + attachmentContext;

            context.Response.Headers.Append("Content-Type", "text/event-stream");
            context.Response.Headers.Append("Cache-Control", "no-cache");
            context.Response.Headers.Append("X-Accel-Buffering", "no");

            ChatSession session;
            Message uMsg;
            try {
                session = await GetOrCreateSessionAsync(db, ai, sessionId, projectId, userId, provider, userDefaultProvider, content);
                provider = ResolveProvider(session, provider, userDefaultProvider, ai.DefaultProvider);
                uMsg = new Message { ChatSessionId = session.Id, Content = content, IsAi = false };
                db.Messages.Add(uMsg);
                await db.SaveChangesAsync();
                if (attachmentIds.Count > 0) await attachmentSvc.LinkToMessageAsync(attachmentIds, uMsg.Id, userId);
                context.Response.Headers.Append("X-Session-Id", session.Id.ToString());
            } catch (Exception) {
                await context.Response.WriteAsync($"data: [ERROR:セットアップエラー。もう一度お試しください。]\n\n");
                await context.Response.Body.FlushAsync();
                return;
            }

                var fullResponse = new StringBuilder();
                string? streamError = null;
                using var keepAliveCts = new CancellationTokenSource();
                _ = StartKeepAliveAsync(context, keepAliveCts.Token);

                try {
                    await foreach (var chunk in ai.GetResponseStreamAsync(enrichedContent, userId, session.Id, provider, agentId, string.IsNullOrEmpty(model) ? null : model, string.IsNullOrEmpty(variant) ? null : variant, thinking))
                    {
                        fullResponse.Append(chunk);
                        var data = chunk.Replace("\n", "\\n").Replace("\r", "\\r");
                        await context.Response.WriteAsync($"data: {data}\n\n");
                        await context.Response.Body.FlushAsync();
                    }
                } catch (Exception ex) {
                    streamError = ex is TimeoutException ? "AI タイムアウト。もう一度お試しください。" : "AI 処理エラー。もう一度お試しください。";
                    await context.Response.WriteAsync($"data: [ERROR:{streamError}]\n\n");
                    await context.Response.Body.FlushAsync();
                } finally {
                    keepAliveCts.Cancel();
                }

                string agentName = provider;
                if (agentId.HasValue) {
                    var agentProfile = await db.AgentProfiles.FindAsync(agentId.Value);
                    if (agentProfile != null) agentName = agentProfile.RoleName;
                }
                var responseText = streamError != null ? $"[Error: {streamError}]" : fullResponse.ToString();
                var aMsg = new Message { ChatSessionId = session.Id, Content = responseText, IsAi = true, AgentName = agentName };
                db.Messages.Add(aMsg);

                session.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();

                if (streamError == null) {
                    var streamSteps = db.ChangeTracker.Entries<AgentStep>()
                        .Select(e => e.Entity).Where(s => s.MessageId == uMsg.Id).ToList();
                    var spt = streamSteps.Sum(s => s.PromptTokens);
                    var sct = streamSteps.Sum(s => s.CompletionTokens);
                    var stt = streamSteps.Sum(s => s.TotalTokens);
                    if (stt == 0) stt = spt + sct;
                    await context.Response.WriteAsync($"data: [DONE:{spt}:{sct}:{stt}]\n\n");

                    if (session.Title.StartsWith("New Chat")) {
                        var capturedSessionId = session.Id;
                        var capturedContent = content;
                        var capturedResponse = responseText;
                        var capturedProvider = provider;
                        var sp = context.RequestServices;
                        _ = Task.Run(async () => {
                            using var scope = sp.CreateScope();
                            var titleAi = scope.ServiceProvider.GetRequiredService<AiService>();
                            var titleDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                            var s = await titleDb.ChatSessions.FindAsync(capturedSessionId);
                            if (s != null && s.Title.StartsWith("New Chat")) {
                                s.Title = await titleAi.GenerateTitleAsync(capturedContent, capturedResponse, capturedProvider);
                                await titleDb.SaveChangesAsync();
                            }
                        });
                    }
                    _ = Task.Run(() => consolidation.TryConsolidateAsync(content, responseText, userId));
                }
        }).DisableAntiforgery();

        group.MapPost("/chat/cooperate/stream", async (
            HttpContext context, AppDbContext db, AiService ai,
            MemoryConsolidationService consolidation, AttachmentService attachmentSvc, ClaimsPrincipal user) =>
        {
            var form = await context.Request.ReadFormAsync();
            var content = form["content"].ToString();
            if (string.IsNullOrWhiteSpace(content))
                content = "";
            var provider = form["provider"].ToString();
            int? projectId = int.TryParse(form["projectId"].ToString(), out var postedProjectId) ? postedProjectId : null;
            var sessionIdStr = form["sessionId"].ToString();
            int? sessionId = int.TryParse(sessionIdStr, out var sid) ? sid : null;
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var userDefaultProvider = user.FindFirstValue("DefaultProvider") ?? "";
            var attachmentIds = form["attachmentIds"].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out var n) ? n : 0)
                .Where(n => n > 0).ToList();
            var attachmentContext = attachmentIds.Count > 0
                ? await attachmentSvc.BuildPromptContextAsync(attachmentIds, userId) : "";
            var enrichedContent = string.IsNullOrEmpty(attachmentContext) ? content : content + "\n" + attachmentContext;
            var processTypeStr = form["processType"].ToString();
            var streamProcessType = processTypeStr == "sequential" ? CrewProcessType.Sequential : CrewProcessType.Hierarchical;

            context.Response.Headers.Append("Content-Type", "text/event-stream");
            context.Response.Headers.Append("Cache-Control", "no-cache");
            context.Response.Headers.Append("X-Accel-Buffering", "no");

            async Task SendEvent(string eventName, string data)
            {
                await context.Response.WriteAsync($"event: {eventName}\ndata: {data.Replace("\n", "\\n")}\n\n");
                await context.Response.Body.FlushAsync();
            }

            var session = await GetOrCreateSessionAsync(db, ai, sessionId, projectId, userId, provider, userDefaultProvider, content);
            provider = ResolveProvider(session, provider, userDefaultProvider, ai.DefaultProvider);

            context.Response.Headers.Append("X-Session-Id", session.Id.ToString());

            var uMsg = new Message { ChatSessionId = session.Id, Content = content, IsAi = false };
            db.Messages.Add(uMsg);
            var aMsg = new Message { ChatSessionId = session.Id, Content = "", IsAi = true, AgentName = "Multi-Agent" };
            db.Messages.Add(aMsg);
            await db.SaveChangesAsync();
            if (attachmentIds.Count > 0) await attachmentSvc.LinkToMessageAsync(attachmentIds, uMsg.Id, userId);

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

            using var keepAliveCts2 = new CancellationTokenSource();
            _ = Task.Run(async () => {
                while (!keepAliveCts2.Token.IsCancellationRequested) {
                    await Task.Delay(15000, keepAliveCts2.Token).ContinueWith(_ => { });
                    if (!keepAliveCts2.Token.IsCancellationRequested)
                        try { await context.Response.WriteAsync(": ping\n\n"); await context.Response.Body.FlushAsync(); } catch { }
                }
            });

            string html;
            try {
                (html, _) = await ai.CooperateAsync(enrichedContent, userId, aMsg.Id, session.Id, provider,
                    onStepComplete: async (role, stepHtml) =>
                    {
                        var payload = JsonSerializer.Serialize(new { role, html = stepHtml });
                        await SendEvent("step-complete", payload);
                    }, processType: streamProcessType,
                    onStepProgress: async (role, progress) =>
                    {
                        var payload = JsonSerializer.Serialize(new { role, progress });
                        await SendEvent("step-progress", payload);
                    });
                keepAliveCts2.Cancel();
                aMsg.Content = html;
                session.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();

                await SendEvent("final", html);
                await SendEvent("done", "");

                if (session.Title.StartsWith("New Chat")) {
                    var capturedSessionId = session.Id;
                    var capturedContent = content;
                    var capturedHtml = html;
                    var capturedProvider = provider;
                    var sp = context.RequestServices;
                    _ = Task.Run(async () => {
                        using var scope = sp.CreateScope();
                        var titleAi = scope.ServiceProvider.GetRequiredService<AiService>();
                        var titleDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var s = await titleDb.ChatSessions.FindAsync(capturedSessionId);
                        if (s != null && s.Title.StartsWith("New Chat")) {
                            s.Title = await titleAi.GenerateTitleAsync(capturedContent, capturedHtml, capturedProvider);
                            await titleDb.SaveChangesAsync();
                        }
                    });
                }
                _ = Task.Run(() => consolidation.TryConsolidateAsync(content, html, userId));
            } catch (Exception ex) {
                keepAliveCts2.Cancel();
                var errMsg = ex is TimeoutException ? "AI タイムアウト。もう一度お試しください。" : "AI 処理エラー。もう一度お試しください。";
                aMsg.Content = $"[Error: {errMsg}]";
                session.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                await SendEvent("error", errMsg);
                await SendEvent("done", "");
            }
        }).DisableAntiforgery();

        // Plan Mode endpoint: AI generates a plan without executing
        group.MapPost("/chat/plan", async (HttpContext context, AppDbContext db, AiService ai, ClaimsPrincipal user) =>
        {
            var form = await context.Request.ReadFormAsync();
            var content = form["content"].ToString();
            if (string.IsNullOrWhiteSpace(content))
                content = "";
            var provider = form["provider"].ToString();
            int? sessionId = int.TryParse(form["sessionId"].ToString(), out var sid) ? sid : null;
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var userDefaultProvider = user.FindFirstValue("DefaultProvider") ?? "";

            context.Response.Headers.Append("Content-Type", "text/event-stream");
            context.Response.Headers.Append("Cache-Control", "no-cache");
            context.Response.Headers.Append("X-Accel-Buffering", "no");

            ChatSession? session = null;
            if (sessionId.HasValue)
            {
                session = await db.ChatSessions.Include(s => s.Project).FirstOrDefaultAsync(s => s.Id == sessionId.Value && s.UserId == userId);
                provider = string.IsNullOrEmpty(provider)
                    ? (session?.PreferredProvider ?? (string.IsNullOrEmpty(userDefaultProvider) ? ai.DefaultProvider : userDefaultProvider))
                    : provider;
            }
            else
            {
                provider = string.IsNullOrEmpty(provider)
                    ? (string.IsNullOrEmpty(userDefaultProvider) ? ai.DefaultProvider : userDefaultProvider)
                    : provider;
            }

            var systemPrompt = await ai.GetPlanAsync(content, "", userId, sessionId, provider);
            // The plan content comes back as AI response text — stream it
            await foreach (var chunk in ai.GetResponseStreamAsync(content, userId, sessionId, provider))
            {
                var data = chunk.Replace("\n", "\\n").Replace("\r", "\\r");
                await context.Response.WriteAsync($"data: {data}\n\n");
                await context.Response.Body.FlushAsync();
            }
            await context.Response.WriteAsync("data: [PLAN_COMPLETE]\n\n");
            await context.Response.Body.FlushAsync();
        }).DisableAntiforgery();
    }
}
