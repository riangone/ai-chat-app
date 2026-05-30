using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiChatApp.Data;
using AiChatApp.Models;
using AiChatApp.Services.Harness;
using AiChatApp.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiChatApp.Services;

public class AiCollaborationService
{
    private readonly AppDbContext _db;
    private readonly MemorySearchService _memorySearch;
    private readonly SessionMemoryService _sessionMemory;
    private readonly SkillManagerService _skillManager;
    private readonly PipelineLoaderService _pipelineLoader;
    private readonly SchemaValidationService _schemaValidator;
    private readonly ToolExecutorService _toolExecutor;
    private readonly EvalService _evalService;
    private readonly ICliExecutor _cliExecutor;
    private readonly AiPromptService _promptService;
    private readonly AiResponseProcessor _responseProcessor;
    private readonly ILogger<AiCollaborationService> _logger;

    public AiCollaborationService(
        AppDbContext db, 
        MemorySearchService memorySearch, 
        SessionMemoryService sessionMemory,
        SkillManagerService skillManager,
        PipelineLoaderService pipelineLoader,
        SchemaValidationService schemaValidator,
        ToolExecutorService toolExecutor,
        EvalService evalService,
        ICliExecutor cliExecutor,
        AiPromptService promptService,
        AiResponseProcessor responseProcessor,
        ILogger<AiCollaborationService> logger)
    {
        _db = db;
        _memorySearch = memorySearch;
        _sessionMemory = sessionMemory;
        _skillManager = skillManager;
        _pipelineLoader = pipelineLoader;
        _schemaValidator = schemaValidator;
        _toolExecutor = toolExecutor;
        _evalService = evalService;
        _cliExecutor = cliExecutor;
        _promptService = promptService;
        _responseProcessor = responseProcessor;
        _logger = logger;
    }

