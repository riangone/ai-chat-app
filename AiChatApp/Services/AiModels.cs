using AiChatApp.Models;

namespace AiChatApp.Services;

public enum ContextDepth { Light, Standard, Full }

public record AgentDefinition(string Name, string DisplayName, string Description, string SystemPrompt);

public record SubtaskDef(string Id, string Title, string Agent, string Task, string ExpectedOutput, List<string> Deps);

public record OrchestratorPlan(string Goal, List<SubtaskDef> Subtasks, string ExecutionNote);

public record BlackboardArtifact(string SubtaskId, string AgentRole, string Content, string ArtifactType, int RevisionNumber, DateTime CreatedAt);

public record ReviewIssue(string Severity, string Description, string Suggestion);

public record SubtaskReview(string SubtaskId, string Verdict, double Score, List<ReviewIssue> Issues);

public record ReviewerFeedback(string OverallVerdict, double FinalScore, List<SubtaskReview> SubtaskReviews, string Summary);

public sealed class TaskBlackboard 
{ 
    private readonly Dictionary<string, BlackboardArtifact> _store = new(); 
    public void Write(string subtaskId, string agentRole, string content, string artifactType = "text", int revision = 0) 
        => _store[subtaskId] = new BlackboardArtifact(subtaskId, agentRole, content, artifactType, revision, DateTime.UtcNow); 
    
    public BlackboardArtifact? Read(string subtaskId) 
        => _store.TryGetValue(subtaskId, out var a) ? a : null; 
    
    public string BuildDepContext(List<string> deps, List<SubtaskDef> allSubtasks) 
    { 
        if (!deps.Any()) return ""; 
        var sb = new System.Text.StringBuilder("\n## Context from upstream tasks:"); 
        foreach (var dep in deps) 
        { 
            var artifact = Read(dep); 
            if (artifact == null) continue; 
            var def = allSubtasks.FirstOrDefault(s => s.Id == dep); 
            sb.AppendLine($"\n### [{dep}] {def?.Title ?? dep} (by {artifact.AgentRole}):"); 
            sb.AppendLine(artifact.Content); 
        } 
        return sb.ToString(); 
    } 
}
