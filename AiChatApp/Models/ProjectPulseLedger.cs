namespace AiChatApp.Models;

public class ProjectPulseLedger
{
    public int Id { get; set; }
    public int ProjectId { get; set; }

    public string SourceType { get; set; } = "git_commit";
    public string SourceKey { get; set; } = "";

    public string Status { get; set; } = "pending";

    public string? BranchName { get; set; }
    public string? ResultSummary { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
