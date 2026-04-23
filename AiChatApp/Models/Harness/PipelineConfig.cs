namespace AiChatApp.Models.Harness;

public class PipelineStageConfig
{
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public string? SystemPromptTemplate { get; set; }
    public string? SystemPromptInline { get; set; }
    public string? OutputSchema { get; set; }
    public bool RetryOnQualityFail { get; set; } = false;
    public int MaxAttempts { get; set; } = 1;
    public bool IsFinalStage { get; set; } = false;
    public bool IsOptional { get; set; } = false;
    public List<string> Tools { get; set; } = new();
    public Dictionary<string, string> InputMapping { get; set; } = new();
}

public class PipelineConfig
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int MaxRetries { get; set; } = 2;
    public List<PipelineStageConfig> Stages { get; set; } = new();
}

public class PipelineContext
{
    public string Task { get; set; } = string.Empty;
    public string Provider { get; set; } = "gemini";
    public Dictionary<string, object> StepOutputs { get; set; } = new();
}
