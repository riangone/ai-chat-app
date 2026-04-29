using Microsoft.AspNetCore.SignalR;

namespace AiChatApp.Hubs;

public class ProactiveAgentHub : Hub
{
    private readonly Services.ProactiveBrainService _proactiveBrain;

    public ProactiveAgentHub(Services.ProactiveBrainService proactiveBrain)
    {
        _proactiveBrain = proactiveBrain;
    }

    public async Task JoinProjectGroup(string projectId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"project-{projectId}");
    }

    public async Task LeaveProjectGroup(string projectId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"project-{projectId}");
    }

    public override async Task OnConnectedAsync()
    {
        var userIdStr = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userIdStr))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userIdStr}");
            
            // 触发欢迎洞察
            if (int.TryParse(userIdStr, out int userId))
            {
                await _proactiveBrain.ProcessWelcomeInsightAsync(userId);
            }
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user-{userId}");
        }
        await base.OnDisconnectedAsync(exception);
    }
}
