using System.ComponentModel.DataAnnotations.Schema;

namespace AiChatApp.Models;

public class LongTermMemory
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public int RelevanceScore { get; set; } = 100;
    public int AccessCount { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;

    [NotMapped]
    public string? SourceFile { get; set; }

    public User? User { get; set; }
}
