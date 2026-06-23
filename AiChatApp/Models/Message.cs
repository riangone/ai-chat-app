using System.ComponentModel.DataAnnotations;

namespace AiChatApp.Models;

public class ChatSession
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? ProjectId { get; set; }
    public string Title { get; set; } = "New Chat";
    public string PreferredProvider { get; set; } = string.Empty;
    public bool PlanModeEnabled { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<Message> Messages { get; set; } = new();
    public Project? Project { get; set; }
}

public class Message
{
    public int Id { get; set; }
    public int ChatSessionId { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsAi { get; set; }
    public string? AgentName { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<AgentStep> AgentSteps { get; set; } = new();
    public List<Attachment> Attachments { get; set; } = new();
}
