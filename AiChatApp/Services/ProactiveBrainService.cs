using AiChatApp.Hubs;
using AiChatApp.Models;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;

namespace AiChatApp.Services;

public class ProactiveBrainService
{
    private readonly IHubContext<ProactiveAgentHub> _hubContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProactiveBrainService> _logger;

    public ProactiveBrainService(
        IHubContext<ProactiveAgentHub> hubContext,
        IServiceProvider serviceProvider,
        ILogger<ProactiveBrainService> logger)
    {
        _hubContext = hubContext;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// 直接发送一个预定义的建议
    /// </summary>
    public async Task SendSuggestionAsync(ProactiveSuggestion suggestion)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("ProactiveMessage", suggestion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending proactive suggestion");
        }
    }

    /// <summary>
    /// 当 Todo 更新时触发后台分析
    /// </summary>
    public void ProcessTodoChange(TodoItem item, string action)
    {
        // 使用 Task.Run 进行火后即忘 (Fire-and-forget) 的后台 AI 分析
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var aiService = scope.ServiceProvider.GetRequiredService<AiService>();
                
                if (action == "created")
                {
                    // Hyperion 思考新任务
                    await HandleNewTodoAnalysis(item, aiService);
                }
                else if (action == "completed")
                {
                    // Hyperion 庆祝并建议下一步
                    await HandleCompletedTodoAnalysis(item, aiService);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing todo change in background");
            }
        });
    }

    private async Task HandleNewTodoAnalysis(TodoItem item, AiService aiService)
    {
        // 模拟一个轻量级的分析，未来可以调用真正的 AI
        // 这里我们先根据关键字做一些启发式建议，展示 Hyperion 的“主动性”
        
        var suggestion = new ProactiveSuggestion
        {
            Title = "任务智能拆解",
            Content = $"检测到新任务: **{item.Title}**。这个任务看起来需要多个步骤，需要我为你生成详细的子任务清单吗？",
            Type = "task",
            Actions = new List<SuggestionAction>
            {
                new() { Label = "生成子任务", Command = "generate-subtasks", Payload = item.Title, Style = "btn-primary" },
                new() { Label = "忽略", Command = "dismiss", Style = "btn-ghost" }
            }
        };

        if (item.Title.ToLower().Contains("bug") || item.Title.ToLower().Contains("修复"))
        {
            suggestion.Title = "Bug 诊断辅助";
            suggestion.Content = $"检测到 Bug 修复任务: **{item.Title}**。我可以帮你扫描相关代码并查找潜在原因。";
            suggestion.Type = "warning";
            suggestion.Actions[0].Label = "开始诊断";
            suggestion.Actions[0].Command = "diagnose-bug";
        }

        await SendSuggestionAsync(suggestion);
    }

    private async Task HandleCompletedTodoAnalysis(TodoItem item, AiService aiService)
    {
        var suggestion = new ProactiveSuggestion
        {
            Title = "目标达成！",
            Content = $"太棒了！你完成了: **{item.Title}**。基于目前的进度，建议下一步关注 **代码审查** 或 **更新文档**。",
            Type = "success",
            Actions = new List<SuggestionAction>
            {
                new() { Label = "更新 README", Command = "update-docs", Style = "btn-outline" },
                new() { Label = "休息一下", Command = "dismiss", Style = "btn-ghost" }
            }
        };

        await SendSuggestionAsync(suggestion);
    }

    /// <summary>
    /// 当笔记保存时触发分析
    /// </summary>
    public void ProcessNoteChange(Note note)
    {
        _ = Task.Run(async () =>
        {
            // 如果笔记内容包含特定关键词，主动提供帮助
            if (note.Content?.Length > 100)
            {
                await SendSuggestionAsync(new ProactiveSuggestion
                {
                    Title = "知识沉淀建议",
                    Content = $"你的笔记 \"{note.Title}\" 内容很丰富。是否需要我将其总结并存入长期记忆库？",
                    Type = "insight",
                    Actions = new List<SuggestionAction>
                    {
                        new() { Label = "存入记忆", Command = "memorize-note", Payload = note.Id.ToString(), Style = "btn-primary" },
                        new() { Label = "暂不处理", Command = "dismiss", Style = "btn-ghost" }
                    }
                });
            }
        });
    }
}
