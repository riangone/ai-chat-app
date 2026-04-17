using AiChatApp.Data;
using AiChatApp.Models;
using AiChatApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite("Data Source=chat.db"));
builder.Services.AddScoped<AiService>();
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

// Pages
app.MapGet("/", (ClaimsPrincipal user) => {
    if (user.Identity?.IsAuthenticated != true) return Results.Redirect("/login");
    return Results.File(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/index.html"), "text/html");
});

// Chat Management
app.MapPost("/api/chat/new", async (AppDbContext db, ClaimsPrincipal user) => {
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var session = new ChatSession { UserId = userId, Title = "New Chat " + DateTime.Now.ToString("HH:mm") };
    db.ChatSessions.Add(session);
    await db.SaveChangesAsync();
    return Results.Content($@"<div id='chat-box' data-session-id='{session.Id}'></div>", "text/html");
}).RequireAuthorization();

app.MapGet("/api/chat/list", async (AppDbContext db, ClaimsPrincipal user) => {
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var sessions = await db.ChatSessions.Where(s => s.UserId == userId).OrderByDescending(s => s.CreatedAt).ToListAsync();
    return Results.Content(string.Concat(sessions.Select(s => $@"
        <div class='flex items-center group w-full'>
            <button hx-get='/api/chat/load/{s.Id}' hx-target='#chat-container' class='btn btn-ghost flex-1 justify-start overflow-hidden text-ellipsis whitespace-nowrap'>{s.Title}</button>
            <button onclick='editTitle({s.Id}, ""{s.Title}"")' class='btn btn-ghost btn-xs opacity-0 group-hover:opacity-100'>Edit</button>
        </div>")), "text/html");
}).RequireAuthorization();

app.MapGet("/api/chat/load/{id}", async (int id, AppDbContext db, ClaimsPrincipal user) => {
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var session = await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
    if (session == null) return Results.NotFound();
    var msgs = await db.Messages.Where(m => m.ChatSessionId == id).OrderBy(m => m.Timestamp).ToListAsync();
    return Results.Content($@"<div id='chat-box' data-session-id='{session.Id}' class='flex-1 overflow-y-auto p-4 md:p-8 space-y-6'>
        {string.Concat(msgs.Select(m => RenderMessage(m)))}
    </div>", "text/html");
}).RequireAuthorization();

app.MapPost("/api/chat/rename", async (HttpContext context, [FromForm] int id, [FromForm] string title, AppDbContext db, ClaimsPrincipal user) => {
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var session = await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
    if (session == null) return Results.NotFound();
    session.Title = title;
    await db.SaveChangesAsync();
    context.Response.Headers.Append("HX-Trigger", "loadChatList");
    return Results.Ok();
}).RequireAuthorization();

app.MapPost("/api/chat", async (HttpContext context, AppDbContext db, AiService ai, ClaimsPrincipal user) => {
    var form = await context.Request.ReadFormAsync();
    var content = form["content"].ToString();
    var sessionIdStr = form["sessionId"].ToString();
    var provider = form["provider"].ToString() ?? "gemini";
    int? sessionId = int.TryParse(sessionIdStr, out var id) ? id : null;
    
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
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
    var aiResponse = await ai.GetResponseAsync(content, provider);
    var aMsg = new Message { ChatSessionId = session.Id, Content = aiResponse, IsAi = true };
    db.Messages.Add(aMsg);
    await db.SaveChangesAsync();
    
    return Results.Content(RenderMessage(uMsg) + RenderMessage(aMsg), "text/html");
}).DisableAntiforgery().RequireAuthorization();

string RenderMessage(Message m) => $@"
<div class='chat {(m.IsAi ? "chat-start" : "chat-end")}'>
    <div class='chat-bubble {(m.IsAi ? "" : "chat-bubble-primary")} markdown'>{System.Net.WebUtility.HtmlEncode(m.Content)}</div>
    <div class='chat-footer opacity-50 flex gap-2 pt-1'>
        <button class='link link-hover text-xs' onclick='copyText(this)'>Copy</button>
        <button class='link link-hover text-xs' onclick='forwardText(this)'>Forward</button>
    </div>
</div>";

app.Run("http://0.0.0.0:5000");
