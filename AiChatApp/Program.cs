using AiChatApp.Data;
using AiChatApp.Models;
using AiChatApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite("Data Source=chat.db"));
builder.Services.AddScoped<MemorySearchService>();
builder.Services.AddScoped<MemoryConsolidationService>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<SessionMemoryService>();
builder.Services.AddScoped<SkillLearningService>();
builder.Services.AddSingleton<SkillManagerService>();
builder.Services.AddScoped<AiService>();
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options => {
    options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
});
builder.Services.AddAntiforgery();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => {
        options.LoginPath = "/login";
    });
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.MapProjectEndpoints();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
}
app.UseStaticFiles();

// Authentication Routes
app.MapGet("/login", () => Results.File(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/login.html"), "text/html"));
app.MapGet("/register", () => Results.File(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/register.html"), "text/html"));

app.MapPost("/api/auth/register", async ([FromForm] string username, [FromForm] string password, AppDbContext db) => {
    if (await db.Users.AnyAsync(u => u.Username == username)) return Results.BadRequest("User already exists");
    var user = new User { Username = username, PasswordHash = BCrypt.Net.BCrypt.HashPassword(password) };
    db.Users.Add(user);
    await db.SaveChangesAsync();

    // ユーザー固有のデフォルトスキルを作成
    db.Skills.AddRange(
        new Skill
        {
            UserId = user.Id,
            Name = "CodeOptimizer",
            Description = "あなたはコード最適化の専門家です。パフォーマンス・可読性・安全性を分析して改善提案を行ってください。",
            ExampleInput = "このコードを最適化して",
            TriggerKeywords = "code,コード,最適化,optimize,refactor",
            BoundAgentRole = "Executor"
        },
        new Skill
        {
            UserId = user.Id,
            Name = "CreativeWriter",
            Description = "あなたは創造的なライターです。生き生きとした表現と文学的な言語を使って文章を作成してください。",
            ExampleInput = "夕焼けについて書いて",
            TriggerKeywords = "write,書いて,creative,文章,story"
        },
        new Skill
        {
            UserId = user.Id,
            Name = "Translator",
            Description = "あなたは多言語の翻訳専門家です。正確で自然な翻訳を提供してください。",
            ExampleInput = "翻訳して",
            TriggerKeywords = "翻訳,translate,translation"
        }
    );
    await db.SaveChangesAsync();

    return Results.Redirect("/login");
}).DisableAntiforgery();

app.MapPost("/api/auth/login", async ([FromForm] string username, [FromForm] string password, AppDbContext db, HttpContext context) => {
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash)) return Results.Unauthorized();
    
    var claims = new List<Claim> { new Claim(ClaimTypes.Name, user.Username), new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()) };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Redirect("/");
}).DisableAntiforgery();

app.MapPost("/api/auth/logout", async (HttpContext context) => {
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.MapGet("/api/auth/me", (ClaimsPrincipal user) => {
    if (user.Identity?.IsAuthenticated != true) return Results.Unauthorized();
    return Results.Ok(new {
        id = user.FindFirstValue(ClaimTypes.NameIdentifier),
        username = user.Identity.Name
    });
}).RequireAuthorization();

// Pages
app.MapGet("/", (ClaimsPrincipal user) => {
    if (user.Identity?.IsAuthenticated != true) return Results.Redirect("/login");
    return Results.File(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/index.html"), "text/html");
});

app.MapGet("/api/agents/list", async (AiService ai, ClaimsPrincipal user) => {
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var agents = await ai.GetAvailableAgentsAsync(userId);
    return Results.Ok(agents);
}).RequireAuthorization();

// Chat Management
app.MapPost("/api/chat/new", async (HttpContext context, AppDbContext db, ClaimsPrincipal user) => {
    var form = await context.Request.ReadFormAsync();
    int? projectId = int.TryParse(form["projectId"].ToString(), out var pid) ? pid : null;
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var session = new ChatSession { UserId = userId, ProjectId = projectId, Title = "New Chat " + DateTime.Now.ToString("HH:mm") };
    db.ChatSessions.Add(session);
    await db.SaveChangesAsync();
    return Results.Content($@"<div id='chat-box' data-session-id='{session.Id}' class='flex-1 overflow-y-auto p-4 md:p-6 space-y-8'>
        <div class='flex flex-col items-center justify-center h-full text-base-content/30 space-y-4'>
            <div class='w-16 h-16 border-4 border-dashed border-current rounded-full opacity-20'></div>
            <p class='text-xl font-medium text-center'>Ready for your questions.</p>
        </div>
    </div>", "text/html");
}).DisableAntiforgery().RequireAuthorization();

app.MapGet("/api/chat/list", async (int? projectId, AppDbContext db, ClaimsPrincipal user) => {
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var query = db.ChatSessions.Where(s => s.UserId == userId);
    if (projectId.HasValue)
        query = query.Where(s => s.ProjectId == projectId);
    else
        query = query.Where(s => s.ProjectId == null);
        
    var sessions = await query.OrderByDescending(s => s.CreatedAt).ToListAsync();
    return Results.Content(string.Concat(sessions.Select(s => $@"
        <div class='flex items-center group w-full mb-1 border-b border-base-200 pb-1'>
            <button onclick='loadChatSession({s.Id})' class='btn btn-ghost btn-sm flex-1 justify-start overflow-hidden text-ellipsis whitespace-nowrap font-normal'>{s.Title}</button>
            <div class='flex flex-none gap-0.5 ml-1'>
                <button onclick='editTitle({s.Id}, ""{s.Title}"")' class='btn btn-ghost btn-xs px-1 text-primary' title='Rename'>
                    <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""1.5"" stroke=""currentColor"" class=""w-4 h-4""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""m16.862 4.487 1.687-1.688a1.875 1.875 0 1 1 2.652 2.652L10.582 16.07a4.5 4.5 0 0 1-1.897 1.13L6 18l.8-2.685a4.5 4.5 0 0 1 1.13-1.897l8.932-8.931Zm0 0L19.5 7.125M18 14v4.75A2.25 2.25 0 0 1 15.75 21H5.25A2.25 2.25 0 0 1 3 18.75V8.25A2.25 2.25 0 0 1 5.25 6H10"" /></svg>
                </button>
                <button onclick='deleteChat({s.Id})' class='btn btn-ghost btn-xs px-1 text-error' title='Delete'>
                    <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""1.5"" stroke=""currentColor"" class=""w-4 h-4""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""m14.74 9-.346 9m-4.788 0L9.26 9m9.968-3.21c.342.052.682.107 1.022.166m-1.022-.165L18.16 19.673a2.25 2.25 0 0 1-2.244 2.077H8.084a2.25 2.25 0 0 1-2.244-2.077L4.772 5.79m14.456 0a48.108 48.172 0 0 0-3.478-.397m-12 .562c.34-.059.68-.114 1.022-.165m0 0a48.11 48.11 0 0 1 3.478-.397m7.5 0v-.916c0-1.18-.91-2.164-2.09-2.201a51.964 51.964 0 0 0-3.32 0c-1.18.037-2.09 1.022-2.09 2.201v.916m7.5 0a48.667 48.667 0 0 0-7.5 0"" /></svg>
                </button>
            </div>
        </div>")), "text/html");
}).RequireAuthorization();

app.MapDelete("/api/chat/{id}", async (int id, AppDbContext db, ClaimsPrincipal user) => {
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var session = await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
    if (session != null) {
        db.ChatSessions.Remove(session);
        await db.SaveChangesAsync();
    }
    return Results.Ok();
}).RequireAuthorization();

app.MapGet("/api/chat/load/{id}", async (int id, AppDbContext db, ClaimsPrincipal user) => {
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var session = await db.ChatSessions
        .Include(s => s.Messages)
        .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
    if (session == null) return Results.NotFound();

    var messageIds = session.Messages.Select(m => m.Id).ToList();
    var allSteps = await db.AgentSteps
        .Where(s => messageIds.Contains(s.MessageId))
        .ToListAsync();

    var messagesHtml = string.Concat(session.Messages.OrderBy(m => m.Timestamp).Select(m => {
        var steps = allSteps.Where(s => s.MessageId == m.Id).ToList();
        return RenderMessage(m, steps.Any() ? steps : null);
    }));

    return Results.Content($@"<div id='chat-box' data-session-id='{id}' class='flex-1 overflow-y-auto p-4 md:p-6 space-y-8 custom-scrollbar'>
        {messagesHtml}
        <script>renderMarkdown(); syncActiveSessionFromDom(); scrollToBottom();</script>
    </div>", "text/html");
}).RequireAuthorization();

app.MapPost("/api/chat/rename", async (HttpContext context, AppDbContext db, ClaimsPrincipal user) => {
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
}).DisableAntiforgery().RequireAuthorization();

app.MapPost("/api/chat", async (HttpContext context, AppDbContext db, AiService ai, 
    MemoryConsolidationService consolidation, ClaimsPrincipal user) => {
    var form = await context.Request.ReadFormAsync();
    var content = form["content"].ToString();
    var sessionIdStr = form["sessionId"].ToString();
    int? projectId = int.TryParse(form["projectId"].ToString(), out var postedProjectId) ? postedProjectId : null;
    var provider = form["provider"].ToString() is { Length: > 0 } p ? p : "gemini";
    var selectedAgents = form["selectedAgents"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    int? sessionId = int.TryParse(sessionIdStr, out var id) ? id : null;
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
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
        session = new ChatSession { UserId = userId, ProjectId = projectId, Title = content.Length > 20 ? content[..20] + "..." : content };
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync();
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
        return Results.Content(RenderMessage(uMsg) + RenderMessage(aMsg), "text/html");
    } else {
        aiResponse = await ai.GetResponseAsync(content, userId, session.Id, provider);
        var aMsg = new Message { ChatSessionId = session.Id, Content = aiResponse, IsAi = true };
        db.Messages.Add(aMsg);
        await db.SaveChangesAsync();

        _ = Task.Run(() => consolidation.TryConsolidateAsync(content, aiResponse, userId));
        return Results.Content(RenderMessage(uMsg) + RenderMessage(aMsg), "text/html");
    }
}).DisableAntiforgery().RequireAuthorization();


app.MapPost("/api/chat/stream", async (HttpContext context, AppDbContext db, AiService ai, ClaimsPrincipal user) => {
    var form = await context.Request.ReadFormAsync();
    var content = form["content"].ToString();
    var provider = form["provider"].ToString() is { Length: > 0 } p ? p : "gemini";
    var sessionIdStr = form["sessionId"].ToString();
    int? projectId = int.TryParse(form["projectId"].ToString(), out var postedProjectId) ? postedProjectId : null;
    int? sessionId = int.TryParse(sessionIdStr, out var id) ? id : null;
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // Get or create session
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
        session = new ChatSession { UserId = userId, ProjectId = projectId, Title = content.Length > 20 ? content[..20] + "..." : content };
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync();
    }

    var uMsg = new Message { ChatSessionId = session.Id, Content = content, IsAi = false };
    db.Messages.Add(uMsg);
    await db.SaveChangesAsync();

    context.Response.Headers.Append("Content-Type", "text/event-stream");
    context.Response.Headers.Append("Cache-Control", "no-cache");
    context.Response.Headers.Append("X-Accel-Buffering", "no");
    context.Response.Headers.Append("X-Session-Id", session.Id.ToString());

    var fullResponse = new StringBuilder();
    await foreach (var chunk in ai.GetResponseStreamAsync(content, userId, session.Id, provider))
    {
        fullResponse.Append(chunk);
        // Encode chunk for SSE
        var data = chunk.Replace("\n", "\\n").Replace("\r", "\\r");
        await context.Response.WriteAsync($"data: {data}\n\n");
        await context.Response.Body.FlushAsync();
    }

    // Save AI response to DB
    var aMsg = new Message { ChatSessionId = session.Id, Content = fullResponse.ToString(), IsAi = true };
    db.Messages.Add(aMsg);
    await db.SaveChangesAsync();

    await context.Response.WriteAsync("data: [DONE]\n\n");
}).DisableAntiforgery().RequireAuthorization();

app.MapPost("/api/chat/cooperate/stream", async (
    HttpContext context, AppDbContext db, AiService ai,
    MemoryConsolidationService consolidation, ClaimsPrincipal user) =>
{
    var form = await context.Request.ReadFormAsync();
    var content = form["content"].ToString();
    var provider = form["provider"].ToString() is { Length: > 0 } p ? p : "gemini";
    int? projectId = int.TryParse(form["projectId"].ToString(), out var postedProjectId) ? postedProjectId : null;
    int? sessionId = int.TryParse(form["sessionId"].ToString(), out var sid) ? sid : null;
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    context.Response.Headers.Append("Content-Type", "text/event-stream");
    context.Response.Headers.Append("Cache-Control", "no-cache");
    context.Response.Headers.Append("X-Accel-Buffering", "no");

    async Task SendEvent(string eventName, string data)
    {
        await context.Response.WriteAsync($"event: {eventName}\ndata: {data.Replace("\n", "\\n")}\n\n");
        await context.Response.Body.FlushAsync();
    }

    // セッション作成
    ChatSession? session = sessionId.HasValue
        ? await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId)
        : null;
    if (session == null)
    {
        session = new ChatSession { UserId = userId, ProjectId = projectId, Title = content.Length > 20 ? content[..20] + "..." : content };
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync();
    }

    context.Response.Headers.Append("X-Session-Id", session.Id.ToString());

    var uMsg = new Message { ChatSessionId = session.Id, Content = content, IsAi = false };
    db.Messages.Add(uMsg);
    var aMsg = new Message { ChatSessionId = session.Id, Content = "", IsAi = true };
    db.Messages.Add(aMsg);
    await db.SaveChangesAsync();

    // セッションIDとエージェントリストをフロントへ通知
    List<string> agentRoles;
    var sessionWithProject = await db.ChatSessions
        .Include(s => s.Project)
            .ThenInclude(p => p!.Agents)
        .FirstOrDefaultAsync(s => s.Id == session.Id);
    
    var customAgents = sessionWithProject?.Project?.Agents?
        .Where(a => a.IsActive)
        .OrderBy(a => a.Id)
        .Select(a => a.RoleName)
        .ToList();
    
    agentRoles = (customAgents != null && customAgents.Any())
        ? customAgents
        : new List<string> { "Orchestrator", "Executor", "Reviewer" };

    var sessionPayload = JsonSerializer.Serialize(new
    {
        sessionId = session.Id,
        agents = agentRoles
    });
    await SendEvent("session", sessionPayload);

    // 各ステップの開始通知
    var (html, _) = await ai.CooperateAsync(content, userId, aMsg.Id, session.Id, provider,
        onStepComplete: async (role, stepHtml) =>
        {
            var payload = JsonSerializer.Serialize(new { role, html = stepHtml });
            await SendEvent("step-complete", payload);
        });

    aMsg.Content = html;
    await db.SaveChangesAsync();

    await SendEvent("final", html);
    await SendEvent("done", "");

    _ = Task.Run(() => consolidation.TryConsolidateAsync(content, html, userId));
}).DisableAntiforgery().RequireAuthorization();

// Memory Management
app.MapGet("/api/memories", async (AppDbContext db, ClaimsPrincipal user) => {
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var memories = await db.LongTermMemories.Where(m => m.UserId == userId).OrderByDescending(m => m.CreatedAt).ToListAsync();
    return Results.Content(string.Concat(memories.Select(m => $@"
        <div class='flex flex-col gap-1 p-3 bg-base-200 rounded-lg group'>
            <div class='flex justify-between items-start'>
                <span class='text-xs font-bold text-primary'>{m.Tags}</span>
                <div class='flex items-center gap-2'>
                    <div class='w-12 bg-base-300 rounded-full h-1'>
                        <div class='bg-primary h-1 rounded-full' style='width:{m.RelevanceScore}%'></div>
                    </div>
                    <button hx-delete='/api/memories/{m.Id}' hx-target='closest div' hx-swap='outerHTML' 
                            class='btn btn-ghost btn-xs text-error opacity-0 group-hover:opacity-100'>Delete</button>
                </div>
            </div>
            <p class='text-sm'>{m.Content}</p>
            <p class='text-xs opacity-30'>参照 {m.AccessCount}回 · {m.CreatedAt:yyyy/MM/dd}</p>
        </div>")), "text/html");
}).RequireAuthorization();

app.MapPost("/api/memories", async (HttpContext context, AppDbContext db, ClaimsPrincipal user) => {
    var form = await context.Request.ReadFormAsync();
    var content = form["content"].ToString();
    var tags = form["tags"].ToString();
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    
    var memory = new LongTermMemory { UserId = userId, Content = content, Tags = tags, RelevanceScore = 100 };
    db.LongTermMemories.Add(memory);
    await db.SaveChangesAsync();
    return Results.Ok();
}).DisableAntiforgery().RequireAuthorization();

app.MapDelete("/api/memories/{id}", async (int id, AppDbContext db, ClaimsPrincipal user) => {
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var memory = await db.LongTermMemories.FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
    if (memory != null) {
        db.LongTermMemories.Remove(memory);
        await db.SaveChangesAsync();
    }
    return Results.Ok();
}).RequireAuthorization();

// Skill Management (Unified File-Based)
app.MapGet("/api/skills", async (SkillManagerService skillManager) => {
    var skills = await skillManager.GetAllSkillsAsync();
    return Results.Content(string.Concat(skills.Select(s => {
        var systemBadge = s.IsSystem ? "<span class='badge badge-neutral badge-xs mb-1'>System</span>" : "<span class='badge badge-primary badge-xs mb-1'>User</span>";
        return $@"
        <div class='flex flex-col p-3 bg-base-200 rounded-lg group'>
            <div class='flex items-start justify-between'>
                <div class='flex-1'>
                    {systemBadge}
                    <div class='font-bold text-sm'>{s.DisplayName}</div>
                    <div class='text-[10px] opacity-60 mb-2'>{s.Description}</div>
                    <textarea class='textarea textarea-bordered textarea-xs w-full h-24 font-mono text-[10px] bg-base-300' 
                              id='prompt-{s.Name}' readonly>{s.Prompt}</textarea>
                </div>
                <div class='flex flex-col items-end gap-2 ml-2'>
                    <button onclick='editSkillPrompt(""{s.Name}"")' class='btn btn-ghost btn-xs text-primary' id='btn-edit-{s.Name}'>Edit</button>
                    <button onclick='saveSkillPrompt(""{s.Name}"", {s.IsSystem.ToString().ToLower()})' class='btn btn-primary btn-xs hidden' id='btn-save-{s.Name}'>Save</button>
                    {(!s.IsSystem ? $@"<button hx-delete='/api/skills/{s.Name}' hx-target='closest div' hx-swap='outerHTML'
                            class='btn btn-ghost btn-xs text-error opacity-0 group-hover:opacity-100'>Delete</button>" : "")}
                </div>
            </div>
        </div>";
    })), "text/html");
}).RequireAuthorization();

// CLI Session Endpoints
app.MapGet("/api/cli/sessions", () => {
    var sessions = new List<dynamic>();
    
    // 1. Gemini
    var geminiPath = "/home/ubuntu/.gemini/tmp/ai-chat-app/chats";
    if (Directory.Exists(geminiPath)) {
        sessions.AddRange(Directory.GetFiles(geminiPath, "session-*.json")
            .Select(f => new FileInfo(f))
            .Select(f => new { Source = "Gemini", Name = f.Name, Time = f.LastWriteTime, Path = f.FullName }));
    }

    // 2. Claude (Simplified from cache)
    var claudePath = "/home/ubuntu/.cache/claude-cli-nodejs";
    if (Directory.Exists(claudePath)) {
        sessions.AddRange(Directory.GetFiles(claudePath, "*.jsonl")
            .Select(f => new FileInfo(f))
            .Select(f => new { Source = "Claude", Name = f.Name, Time = f.LastWriteTime, Path = f.FullName }));
    }

    // 3. Codex (From history.jsonl)
    var codexHistory = "/home/ubuntu/.codex/history.jsonl";
    if (File.Exists(codexHistory)) {
        sessions.Add(new { Source = "Codex", Name = "history.jsonl", Time = new FileInfo(codexHistory).LastWriteTime, Path = codexHistory });
    }

    // 4. Copilot (Placeholder based on known structure)
    var copilotPath = "/home/ubuntu/.copilot/logs";
    if (Directory.Exists(copilotPath)) {
        sessions.AddRange(Directory.GetFiles(copilotPath, "*.log")
            .Select(f => new FileInfo(f))
            .Select(f => new { Source = "Copilot", Name = f.Name, Time = f.LastWriteTime, Path = f.FullName }));
    }

    var sorted = sessions.OrderByDescending(s => s.Time).Take(50).ToList();

    return Results.Content(string.Concat(sorted.Select(s => {
        var badgeColor = s.Source switch {
            "Gemini" => "badge-primary",
            "Claude" => "badge-secondary",
            "Codex" => "badge-accent",
            "Copilot" => "badge-info",
            _ => "badge-ghost"
        };
        return $@"
        <div class='flex items-center group w-full mb-1'>
            <button onclick='loadCliSession(""{s.Source}"", ""{s.Path.Replace("\\", "\\\\").Replace("\"", "&quot;")}"")'
                    class='btn btn-ghost btn-sm flex-1 justify-start overflow-hidden text-ellipsis whitespace-nowrap font-normal text-xs'>
                <span class='badge {badgeColor} badge-xs mr-2 opacity-70'>{s.Source}</span>
                {s.Time:MM/dd HH:mm}
            </button>
        </div>";
    })), "text/html");
}).RequireAuthorization();

app.MapGet("/api/cli/load", (string source, string path) => {
    if (!File.Exists(path)) return Results.NotFound();

    var htmlBuilder = new StringBuilder();
    htmlBuilder.Append("<div id='chat-box' class='flex-1 overflow-y-auto p-4 md:p-6 space-y-8 custom-scrollbar'>");
    htmlBuilder.Append($@"<div class='alert alert-info shadow-sm mb-4'><svg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 24 24' class='stroke-current shrink-0 w-6 h-6'><path stroke-linecap='round' stroke-linejoin='round' stroke-width='2' d='M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z'></path></svg><span>Viewing {source} Session (Read-Only)</span></div>");

    if (source == "Gemini") {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var messages = doc.RootElement.GetProperty("messages");
        foreach (var msg in messages.EnumerateArray()) {
            var type = msg.GetProperty("type").GetString();
            var isAi = type == "gemini";
            string content = isAi ? (msg.GetProperty("content").GetString() ?? "") : 
                string.Join("\n", msg.GetProperty("content").EnumerateArray().Select(c => c.GetProperty("text").GetString()));
            htmlBuilder.Append(RenderCliMessage(content, isAi));
        }
    } 
    else if (source == "Claude" || source == "Codex") {
        var lines = File.ReadLines(path).TakeLast(100);
        foreach (var line in lines) {
            try {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                string? text = null;
                bool isAi = false;

                if (source == "Codex" && root.TryGetProperty("text", out var t)) {
                    text = t.GetString();
                } else if (source == "Claude") {
                    if (root.TryGetProperty("content", out var c)) text = c.GetString();
                    else if (root.TryGetProperty("debug", out var d)) text = $"[Debug] {d.GetString()}";
                }

                if (!string.IsNullOrEmpty(text)) {
                    htmlBuilder.Append(RenderCliMessage(text, false)); // Simplified role detection
                }
            } catch { /* Skip invalid JSON lines */ }
        }
    }
    else {
        htmlBuilder.Append("<p class='p-4 opacity-50'>Log format parsing for this source is coming soon. Showing raw content:</p>");
        htmlBuilder.Append($"<pre class='p-4 bg-base-300 rounded text-xs overflow-auto'>{File.ReadAllText(path)}</pre>");
    }

    htmlBuilder.Append("</div><script>renderMarkdown(); syncActiveSessionFromDom(); scrollToBottom();</script>");
    return Results.Content(htmlBuilder.ToString(), "text/html");
}).RequireAuthorization();

static string RenderCliMessage(string content, bool isAi) => $@"
    <div class='chat {(isAi ? "chat-start" : "chat-end")} group message-bubble-container'>
        <div class='chat-bubble shadow-sm {(isAi ? "bg-base-200 text-base-content border border-base-300" : "bg-primary text-primary-content")} markdown leading-relaxed p-3 md:p-4 rounded-[18px] {(isAi ? "rounded-bl-none" : "rounded-tr-none")}'>
            <div class='content-body'>{content}</div>
        </div>
    </div>";

app.MapPost("/api/skills/save", async (HttpContext context, SkillManagerService skillManager) => {
    var form = await context.Request.ReadFormAsync();
    var name = form["name"].ToString();
    var prompt = form["prompt"].ToString();
    var isSystem = form["isSystem"].ToString() == "true";
    await skillManager.SaveSkillAsync(name, prompt, isSystem);
    return Results.Ok();
}).DisableAntiforgery().RequireAuthorization();

app.MapDelete("/api/skills/{name}", (string name, SkillManagerService skillManager) => {
    skillManager.DeleteSkill(name, isSystem: false);
    return Results.Ok();
}).RequireAuthorization();

string RenderMessage(Message m, List<AgentStep>? steps = null) => $@"
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
        <div class='content-body'>{m.Content}</div>
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

app.Run("http://0.0.0.0:5000");
