using WebPush;
using Microsoft.Extensions.Configuration;
using AiChatApp.Data;
using AiChatApp.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AiChatApp.Services;

public class WebPushService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WebPushService> _logger;
    private readonly VapidDetails _vapidDetails;

    public WebPushService(IConfiguration configuration, IServiceProvider serviceProvider, ILogger<WebPushService> logger)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _logger = logger;

        var publicKey = _configuration["WebPush:VapidPublicKey"];
        var privateKey = _configuration["WebPush:VapidPrivateKey"];
        var subject = _configuration["WebPush:VapidSubject"] ?? "mailto:admin@example.com";

        if (string.IsNullOrEmpty(publicKey) || string.IsNullOrEmpty(privateKey))
        {
            // Generate keys if not present in config (for development/initial setup)
            var keys = VapidHelper.GenerateVapidKeys();
            publicKey = keys.PublicKey;
            privateKey = keys.PrivateKey;
            _logger.LogWarning("Generated new VAPID keys. Please save these in appsettings.json: PublicKey={PublicKey}, PrivateKey={PrivateKey}", publicKey, privateKey);
        }

        _vapidDetails = new VapidDetails(subject, publicKey, privateKey);
    }

    public string GetPublicKey() => _vapidDetails.PublicKey;

    public async Task SendNotificationAsync(int userId, string title, string message, string? url = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var subscriptions = await db.PushSubscriptions
            .Where(s => s.UserId == userId)
            .ToListAsync();

        if (!subscriptions.Any()) return;

        var payload = JsonSerializer.Serialize(new
        {
            title,
            body = message,
            url = url ?? "/",
            icon = "data:image/svg+xml,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><rect width='100' height='100' rx='20' fill='%236366f1'/><text y='.9em' font-size='80' x='10'>💬</text></svg>"
        });

        _logger.LogInformation("Sending web push notification to user {UserId} with {SubscriptionCount} subscriptions", userId, subscriptions.Count);

        var webPushClient = new WebPushClient();
        foreach (var sub in subscriptions)
        {
            try
            {
                var pushSubscription = new WebPush.PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
                await webPushClient.SendNotificationAsync(pushSubscription, payload, _vapidDetails);
                _logger.LogInformation("Successfully sent notification to {Endpoint}", sub.Endpoint);
            }
            catch (WebPushException ex)
            {
                if (ex.StatusCode == System.Net.HttpStatusCode.Gone || ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogInformation("Removing expired subscription: {Endpoint}", sub.Endpoint);
                    db.PushSubscriptions.Remove(sub);
                }
                else
                {
                    _logger.LogError(ex, "Error sending web push notification (Status: {StatusCode}) to {Endpoint}", ex.StatusCode, sub.Endpoint);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error sending web push notification to {Endpoint}", sub.Endpoint);
            }
        }
        
        await db.SaveChangesAsync();
    }
}
