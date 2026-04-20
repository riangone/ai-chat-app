using System.ComponentModel.DataAnnotations;

namespace AiChatApp.Models;

public class SessionMemory
{
    public int Id { get; set; }
    public int ChatSessionId { get; set; }
    public string AgentName { get; set; } = "";      // 書き込んだエージェント
    public string Key { get; set; } = "";             // メモリキー
    public string Value { get; set; } = "";           // メモリ値
    public bool IsPromoted { get; set; } = false;     // LongTermMemoryに昇格済みか
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public ChatSession? ChatSession { get; set; }
}
