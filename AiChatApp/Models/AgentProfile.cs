using System.ComponentModel.DataAnnotations;

namespace AiChatApp.Models;

public class AgentProfile
{
    public int Id { get; set; }
    
    [Required]
    public string RoleName { get; set; } = string.Empty;
    
    [Required]
    public string SystemPrompt { get; set; } = string.Empty;
    
    public int ProjectId { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    public string? Color { get; set; } // UI representation

    public string? PreferredProvider { get; set; } // Specific AI provider for this agent

    // Navigation property
    public Project? Project { get; set; }
}
