using System.ComponentModel.DataAnnotations;

namespace AiChatApp.Models;

public class Project
{
    public int Id { get; set; }
    
    [Required]
    public string Name { get; set; } = string.Empty;
    
    [Required]
    public string RootPath { get; set; } = string.Empty;
    
    public int UserId { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Navigation properties
    public List<AgentProfile> Agents { get; set; } = new();
    public List<ChatSession> ChatSessions { get; set; } = new();
}
