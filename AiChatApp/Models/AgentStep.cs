namespace AiChatApp.Models;

public class AgentStep
{
    public int Id { get; set; }
    public int MessageId { get; set; }                      // 紐づくAIメッセージ
    public string Role { get; set; } = string.Empty;        // "Orchestrator" | "Executor" | "Reviewer"
    public string Persona { get; set; } = string.Empty;     // 使用したシステムプロンプト
    public string Input { get; set; } = string.Empty;       // このエージェントへの入力
    public string Output { get; set; } = string.Empty;      // このエージェントの出力
    public int AttemptNumber { get; set; } = 1;             // 差し戻し時は 2, 3...
    public bool WasAccepted { get; set; } = true;           // Reviewerが承認したか
    public int DurationMs { get; set; }                     // 実行時間（ミリ秒）
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Message? Message { get; set; }
    public List<AiChatApp.Models.Harness.Evaluation> Evaluations { get; set; } = new();
}
