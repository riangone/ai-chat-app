using System.ComponentModel.DataAnnotations;

namespace AiChatApp.Models;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public List<ChatSession> ChatSessions { get; set; } = new();
}

public class ChatSession
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? ProjectId { get; set; } // Added link to project
    public string Title { get; set; } = "New Chat";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<Message> Messages { get; set; } = new();
    public Project? Project { get; set; } // Navigation property
}

public class Message
{
    public int Id { get; set; }
    public int ChatSessionId { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsAi { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
