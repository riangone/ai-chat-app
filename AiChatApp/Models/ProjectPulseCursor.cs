namespace AiChatApp.Models;

public class ProjectPulseCursor
{
    public int ProjectId { get; set; }
    public string LastKnownHash { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
