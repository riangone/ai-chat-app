namespace AiChatApp.Models;

public class LongTermMemory
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;       // カンマ区切りで複数タグを格納: "code,csharp,performance"
    public int RelevanceScore { get; set; } = 100;         // 重要度 0-100。低いものは検索時に除外
    public int AccessCount { get; set; } = 0;              // 参照された回数（将来的な鮮度管理用）
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
}
