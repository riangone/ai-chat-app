using AiChatApp.Hubs;
using AiChatApp.Models;
using AiChatApp.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
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
    /// 当用户连接时触发，生成“断点续传”洞察。
    /// </summary>
    public async Task ProcessWelcomeInsightAsync(int userId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var aiService = scope.ServiceProvider.GetRequiredService<AiService>();
                
                // 1. 哨兵扫描：获取最近状态
                var summaryInput = "请根据当前项目状态生成一份欢迎报告。重点：最近的进展和接下来的关键任务。";
                
                // 2. 记录员总结 (Summarizer) - 使用 opencode 节省 Token
                var summary = await aiService.ExecuteProactiveAgentAsync(ProactiveAgentProfiles.Summarizer, summaryInput, userId, provider: "opencode");

                // 3. 主脑生成建议 (Hyperion Brain) - 强制简短
                var insight = await aiService.ExecuteProactiveAgentAsync(ProactiveAgentProfiles.HyperionBrain, $"请根据以下总结给出一句简短的欢迎建议（30字以内）：{summary}", userId, provider: "opencode");

                await SendSuggestionAsync(new ProactiveSuggestion
                {
                    Title = "Hyperion 欢迎洞察",
                    Content = insight,
                    Type = "insight",
                    Actions = new List<SuggestionAction>
                    {
                        new() { Label = "开始工作", Command = "dismiss", Style = "btn-primary" }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating welcome insight");
            }
        });
    }

    /// <summary>
    /// 定期扫描项目脉搏（基于增量变更）
    /// </summary>
    public async Task AnalyzeProjectPulseAsync(string diffSummary, int? userId = null)
    {
        if (string.IsNullOrWhiteSpace(diffSummary)) return;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var aiService = scope.ServiceProvider.GetRequiredService<AiService>();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // 使用 opencode 这种免费模型进行扫描
                var prompt = $"作为项目哨兵，请分析以下代码变更并给出一条精炼的工程建议。要求：不要超过50字，使用 Markdown。变更内容：\n{diffSummary}";
                
                var advice = await aiService.ExecuteProactiveAgentAsync(ProactiveAgentProfiles.HyperionBrain, prompt, userId, provider: "opencode");

                if (string.IsNullOrWhiteSpace(advice) || advice.Contains("no significant changes", StringComparison.OrdinalIgnoreCase)) return;

                // 持久化到 Hyperion 会话
                if (userId.HasValue)
                {
                    var session = await db.ChatSessions.FirstOrDefaultAsync(s => s.UserId == userId.Value && s.Title == "Hyperion 任务洞察");
                    if (session == null)
                    {
                        session = new ChatSession { Title = "Hyperion 任务洞察", UserId = userId.Value };
                        db.ChatSessions.Add(session);
                        await db.SaveChangesAsync();
                    }

                    var message = new Message
                    {
                        ChatSessionId = session.Id,
                        Content = advice,
                        IsAi = true,
                        Timestamp = DateTime.UtcNow,
                        AgentName = "Hyperion (Sentinel)"
                    };
                    db.Messages.Add(message);
                    await db.SaveChangesAsync();

                    await SendSuggestionAsync(new ProactiveSuggestion
                    {
                        Title = "项目脉搏洞察",
                        Content = advice,
                        Type = "insight",
                        Actions = new List<SuggestionAction>
                        {
                            new() { Label = "查看建议", Command = "open-session", Payload = session.Id.ToString(), Style = "btn-primary" },
                            new() { Label = "忽略", Command = "dismiss", Style = "btn-ghost" }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in project pulse analysis");
            }
        });
    }

    /// <summary>
    /// 当 Todo 更新时触发后台分析并存入聊天记录
    /// </summary>
    public void ProcessTodoChange(TodoItem item, string action)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var aiService = scope.ServiceProvider.GetRequiredService<AiService>();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                
                if (action == "created")
                {
                    // 1. 分析与生成建议
                    var analysis = await aiService.ExecuteProactiveAgentAsync(ProactiveAgentProfiles.Summarizer, $"任务: {item.Title}. 描述: {item.Description}. 请用一句话概括核心目标。", item.UserId);
                    var advice = await aiService.ExecuteProactiveAgentAsync(ProactiveAgentProfiles.HyperionBrain, $"用户创建了新任务，请给出一条专业的工程建议。要求：不要超过60字，使用 Markdown 加粗重点。内容：{analysis}", item.UserId);

                    // 2. 持久化到聊天记录 (AI 发起的会话)
                    if (item.UserId.HasValue)
                    {
                        var session = await db.ChatSessions
                            .FirstOrDefaultAsync(s => s.UserId == item.UserId.Value && s.Title == "Hyperion 任务洞察");
                        
                        if (session == null)
                        {
                            session = new ChatSession 
                            { 
                                Title = "Hyperion 任务洞察", 
                                UserId = item.UserId.Value,
                                CreatedAt = DateTime.UtcNow,
                                UpdatedAt = DateTime.UtcNow
                            };
                            db.ChatSessions.Add(session);
                            await db.SaveChangesAsync();
                        }

                        var message = new Message
                        {
                            ChatSessionId = session.Id,
                            Content = advice,
                            IsAi = true,
                            Timestamp = DateTime.UtcNow,
                            AgentName = "Hyperion"
                        };
                        db.Messages.Add(message);
                        session.UpdatedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync();

                        // 3. 通知前端 (不仅发送弹窗，还通知有新消息)
                        await SendSuggestionAsync(new ProactiveSuggestion
                        {
                            Title = "收到新洞察",
                            Content = advice,
                            Type = "task",
                            Actions = new List<SuggestionAction>
                            {
                                new() { Label = "打开会话", Command = "open-session", Payload = session.Id.ToString(), Style = "btn-primary" },
                                new() { Label = "忽略", Command = "dismiss", Style = "btn-ghost" }
                            }
                        });
                    }
                }
                else if (action == "completed")
                {
                    // Hyperion 庆祝并建议下一步 - 存入数据库
                    await HandleCompletedTodoAnalysis(item, aiService, db);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing todo change in background");
            }
        });
    }

    private async Task HandleCompletedTodoAnalysis(TodoItem item, AiService aiService, AppDbContext db)
    {
        var title = "目标达成！";
        var content = $"太棒了！你完成了任务: **{item.Title}**。基于目前的进度，建议下一步关注 **代码审查** 或 **更新文档**。";
        
        if (item.UserId.HasValue)
        {
            var session = await db.ChatSessions
                .FirstOrDefaultAsync(s => s.UserId == item.UserId.Value && s.Title == "Hyperion 任务洞察");
            
            if (session == null)
            {
                session = new ChatSession { Title = "Hyperion 任务洞察", UserId = item.UserId.Value };
                db.ChatSessions.Add(session);
                await db.SaveChangesAsync();
            }

            var message = new Message
            {
                ChatSessionId = session.Id,
                Content = content,
                IsAi = true,
                Timestamp = DateTime.UtcNow,
                AgentName = "Hyperion"
            };
            db.Messages.Add(message);
            session.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await SendSuggestionAsync(new ProactiveSuggestion
            {
                Title = title,
                Content = content,
                Type = "success",
                Actions = new List<SuggestionAction>
                {
                    new() { Label = "打开会话", Command = "open-session", Payload = session.Id.ToString(), Style = "btn-primary" },
                    new() { Label = "更新 README", Command = "update-docs", Style = "btn-outline" },
                    new() { Label = "忽略", Command = "dismiss", Style = "btn-ghost" }
                }
            });
        }
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
