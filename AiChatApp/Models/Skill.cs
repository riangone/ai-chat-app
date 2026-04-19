namespace AiChatApp.Models;

public class Skill
{
    public int Id { get; set; }
    public int UserId { get; set; }                         // ユーザースコープ（グローバルから変更）
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExampleInput { get; set; } = string.Empty;
    public string TriggerKeywords { get; set; } = string.Empty; // 発火キーワード（カンマ区切り）。空の場合は常に有効
    public bool IsEnabled { get; set; } = true;
    public string? BoundAgentRole { get; set; }             // null=全エージェント, "Executor"=Executorのみ

    public User? User { get; set; }
}
