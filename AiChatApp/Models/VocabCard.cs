using System.ComponentModel.DataAnnotations;

namespace AiChatApp.Models;

public class VocabCard
{
    public int Id { get; set; }
    public int UserId { get; set; }

    [Required]
    public string Word { get; set; } = string.Empty;

    [Required]
    public string Translation { get; set; } = string.Empty;

    public string? Reading { get; set; }
    public string? Example { get; set; }
    public string? ExampleTranslation { get; set; }
    public string? Tags { get; set; }
    public string? Category { get; set; }

    public int Level { get; set; } = 0;
    public int TimesCorrect { get; set; }
    public int TimesWrong { get; set; }
    public DateTime? NextReviewAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
