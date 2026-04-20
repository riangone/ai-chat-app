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
    return Results.Ok(new { username = user.Identity.Name });
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

app.MapGet("/api/chat/list", async (AppDbContext db, ClaimsPrincipal user) => {
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var sessions = await db.ChatSessions.Where(s => s.UserId == userId).OrderByDescending(s => s.CreatedAt).ToListAsync();
    return Results.Content(string.Concat(sessions.Select(s => $@"
        <div class='flex items-center group w-full mb-1'>
            <button hx-get='/api/chat/load/{s.Id}' hx-target='#chat-container' class='btn btn-ghost btn-sm flex-1 justify-start overflow-hidden text-ellipsis whitespace-nowrap font-normal'>{s.Title}</button>
            <button onclick='editTitle({s.Id}, ""{s.Title}"")' class='btn btn-ghost btn-xs opacity-0 group-hover:opacity-60 px-1'>
                <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""1.5"" stroke=""currentColor"" class=""w-4 h-4""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""m16.862 4.487 1.687-1.688a1.875 1.875 0 1 1 2.652 2.652L10.582 16.07a4.5 4.5 0 0 1-1.897 1.13L6 18l.8-2.685a4.5 4.5 0 0 1 1.13-1.897l8.932-8.931Zm0 0L19.5 7.125M18 14v4.75A2.25 2.25 0 0 1 15.75 21H5.25A2.25 2.25 0 0 1 3 18.75V8.25A2.25 2.25 0 0 1 5.25 6H10"" /></svg>
            </button>
        </div>")), "text/html");
}).RequireAuthorization();

app.MapGet("/api/chat/load/{id}", async (int id, AppDbContext db, ClaimsPrincipal user) => {
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var session = await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
    if (session == null) return Results.NotFound();
    var msgs = await db.Messages.Where(m => m.ChatSessionId == id).OrderBy(m => m.Timestamp).ToListAsync();
    return Results.Content($@"<div id='chat-box' data-session-id='{session.Id}' class='flex-1 overflow-y-auto p-4 md:p-6 space-y-8'>
        {string.Concat(msgs.Select(m => RenderMessage(m)))}
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
    var provider = form["provider"].ToString() is { Length: > 0 } p ? p : "gemini";
    var selectedAgents = form["selectedAgents"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    int? sessionId = int.TryParse(sessionIdStr, out var id) ? id : null;
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var isCooperative = form["mode"] == "cooperative";

    ChatSession? session;
    if (sessionId.HasValue) {
        session = await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId);
    } else {
        session = await db.ChatSessions.Where(s => s.UserId == userId).OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync();
    }

    if (session == null) {
        session = new ChatSession { UserId = userId, Title = content.Length > 20 ? content[..20] + "..." : content };
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
    int? sessionId = int.TryParse(sessionIdStr, out var id) ? id : null;
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // Get or create session
    ChatSession? session;
    if (sessionId.HasValue) {
        session = await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId);
    } else {
        session = await db.ChatSessions.Where(s => s.UserId == userId).OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync();
    }
    
    if (session == null) {
        session = new ChatSession { UserId = userId, Title = content.Length > 20 ? content[..20] + "..." : content };
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync();
    }

    var uMsg = new Message { ChatSessionId = session.Id, Content = content, IsAi = false };
    db.Messages.Add(uMsg);
    await db.SaveChangesAsync();

    context.Response.Headers.Append("Content-Type", "text/event-stream");
    context.Response.Headers.Append("Cache-Control", "no-cache");
    context.Response.Headers.Append("X-Accel-Buffering", "no");

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
        session = new ChatSession { UserId = userId, Title = content.Length > 20 ? content[..20] + "..." : content };
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync();
    }

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

// Skill Management
app.MapGet("/api/skills", async (AppDbContext db, ClaimsPrincipal user) => {
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var skills = await db.Skills.Where(s => s.UserId == userId).OrderByDescending(s => s.Id).ToListAsync();
    return Results.Content(string.Concat(skills.Select(s => {
        var statusBadge = s.IsAutoGenerated && !s.IsApproved 
            ? "<span class='badge badge-warning badge-xs mb-1'>New Candidate</span>" 
            : "";
        var stats = s.UseCount > 0 
            ? $"<div class='text-[10px] opacity-40 mt-1'>Used {s.UseCount} times (Success: {s.SuccessCount})</div>" 
            : "";
        var approveBtn = s.IsAutoGenerated && !s.IsApproved
            ? $"<button hx-post='/api/skills/approve/{s.Id}' hx-target='closest div' hx-swap='outerHTML' class='btn btn-primary btn-xs mt-2'>Approve</button>"
            : "";

        return $@"
        <div class='flex flex-col p-3 bg-base-200 rounded-lg group'>
            <div class='flex items-start justify-between'>
                <div class='flex-1'>
                    {statusBadge}
                    <div class='font-bold text-sm'>{s.Name}</div>
                    <div class='text-xs opacity-60'>{s.Description}</div>
                    {(string.IsNullOrEmpty(s.TriggerKeywords) ? "" : $"<div class='text-[10px] opacity-40 mt-1 font-mono'>Keywords: {s.TriggerKeywords}</div>")}
                    {stats}
                    {approveBtn}
                </div>
                <div class='flex flex-col items-end gap-2'>
                    <input type='checkbox' class='toggle toggle-primary toggle-sm' {(s.IsEnabled ? "checked" : "")} 
                           hx-post='/api/skills/toggle/{s.Id}' hx-swap='none' />
                    <button hx-delete='/api/skills/{s.Id}' hx-target='closest div' hx-swap='outerHTML'
                            class='btn btn-ghost btn-xs text-error opacity-0 group-hover:opacity-100'>Delete</button>
                </div>
            </div>
        </div>";
    })), "text/html");
}).RequireAuthorization();

app.MapPost("/api/skills/approve/{id}", async (int id, AppDbContext db, ClaimsPrincipal user) => {
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var skill = await db.Skills.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
    if (skill != null) {
        skill.IsApproved = true;
        await db.SaveChangesAsync();
    }
    return Results.Redirect("/api/skills"); // 再描画
}).RequireAuthorization();

app.MapDelete("/api/skills/{id}", async (int id, AppDbContext db, ClaimsPrincipal user) => {
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var skill = await db.Skills.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
    if (skill != null) {
        db.Skills.Remove(skill);
        await db.SaveChangesAsync();
    }
    return Results.Ok();
}).RequireAuthorization();

string RenderMessage(Message m) => $@"
<div class='chat {(m.IsAi ? "chat-start" : "chat-end")} group message-bubble-container'>
    <div class='chat-bubble shadow-sm {(m.IsAi ? "bg-base-200 text-base-content border border-base-300" : "bg-primary text-primary-content")} markdown leading-relaxed p-3 md:p-4 rounded-[18px] {(m.IsAi ? "rounded-bl-none" : "rounded-tr-none")}'>{System.Net.WebUtility.HtmlEncode(m.Content)}</div>
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
    </div>
</div>";

app.Run("http://0.0.0.0:5000");
