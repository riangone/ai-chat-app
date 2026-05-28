using System.ComponentModel.DataAnnotations;

namespace AiChatApp.Models;

public class Crew
{
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Process { get; set; } = "hierarchical"; // sequential | hierarchical

    public string AgentIds { get; set; } = "[]"; // JSON array of AgentProfile IDs

    public string TaskTemplates { get; set; } = "[]"; // JSON array of task template strings

    public int UserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
