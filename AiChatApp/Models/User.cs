using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiChatApp.Models;

public class User
{
    public int Id { get; set; }
    
    [Required]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;
    
    [Required]
    public string PasswordHash { get; set; } = string.Empty;
    
    [MaxLength(100)]
    public string Email { get; set; } = string.Empty;
    
    public string DefaultProvider { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? LastLoginAt { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    public bool IsAdmin { get; set; } = false;

    // Cache for AI briefing
    public string? LastBriefingContent { get; set; }
    public DateTime? BriefingUpdatedAt { get; set; }
    
    // Navigation properties
    public List<ChatSession> ChatSessions { get; set; } = new();
    public List<LongTermMemory> LongTermMemories { get; set; } = new();
    public List<Note> Notes { get; set; } = new();
    public List<TodoItem> TodoItems { get; set; } = new();
    public List<InputHistory> InputHistories { get; set; } = new();
}
