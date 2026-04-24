using System.ComponentModel.DataAnnotations;

namespace AiChatApp.Models
{
    public class Note
    {
        public int Id { get; set; }
        
        [Required]
        public string Title { get; set; } = string.Empty;
        
        public string Content { get; set; } = string.Empty;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // 特定のユーザーに紐付ける場合は UserId を追加
        public int? UserId { get; set; }
    }
}
