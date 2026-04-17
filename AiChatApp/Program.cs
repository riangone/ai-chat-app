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
    return Results.Content($@"<div id='chat-box' data-session-id='{session.Id}' class='flex-1 overflow-y-auto p-4 md:p-6 space-y-8'>
        <div class='flex flex-col items-center justify-center h-full text-base-content/30 space-y-4'>
            <div class='w-16 h-16 border-4 border-dashed border-current rounded-full opacity-20'></div>
            <p class='text-xl font-medium text-center'>Ready for your questions.</p>
        </div>
    </div>", "text/html");
}).RequireAuthorization();

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

// ... (MapPost rename stays same)

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
<div class='chat {(m.IsAi ? "chat-start" : "chat-end")} group message-bubble-container'>
    <div class='chat-bubble shadow-sm {(m.IsAi ? "bg-base-200 text-base-content border border-base-300" : "bg-primary text-primary-content")} markdown leading-relaxed p-3 md:p-4 rounded-[18px] {(m.IsAi ? "rounded-bl-none" : "rounded-tr-none")}'>
        {System.Net.WebUtility.HtmlEncode(m.Content)}
    </div>
    <div class='chat-footer opacity-0 group-hover:opacity-50 transition-opacity flex gap-3 pt-2 px-1'>
        <button class='hover:text-primary transition-colors' onclick='copyText(this)' title='Copy'>
            <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""1.5"" stroke=""currentColor"" class=""w-3.5 h-3.5""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""M15.75 17.25v3.375c0 .621-.504 1.125-1.125 1.125h-9.75a1.125 1.125 0 0 1-1.125-1.125V7.875c0-.621.504-1.125 1.125-1.125H6.75a9.06 9.06 0 0 1 1.5 1.5h6.375a1.125 1.125 0 0 1 1.125 1.125v9.375Zm3 3V6.75a1.125 1.125 0 0 0-1.125-1.125h-1.5a3.375 3.375 0 0 1-3.375-3.375V2.125c0-.621-.504-1.125-1.125-1.125H9.75a1.125 1.125 0 0 0-1.125 1.125V4.5a9.06 9.06 0 0 1 1.5 1.5h6.75a1.125 1.125 0 0 1 1.125 1.125v13.125a1.125 1.125 0 0 1-1.125 1.125H15"" /></svg>
        </button>
        <button class='hover:text-primary transition-colors' onclick='forwardText(this)' title='Forward'>
            <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""1.5"" stroke=""currentColor"" class=""w-3.5 h-3.5""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""M9 15 3 9m0 0 6-6M3 9h12a6 6 0 0 1 0 12h-3"" /></svg>
        </button>
    </div>
</div>";

app.Run("http://0.0.0.0:5000");
