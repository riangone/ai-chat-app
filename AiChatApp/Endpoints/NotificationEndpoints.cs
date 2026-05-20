using AiChatApp.Data;
using AiChatApp.Models;
using AiChatApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AiChatApp.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").RequireAuthorization();

        group.MapGet("/vapid-public-key", (WebPushService webPushService) =>
        {
            return Results.Ok(new { publicKey = webPushService.GetPublicKey() });
        });

        group.MapPost("/subscribe", async (ClaimsPrincipal user, AppDbContext db, [FromBody] PushSubscriptionRequest request) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            
            // Check if subscription already exists
            var existing = await db.PushSubscriptions
                .FirstOrDefaultAsync(s => s.UserId == userId && s.Endpoint == request.Endpoint);

            if (existing != null)
            {
                existing.P256dh = request.P256dh;
                existing.Auth = request.Auth;
            }
            else
            {
                var sub = new PushSubscription
                {
                    UserId = userId,
                    Endpoint = request.Endpoint,
                    P256dh = request.P256dh,
                    Auth = request.Auth
                };
                db.PushSubscriptions.Add(sub);
            }

            await db.SaveChangesAsync();
            return Results.Ok();
        });

        group.MapPost("/unsubscribe", async (ClaimsPrincipal user, AppDbContext db, [FromBody] string endpoint) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var sub = await db.PushSubscriptions
                .FirstOrDefaultAsync(s => s.UserId == userId && s.Endpoint == endpoint);

            if (sub != null)
            {
                db.PushSubscriptions.Remove(sub);
                await db.SaveChangesAsync();
            }

            return Results.Ok();
        });

        group.MapPost("/test", async (ClaimsPrincipal user, WebPushService webPushService) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await webPushService.SendNotificationAsync(userId, "测试通知", "如果您看到这条消息，说明 Web Push 配置成功！");
            return Results.Ok();
        });
    }
}

public record PushSubscriptionRequest(string Endpoint, string P256dh, string Auth);
