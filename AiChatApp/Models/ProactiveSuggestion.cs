using System;
using System.Collections.Generic;

namespace AiChatApp.Models;

public class ProactiveSuggestion
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Type { get; set; } = "info"; // info, warning, task, insight, success
    public List<SuggestionAction> Actions { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Metadata { get; set; }
}

public class SuggestionAction
{
    public string Label { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty; // e.g., "fix-code", "create-subtasks", "open-note"
    public string? Payload { get; set; }
    public string Style { get; set; } = "btn-ghost"; // btn-primary, btn-outline, etc.
}
