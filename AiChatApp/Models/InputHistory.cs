using System;

namespace AiChatApp.Models;

public class InputHistory
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime UsedAt { get; set; } = DateTime.UtcNow;
}