    public async Task<(string Html, List<AgentStep> Steps)> CooperateAsync(string task, int userId, int messageId, int? chatSessionId, string? provider = null, List<string>? selectedAgentNames = null, Func<string, string, Task>? onStepComplete = null, CrewProcessType processType = CrewProcessType.Hierarchical)
    {
        var targetProvider = provider ?? "antigravity"; // Default to antigravity if not provided
        var workingDir = await GetProjectRootAsync(chatSessionId);
        task = _promptService.ResolveImageReferences(task, workingDir);
        var steps = new List<AgentStep>();
        List<AgentDefinition> agentsToRun = new();
        ChatSession? session = null;

        if (chatSessionId.HasValue)
        {
            session = await _db.ChatSessions.Include(s => s.Project).ThenInclude(p => p!.Agents).FirstOrDefaultAsync(s => s.Id == chatSessionId.Value);
        }

        if (selectedAgentNames != null && selectedAgentNames.Any())
        {
            var skills = await _skillManager.GetAllSkillsAsync(userId);
            var allAvailable = skills.Select(s => new AgentDefinition(s.Name, s.DisplayName, s.Description, s.Prompt)).ToList();
            foreach (var name in selectedAgentNames)
            {
                var def = allAvailable.FirstOrDefault(a => a.Name == name);
                if (def != null) agentsToRun.Add(def);
            }
        }
        else if (session?.Project?.Agents != null && session.Project.Agents.Any())
        {
            var activeAgents = session.Project.Agents.Where(a => a.IsActive).OrderBy(a => a.Id).ToList();
            agentsToRun = activeAgents.Select(a => new AgentDefinition(a.RoleName, a.RoleName, "DB Agent", a.SystemPrompt, a.Goal, a.Backstory)).ToList();
        }

        if (processType == CrewProcessType.Sequential || agentsToRun.Any())
        {
            // Sequential mode: chain agents one by one
            if (!agentsToRun.Any())
            {
                var skills = await _skillManager.GetAllSkillsAsync(userId);
                agentsToRun = skills.Select(s => new AgentDefinition(s.Name, s.DisplayName, s.Description, s.Prompt)).ToList();
            }
            var sharedProjectRoot = await GetProjectRootAsync(chatSessionId);
            var sharedPolicies = await _promptService.LoadPoliciesAsync();
            string lastOutput = "";
            foreach (var agent in agentsToRun)
            {
                string input = string.IsNullOrEmpty(lastOutput) ? task : $"Task: {task}\n\nPrevious Agent Output:\n{lastOutput}";
                var step = await RunAgentStepAsync(agent.Name, agent.SystemPrompt, input, messageId, targetProvider, userId, chatSessionId, workingDir: sharedProjectRoot, policies: sharedPolicies, session: session, agentGoal: agent.Goal, agentBackstory: agent.Backstory);
                var toolOutput = await _toolExecutor.ExecuteToolsAsync(step.Output, sharedProjectRoot);
                if (toolOutput != step.Output) { step.Output = toolOutput; await _db.SaveChangesAsync(); }

                _ = Task.Run(async () => {
                    try { await _evalService.EvaluateStepAsync(step.Id, task, step.Output, targetProvider); }
                    catch (Exception ex) { _logger.LogError(ex, "EvaluateStepAsync failed for step {StepId}", step.Id); }
                });

                steps.Add(step);
                lastOutput = step.Output;
                if (onStepComplete != null) await onStepComplete(step.Role, _responseProcessor.BuildStepHtml(step));
            }

            if (chatSessionId.HasValue)
            {
                _ = Task.Run(async () => {
                    try { await _sessionMemory.PromoteToLongTermAsync(chatSessionId.Value, userId); } 
                    catch (Exception ex) { _logger.LogError(ex, "PromoteToLongTerm failed for session {SessionId}", chatSessionId.Value); }
                });
            }
            return (_responseProcessor.BuildCooperativeHtml(steps, lastOutput), steps);
        }

        // Hierarchical mode: use orchestrator plan + task graph
        var pipeline = _pipelineLoader.Get("default") ?? throw new Exception("Default pipeline not found.");
        string currentInput = task;
        string contextFromPreviousStages = "";
        OrchestratorPlan? activePlan = null;
        TaskBlackboard? activeBoard = null;
        var pipelineProjectRoot = await GetProjectRootAsync(chatSessionId);
        var pipelinePolicies = await _promptService.LoadPoliciesAsync();
        List<AgentDefinition>? pipelineAgents = agentsToRun.Any() ? agentsToRun : null;

        foreach (var stage in pipeline.Stages)
        {
            if (stage.IsOptional && string.IsNullOrEmpty(currentInput)) continue;
            if (activePlan != null && stage.Role != "reviewer") continue;

            AgentStep? stageStep = null;
            string stagePersona = !string.IsNullOrEmpty(stage.SystemPromptTemplate) ? await _pipelineLoader.GetPromptTemplateAsync(stage.SystemPromptTemplate) : stage.SystemPromptInline ?? "You are a helpful AI assistant.";
            if (stage.RetryOnQualityFail) stagePersona += "\n\nAfter your response, on a new line, include exactly [QUALITY_OK] if your response fully satisfies the task, or [QUALITY_FAIL] if it is incomplete or needs revision.";

            for (int attempt = 1; attempt <= stage.MaxAttempts; attempt++)
            {
                string combinedInput = string.IsNullOrEmpty(contextFromPreviousStages) ? currentInput : $"Task: {task}\n\nContext from previous stages:\n{contextFromPreviousStages}\n\nCurrent stage input: {currentInput}";
                stageStep = await RunAgentStepAsync(stage.Name, stagePersona, combinedInput, messageId, stage.Provider ?? targetProvider, userId, chatSessionId, attempt, pipelineProjectRoot, pipelinePolicies, session);
                var toolOutput = await _toolExecutor.ExecuteToolsAsync(stageStep.Output, pipelineProjectRoot);
                if (toolOutput != stageStep.Output) stageStep.Output = toolOutput;

                if (!string.IsNullOrEmpty(stage.OutputSchema))
                {
                    var validationResult = _schemaValidator.Validate($"{stage.OutputSchema}.json", stageStep.Output);
                    if (!validationResult.IsValid)
                    {
                        stageStep.WasAccepted = false;
                        await _db.SaveChangesAsync();
                        if (attempt < stage.MaxAttempts) { currentInput = _schemaValidator.GenerateCorrectivePrompt(validationResult.Errors, stageStep.Output); continue; }
                    }
                }

                if (stage.RetryOnQualityFail && attempt < stage.MaxAttempts)
                {
                    var qaMatch = Regex.Match(stageStep.Output, @"\[QUALITY_(OK|FAIL)\]", RegexOptions.None, TimeSpan.FromSeconds(1));
                    bool qualityOk = qaMatch.Success && qaMatch.Groups[1].Value == "OK";
                    stageStep.Output = Regex.Replace(stageStep.Output, @"\s*\[QUALITY_(OK|FAIL)\]\s*", " ", RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();
                    if (!qualityOk) { stageStep.WasAccepted = false; await _db.SaveChangesAsync(); continue; }
                }

                stageStep.WasAccepted = true;
                await _db.SaveChangesAsync();

                _ = Task.Run(async () => {
                    try { await _evalService.EvaluateStepAsync(stageStep.Id, task, stageStep.Output, stage.Provider ?? provider); }
                    catch (Exception ex) { _logger.LogError(ex, "EvaluateStepAsync failed for step {StepId}", stageStep.Id); }
                });
                break;
            }

            if (stageStep is null) continue;
            steps.Add(stageStep);
            if (onStepComplete != null) await onStepComplete(stageStep.Role, _responseProcessor.BuildStepHtml(stageStep));

            if (stage.Role == "orchestrator" && stageStep.WasAccepted)
            {
                var plan = TryParseOrchestratorPlan(stageStep.Output);
                if (plan != null)
                {
                    activePlan = plan;
                    var skills = await _skillManager.GetAllSkillsAsync(userId);
                    pipelineAgents ??= skills.Select(s => new AgentDefinition(s.Name, s.DisplayName, s.Description, s.Prompt)).ToList();
                    activeBoard = await ExecuteTaskGraphAsync(plan, task, messageId, targetProvider, userId, chatSessionId, onStepComplete, steps, pipelineAgents, session);
                    currentInput = BuildTaskGraphReviewInput(plan, activeBoard, task);
                    contextFromPreviousStages = $"Orchestrator plan:\n{stageStep.Output}";
                    continue;
                }
            }

            contextFromPreviousStages += $"\n--- Stage: {stage.Name} ---\n{_promptService.Truncate(stageStep.Output, 2000)}\n";
            currentInput = stageStep.Output;
            if (stage.IsFinalStage) break;
        }

        if (!steps.Any()) return (_responseProcessor.BuildCooperativeHtml(new List<AgentStep>(), task), steps);

        // Revision logic
        if (activePlan != null && activeBoard != null)
        {
            var reviewerStep = steps.LastOrDefault(s => s.Role.Equals("Reviewer", StringComparison.OrdinalIgnoreCase));
            if (reviewerStep != null)
            {
                var feedback = TryParseReviewerFeedback(reviewerStep.Output);
                if (feedback?.OverallVerdict is "revision_needed" or "failed")
                {
                    var revised = await ReviseFailedSubtasksAsync(activePlan, feedback, activeBoard, messageId, targetProvider, userId, chatSessionId, onStepComplete, steps, pipelineAgents!, session);
                    if (revised)
                    {
                        var reviewPersona = await _pipelineLoader.GetPromptTemplateAsync("stage_reviewer.md");
                        var reviewInput = BuildTaskGraphReviewInput(activePlan, activeBoard, task);
                        var newReviewStep = await RunAgentStepAsync("Reviewer", reviewPersona, reviewInput, messageId, targetProvider, userId, chatSessionId, 2, pipelineProjectRoot, pipelinePolicies);
                        newReviewStep.WasAccepted = true;
                        await _db.SaveChangesAsync();
                        steps.Add(newReviewStep);
                        if (onStepComplete != null) await onStepComplete(newReviewStep.Role, _responseProcessor.BuildStepHtml(newReviewStep));
                    }
                }
            }
        }

        if (chatSessionId.HasValue)
        {
            _ = Task.Run(async () => {
                try { await _sessionMemory.PromoteToLongTermAsync(chatSessionId.Value, userId); } 
                catch (Exception ex) { _logger.LogError(ex, "PromoteToLongTermAsync failed for session {SessionId}", chatSessionId.Value); }
            });
        }
        return (_responseProcessor.BuildCooperativeHtml(steps, steps.Last().Output), steps);
    }

    public async Task<AgentStep> RunAgentStepAsync(string role, string persona, string input, int messageId, string provider, int userId, int? chatSessionId = null, int attemptNumber = 1, string? workingDir = null, string? policies = null, ChatSession? session = null, IEnumerable<LongTermMemory>? sharedMemories = null, IEnumerable<Skill>? sharedSkills = null, string? agentGoal = null, string? agentBackstory = null)
    {
        var roleSkillsTask = sharedSkills != null ? Task.FromResult(sharedSkills.ToList()) : _memorySearch.SearchSkillsAsync(input, userId, agentRole: role);
        var memoriesTask = sharedMemories != null ? Task.FromResult(sharedMemories.ToList()) : _memorySearch.SearchAsync(input, userId, agentRole: role);
        workingDir ??= await GetProjectRootAsync(chatSessionId);
        policies ??= await _promptService.LoadPoliciesAsync();
        var sb = new StringBuilder();
        if (session == null && chatSessionId.HasValue) session = await _db.ChatSessions.Include(s => s.Project).ThenInclude(p => p!.Agents).FirstOrDefaultAsync(s => s.Id == chatSessionId.Value);

        // Build CrewAI-style agent identity block
        string? resolvedGoal = agentGoal;
        string? resolvedBackstory = agentBackstory;
        if (session != null)
        {
            var projectAgent = session.Project?.Agents.FirstOrDefault(a => a.RoleName.Equals(role, StringComparison.OrdinalIgnoreCase));
            if (projectAgent != null)
            {
                resolvedGoal ??= projectAgent.Goal;
                resolvedBackstory ??= projectAgent.Backstory;
                sb.AppendLine(projectAgent.SystemPrompt);
                sb.AppendLine();
            }
        }
        if (!string.IsNullOrEmpty(resolvedGoal) || !string.IsNullOrEmpty(resolvedBackstory))
        {
            sb.AppendLine($"You are {role}.");
            if (!string.IsNullOrEmpty(resolvedGoal)) sb.AppendLine($"Your goal: {resolvedGoal}");
            if (!string.IsNullOrEmpty(resolvedBackstory)) sb.AppendLine($"Background: {resolvedBackstory}");
            sb.AppendLine();
        }
        sb.AppendLine(persona); sb.AppendLine(policies);
        var memories = (await memoriesTask).Take(5);
        if (memories.Any()) { sb.AppendLine("\n[ユーザーの既知情報・長期記憶]:"); foreach (var m in memories) sb.AppendLine($"- {m.Content}"); }
        if (chatSessionId.HasValue) { var sessionMemoryContext = await _sessionMemory.ReadAllAsContextAsync(chatSessionId.Value); if (!string.IsNullOrEmpty(sessionMemoryContext)) { sb.AppendLine(); sb.AppendLine(sessionMemoryContext); } }
        if (attemptNumber == 1 && chatSessionId.HasValue) sb.AppendLine(_promptService.GetSystemPromptTemplate("MemoryInstruction", "\n[MEMORY INSTRUCTION]: 重要な発見があれば \"MEMORY: key=value\" 形式で行末に出力してください。"));
        var roleSkills = await roleSkillsTask;
        if (roleSkills.Any()) { sb.AppendLine("\n[追加スキル指示]:"); sb.Append(string.Join("\n", roleSkills.Select(s => $"- {s.Description}"))); }
        string fullPersona = sb.ToString();
        var sw = Stopwatch.StartNew();
        var result = await _cliExecutor.ExecuteAsync(input, provider, fullPersona, input, workingDir, agentMode: true);
        sw.Stop();
        if (chatSessionId.HasValue) await _responseProcessor.ParseAndSaveMemoryAsync(chatSessionId.Value, role, result.Output);
        var step = new AgentStep { MessageId = messageId, Role = role, Model = string.IsNullOrEmpty(result.Model) ? provider : result.Model, Provider = provider.ToLower(), PromptTokens = result.PromptTokens, CompletionTokens = result.CompletionTokens, TotalTokens = result.TotalTokens, Persona = _promptService.TruncateMessage(fullPersona, 1000), Input = _promptService.TruncateMessage(input, 2000), Output = result.Output, AttemptNumber = attemptNumber, WasAccepted = true, DurationMs = (int)sw.ElapsedMilliseconds, CreatedAt = DateTime.UtcNow };
        _db.AgentSteps.Add(step);
        await _db.SaveChangesAsync();
        return step;
    }

    private async Task<string?> GetProjectRootAsync(int? chatSessionId)
    {
        if (!chatSessionId.HasValue) return null;
        var session = await _db.ChatSessions.Include(s => s.Project).FirstOrDefaultAsync(s => s.Id == chatSessionId.Value);
        return session?.Project?.RootPath;
    }

    public OrchestratorPlan? TryParseOrchestratorPlan(string output) {
        var json = JsonUtils.ExtractJson(output); if (string.IsNullOrEmpty(json)) return null;
        try {
            using var doc = JsonDocument.Parse(json); var root = doc.RootElement; if (!root.TryGetProperty("subtasks", out var stEl)) return null;
            var subtasks = new List<SubtaskDef>(); foreach (var st in stEl.EnumerateArray()) { var id = st.TryGetProperty("id", out var v) ? v.GetString() ?? "" : ""; var title = st.TryGetProperty("title", out v) ? v.GetString() ?? id : id; var agent = st.TryGetProperty("agent", out v) ? v.GetString() ?? "Executor" : "Executor"; var task = st.TryGetProperty("task", out v) ? v.GetString() ?? "" : ""; var exp = st.TryGetProperty("expectedOutput", out v) ? v.GetString() ?? "" : ""; var deps = new List<string>(); if (st.TryGetProperty("deps", out var depsEl)) foreach (var d in depsEl.EnumerateArray()) deps.Add(d.GetString() ?? ""); if (!string.IsNullOrEmpty(id)) subtasks.Add(new SubtaskDef(id, title, agent, task, exp, deps)); }
            if (!subtasks.Any()) return null; var goal = root.TryGetProperty("goal", out var gv) ? gv.GetString() ?? "" : ""; var note = root.TryGetProperty("executionNote", out var nv) ? nv.GetString() ?? "" : ""; return new OrchestratorPlan(goal, subtasks, note);
        } catch { return null; }
    }

    public ReviewerFeedback? TryParseReviewerFeedback(string output) {
        var json = JsonUtils.ExtractJson(output); if (string.IsNullOrEmpty(json)) return null;
        try {
            using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
            var verdict = root.TryGetProperty("overallVerdict", out var vv) ? vv.GetString() ?? "" : ""; var score = root.TryGetProperty("finalScore", out var sv) ? sv.GetDouble() : 0.0; var summary = root.TryGetProperty("summary", out var sumv) ? sumv.GetString() ?? "" : ""; var reviews = new List<SubtaskReview>();
            if (root.TryGetProperty("subtaskReviews", out var revEl)) { foreach (var r in revEl.EnumerateArray()) { var stId = r.TryGetProperty("subtaskId", out var idv) ? idv.GetString() ?? "" : ""; var stVerdict = r.TryGetProperty("verdict", out var rdv) ? rdv.GetString() ?? "" : ""; var stScore = r.TryGetProperty("score", out var scv) ? scv.GetDouble() : 0.0; var issues = new List<ReviewIssue>(); if (r.TryGetProperty("issues", out var issEl)) foreach (var iss in issEl.EnumerateArray()) issues.Add(new ReviewIssue(iss.TryGetProperty("severity", out var sev) ? sev.GetString() ?? "" : "", iss.TryGetProperty("description", out var dsc) ? dsc.GetString() ?? "" : "", iss.TryGetProperty("suggestion", out var sug) ? sug.GetString() ?? "" : "")); if (!string.IsNullOrEmpty(stId)) reviews.Add(new SubtaskReview(stId, stVerdict, stScore, issues)); } }
            return new ReviewerFeedback(verdict, score, reviews, summary);
        } catch { return null; }
    }

    private static List<List<SubtaskDef>> TopologicalLayers(List<SubtaskDef> subtasks) { var remaining = subtasks.ToList(); var completed = new HashSet<string>(); var layers = new List<List<SubtaskDef>>(); while (remaining.Count > 0) { var ready = remaining.Where(s => s.Deps.All(d => completed.Contains(d))).ToList(); if (!ready.Any()) break; layers.Add(ready); foreach (var s in ready) { remaining.Remove(s); completed.Add(s.Id); } } return layers; }
    private static HashSet<string> GetSubtasksToRevise(List<SubtaskDef> all, HashSet<string> failed) { var toRevise = new HashSet<string>(failed); bool changed = true; while (changed) { changed = false; foreach (var st in all) if (!toRevise.Contains(st.Id) && st.Deps.Any(d => toRevise.Contains(d))) { toRevise.Add(st.Id); changed = true; } } return toRevise; }
    private string BuildAgentInput(SubtaskDef subtask, TaskBlackboard board, OrchestratorPlan plan, string? reviewerIssues = null) { var sb = new StringBuilder(); sb.AppendLine($"## Your Assignment: {subtask.Title}"); sb.AppendLine($"**Task:** {subtask.Task}"); sb.AppendLine($"**Expected Output:** {subtask.ExpectedOutput}"); sb.Append(board.BuildDepContext(subtask.Deps, plan.Subtasks)); if (!string.IsNullOrEmpty(reviewerIssues)) { sb.AppendLine("\n## Revision Required — Reviewer Feedback:"); sb.AppendLine(reviewerIssues); sb.AppendLine("\nPlease address ALL issues above in your revised output."); } return sb.ToString(); }

    public async Task RunSubtaskLayerAsync(List<SubtaskDef> layer, OrchestratorPlan plan, TaskBlackboard board, int messageId, string provider, int userId, int? chatSessionId, Func<string, string, Task>? onStepComplete, List<AgentStep> steps, List<AgentDefinition> allAgents, string? projectRoot, Dictionary<string, string>? reviewerIssuesPerTask = null, int revisionNumber = 0, ChatSession? session = null) {
        var policiesTask = _promptService.LoadPoliciesAsync(); var memoriesTask = _memorySearch.SearchAsync(plan.Goal, userId); var skillsTask = _memorySearch.SearchSkillsAsync(plan.Goal, userId);
        var policies = await policiesTask; var sharedMemories = await memoriesTask; var sharedSkills = await skillsTask;
        var layerTasks = layer.Select(async subtask => {
            var agentDef = allAgents.FirstOrDefault(a => a.Name.Equals(subtask.Agent, StringComparison.OrdinalIgnoreCase)) ?? new AgentDefinition(subtask.Agent, subtask.Agent, "", "You are a helpful AI assistant.");
            string? issues = null; reviewerIssuesPerTask?.TryGetValue(subtask.Id, out issues); var input = BuildAgentInput(subtask, board, plan, issues);
            var step = await RunAgentStepAsync(agentDef.Name, agentDef.SystemPrompt, input, messageId, provider, userId, chatSessionId, revisionNumber + 1, projectRoot, policies, session, null, null, agentDef.Goal, agentDef.Backstory);
            var metadata = ExtractMetadataFromOutput(step.Output);
            board.Write(subtask.Id, agentDef.Name, step.Output, revision: revisionNumber, metadata: metadata);
            _ = Task.Run(async () => { try { await _evalService.EvaluateStepAsync(step.Id, subtask.Task, step.Output, provider); } catch (Exception ex) { _logger.LogError(ex, "EvaluateStepAsync failed for step {StepId}", step.Id); } });
            return (subtask.Id, step);
        }).ToList();
        var results = await Task.WhenAll(layerTasks);
        foreach (var (_, step) in results) { steps.Add(step); if (onStepComplete != null) await onStepComplete(step.Role, _responseProcessor.BuildStepHtml(step)); }
    }

    public async Task<TaskBlackboard> ExecuteTaskGraphAsync(OrchestratorPlan plan, string originalTask, int messageId, string defaultProvider, int userId, int? chatSessionId, Func<string, string, Task>? onStepComplete, List<AgentStep> steps, List<AgentDefinition> allAgents, ChatSession? session = null) {
        var projectRoot = await GetProjectRootAsync(chatSessionId); var board = new TaskBlackboard(); var layers = TopologicalLayers(plan.Subtasks);
        foreach (var layer in layers) await RunSubtaskLayerAsync(layer, plan, board, messageId, defaultProvider, userId, chatSessionId, onStepComplete, steps, allAgents, projectRoot, session: session);
        return board;
    }

    public async Task<bool> ReviseFailedSubtasksAsync(OrchestratorPlan plan, ReviewerFeedback feedback, TaskBlackboard board, int messageId, string defaultProvider, int userId, int? chatSessionId, Func<string, string, Task>? onStepComplete, List<AgentStep> steps, List<AgentDefinition> allAgents, ChatSession? session = null) {
        var failedIds = feedback.SubtaskReviews.Where(r => r.Verdict is "revision_needed" or "failed").Select(r => r.SubtaskId).ToHashSet(); if (!failedIds.Any()) return false;
        var toRevise = GetSubtasksToRevise(plan.Subtasks, failedIds);
        var issuesMap = feedback.SubtaskReviews.Where(r => toRevise.Contains(r.SubtaskId) && r.Issues.Any()).ToDictionary(r => r.SubtaskId, r => string.Join("\n", r.Issues.Select(i => $"- [{i.Severity.ToUpper()}] {i.Description}\n  → Suggestion: {i.Suggestion}")));
        var subtasksToRevise = plan.Subtasks.Where(s => toRevise.Contains(s.Id)).ToList(); var layers = TopologicalLayers(subtasksToRevise); var projectRoot = await GetProjectRootAsync(chatSessionId);
        _logger.LogInformation("Directing revision for {Count} subtasks: {Ids}", toRevise.Count, string.Join(", ", toRevise));
        foreach (var layer in layers) await RunSubtaskLayerAsync(layer, plan, board, messageId, defaultProvider, userId, chatSessionId, onStepComplete, steps, allAgents, projectRoot, issuesMap, 1, session);
        return true;
    }

    public string BuildTaskGraphReviewInput(OrchestratorPlan plan, TaskBlackboard board, string originalTask) {
        var sb = new StringBuilder(); sb.AppendLine($"## Original Task\n{originalTask}"); sb.AppendLine($"\n## Orchestrator Goal\n{plan.Goal}");
        if (!string.IsNullOrEmpty(plan.ExecutionNote)) sb.AppendLine($"\n## Execution Note\n{plan.ExecutionNote}");
        sb.AppendLine("\n## Subtask Results");
        foreach (var subtask in plan.Subtasks) { var artifact = board.Read(subtask.Id); sb.AppendLine($"\n### [{subtask.Id}] {subtask.Title} (agent: {subtask.Agent})"); sb.AppendLine($"**Expected:** {subtask.ExpectedOutput}"); if (artifact != null && artifact.RevisionNumber > 0) sb.AppendLine($"**(Revision {artifact.RevisionNumber})**"); sb.AppendLine($"**Output:**\n{artifact?.Content ?? "(no output)"}"); }
        return sb.ToString();
    }

    public record SubtaskDef(string Id, string Title, string Agent, string Task, string ExpectedOutput, List<string> Deps);
    public record OrchestratorPlan(string Goal, List<SubtaskDef> Subtasks, string ExecutionNote);
    public record BlackboardArtifact(string SubtaskId, string AgentRole, string Content, string ArtifactType, int RevisionNumber, DateTime CreatedAt, string? Metadata = null);
    public sealed class TaskBlackboard
    {
        private readonly Dictionary<string, BlackboardArtifact> _store = new();
        public void Write(string subtaskId, string agentRole, string content, string artifactType = "text", int revision = 0, string? metadata = null) => 
            _store[subtaskId] = new BlackboardArtifact(subtaskId, agentRole, content, artifactType, revision, DateTime.UtcNow, metadata);
        public BlackboardArtifact? Read(string subtaskId) => _store.TryGetValue(subtaskId, out var a) ? a : null;
        public string BuildDepContext(List<string> deps, List<SubtaskDef> allSubtasks)
        {
            if (!deps.Any()) return "";
            var sb = new StringBuilder("\n## Context from upstream tasks:");
            foreach (var dep in deps)
            {
                var artifact = Read(dep);
                if (artifact == null) continue;
                var def = allSubtasks.FirstOrDefault(s => s.Id == dep);
                sb.AppendLine($"\n### [{dep}] {def?.Title ?? dep} (by {artifact.AgentRole}):");
                if (!string.IsNullOrEmpty(artifact.Metadata))
                {
                    sb.AppendLine($"[Upstream Metadata]:\n{artifact.Metadata}\n");
                }
                sb.AppendLine(artifact.Content);
            }
            return sb.ToString();
        }
    }

    private static string? ExtractMetadataFromOutput(string output)
    {
        if (string.IsNullOrEmpty(output)) return null;
        try
        {
            var match = Regex.Match(output, @"<metadata>(.*?)</metadata>", RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            var jsonMatch = Regex.Match(output, @"\{\s*""(confidence|assumptions|errors_encountered)""[^\}]+\}", RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            if (jsonMatch.Success)
            {
                return jsonMatch.Value.Trim();
            }
        }
        catch { }
        return null;
    }
    public record ReviewIssue(string Severity, string Description, string Suggestion);
    public record SubtaskReview(string SubtaskId, string Verdict, double Score, List<ReviewIssue> Issues);
    public record ReviewerFeedback(string OverallVerdict, double FinalScore, List<SubtaskReview> SubtaskReviews, string Summary);
}
