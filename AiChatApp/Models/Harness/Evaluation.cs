using System.ComponentModel.DataAnnotations;

namespace AiChatApp.Models.Harness;

public class Evaluation
{
    public int Id { get; set; }
    public int AgentStepId { get; set; }
    public string Criteria { get; set; } = string.Empty; // e.g., "Accuracy", "Safety", "Format"
    public float Score { get; set; } // 0.0 to 1.0
    public string? Reasoning { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public AgentStep? AgentStep { get; set; }
}
