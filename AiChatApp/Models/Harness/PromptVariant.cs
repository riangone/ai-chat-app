namespace AiChatApp.Models.Harness;

public class PromptVariant
{
    public int Id { get; set; }
    public string TemplatePath { get; set; } = string.Empty;   // logical id, e.g. "stage_executor.md"
    public string FileName { get; set; } = string.Empty;        // actual file under pipelines/prompts/variants/
    public string Status { get; set; } = "challenger";          // challenger | champion | retired | rolled_back
    public int? ParentVariantId { get; set; }                   // null = mutated from the baseline file
    public string Hypothesis { get; set; } = string.Empty;      // LLM rationale for the mutation
    public int TrialCount { get; set; }
    public double? AvgScore { get; set; }                       // refreshed by PromptEvolutionService
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PromotedAt { get; set; }
}
