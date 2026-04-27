using System.ComponentModel.DataAnnotations;

namespace AiChatApp.Models;

public class Note
{
    public int Id { get; set; }
    
    [Required]
    public string Title { get; set; } = string.Empty;
    
    public string? Content { get; set; }
    
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
