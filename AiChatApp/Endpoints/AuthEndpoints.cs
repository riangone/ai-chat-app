using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using AiChatApp.Data;
using AiChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace AiChatApp.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // Pages
        app.MapGet("/login", () => Results.File(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/login.html"), "text/html"));
        app.MapGet("/register", () => Results.File(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/register.html"), "text/html"));

        // Auth API
        var group = app.MapGroup("/api/auth");

        group.MapPost("/register", async ([FromForm] string username, [FromForm] string password, AppDbContext db) => {
            if (await db.Users.AnyAsync(u => u.Username == username)) return Results.Redirect("/register?error=exists");
            var user = new User { Username = username, PasswordHash = BCrypt.Net.BCrypt.HashPassword(password) };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return Results.Redirect("/login");
        }).DisableAntiforgery();

        group.MapPost("/login", async ([FromForm] string username, [FromForm] string password, AppDbContext db, HttpContext context) => {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash)) return Results.Redirect("/login?error=invalid");

            var claims = new List<Claim> { 
                new Claim(ClaimTypes.Name, user.Username), 
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()) 
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            return Results.Redirect("/");
        }).DisableAntiforgery();

        group.MapPost("/logout", async (HttpContext context) => {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        });

        group.MapGet("/me", (ClaimsPrincipal user) => {
            if (user.Identity?.IsAuthenticated != true) return Results.Unauthorized();
            return Results.Ok(new {
                id = user.FindFirstValue(ClaimTypes.NameIdentifier),
                username = user.Identity.Name
            });
        }).RequireAuthorization();

        group.MapPost("/change-password", async ([FromForm] string oldPassword, [FromForm] string newPassword, AppDbContext db, ClaimsPrincipal user) => {
            var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdStr == null) return Results.Unauthorized();
            
            var userId = int.Parse(userIdStr);
            var dbUser = await db.Users.FindAsync(userId);
            if (dbUser == null || !BCrypt.Net.BCrypt.Verify(oldPassword, dbUser.PasswordHash)) {
                return Results.BadRequest("Incorrect current password.");
            }

            dbUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            await db.SaveChangesAsync();
            return Results.Ok("Password updated successfully.");
        }).RequireAuthorization().DisableAntiforgery();

        // Admin
        app.MapPost("/api/admin/restart", async (ClaimsPrincipal user) => {
            _ = Task.Run(async () => {
                await Task.Delay(1000);
                var process = new System.Diagnostics.Process {
                    StartInfo = new System.Diagnostics.ProcessStartInfo {
                        FileName = "/bin/bash",
                        Arguments = "-c \"cd .. && ./restart.sh\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                };
                process.Start();
            });
            return Results.Ok("Restarting...");
        }).RequireAuthorization().DisableAntiforgery();

        app.MapGet("/", (ClaimsPrincipal user) => {
            if (user.Identity?.IsAuthenticated != true) return Results.Redirect("/login");
            return Results.File(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/index.html"), "text/html");
        });
    }
}
