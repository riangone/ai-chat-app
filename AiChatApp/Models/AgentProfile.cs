using System.ComponentModel.DataAnnotations;

namespace AiChatApp.Models;

public class AgentProfile
{
    public int Id { get; set; }

    [Required]
    public string RoleName { get; set; } = string.Empty;

    public string Goal { get; set; } = string.Empty;

    public string Backstory { get; set; } = string.Empty;

    [Required]
    public string SystemPrompt { get; set; } = string.Empty;

    public int ProjectId { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Color { get; set; }

    public string? PreferredProvider { get; set; }

    public string? PreferredModel { get; set; }

    public string? PreferredVariant { get; set; }

    public Project? Project { get; set; }
}
