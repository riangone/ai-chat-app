using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiChatApp.Models;

public class PushSubscription
{
    public int Id { get; set; }
    
    [Required]
    public int UserId { get; set; }
    
    [Required]
    public string Endpoint { get; set; } = string.Empty;
    
    public string P256dh { get; set; } = string.Empty;
    
    public string Auth { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation property
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
