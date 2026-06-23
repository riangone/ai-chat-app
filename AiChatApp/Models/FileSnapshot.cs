namespace AiChatApp.Models;

public class FileSnapshot
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public int MessageId { get; set; }
    public string ProjectPath { get; set; } = "";
    public string SnapshotCommitHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ChatSession Session { get; set; } = null!;
}
