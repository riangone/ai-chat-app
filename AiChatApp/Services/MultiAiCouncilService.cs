using AiChatApp.Services.Infrastructure;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiChatApp.Data;
using Microsoft.EntityFrameworkCore;

namespace AiChatApp.Services;

/// <summary>
/// Runs a multi-AI deliberation: Gemini proposes, DeepSeek refines, Claude validates and audits.
/// Returns approved evolution proposals (new skills, agent profiles, memory patterns, or healed skills).
/// </summary>
public class MultiAiCouncilService
{
    private readonly AiService _aiService;
    private readonly AppDbContext _db;
    private readonly ILogger<MultiAiCouncilService> _logger;

    public MultiAiCouncilService(AiService aiService, AppDbContext db, ILogger<MultiAiCouncilService> logger)
    {
        _aiService = aiService;
        _db = db;
        _logger = logger;
    }

    public record EvolutionProposal(
        string Type,
        string Title,
        string Content,
        string Rationale,
        List<string> TriggerKeywords,
        float Confidence);

    public async Task<List<EvolutionProposal>> DeliberateAsync(string conversationBatch, int userId)
    {
        // 1. Perception Phase: 获取使用频率高但成功率低（需要自愈）的技能提案
        var lowPerformingSkills = await GetLowPerformingSkillsProposalsAsync(userId);
        
        // 2. Propose Phase (Gemini - Proposer)
        var proposals = await GetGeminiProposalsAsync(conversationBatch, userId, lowPerformingSkills);
        
        // 融合新提案与自愈提案
        var allProposals = proposals.Concat(lowPerformingSkills).ToList();
        if (allProposals.Count == 0) return [];

        // 3. Refine Phase (DeepSeek - Refiner, using opencode CLI)
        var refinedProposals = await GetDeepSeekRefinementAsync(allProposals, conversationBatch);
        if (refinedProposals.Count == 0)
        {
            _logger.LogWarning("[Council] DeepSeek refinement returned empty. Falling back to Gemini draft proposals.");
            refinedProposals = allProposals;
        }

        // 4. Validate Phase (Claude - Validator, safety & deadlock audit)
        var approved = await GetClaudeValidationAsync(refinedProposals, conversationBatch);

        _logger.LogInformation("[Council] userId={UserId}: {Total} proposed (incl. {LowCount} low-perf) → {Refined} refined → {Approved} approved",
            userId, proposals.Count, lowPerformingSkills.Count, refinedProposals.Count, approved.Count);

        return approved;
    }

    private async Task<List<EvolutionProposal>> GetLowPerformingSkillsProposalsAsync(int userId)
    {
        try
        {
            // 寻找使用次数 >= 3 且成功率 < 60% 的技能
            var lowSkills = await _db.Skills
                .Where(s => s.UserId == userId && s.IsEnabled && s.UseCount >= 3 && ((float)s.SuccessCount / s.UseCount) < 0.60f)
                .ToListAsync();

            var proposals = new List<EvolutionProposal>();
            foreach (var skill in lowSkills)
            {
                proposals.Add(new EvolutionProposal(
                    Type: "skill_heal",
                    Title: skill.Name,
                    Content: skill.Description,
                    Rationale: $"Heal low-performing skill '{skill.Name}' (Success rate: {skill.SuccessCount}/{skill.UseCount})",
                    TriggerKeywords: skill.TriggerKeywords.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(k => k.Trim()).ToList(),
                    Confidence: 0.5f
                ));
            }
            return proposals;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Council] Failed to query low-performing skills for self-healing");
            return [];
        }
    }

    private async Task<List<EvolutionProposal>> GetGeminiProposalsAsync(string conversationBatch, int userId, List<EvolutionProposal> lowPerformingSkills)
    {
        var lowSkillsContext = "";
        if (lowPerformingSkills.Count > 0)
        {
            var json = JsonSerializer.Serialize(lowPerformingSkills.Select(p => new { p.Title, p.Content, p.Rationale }), JsonOpts);
            lowSkillsContext = $"\nLOW-PERFORMING SKILLS REQUIRING DIAGNOSIS & RE-EVOLUTION:\n{json}\n";
        }

        var prompt = $$"""
            You are analyzing conversation patterns to help an AI assistant evolve and improve.
            Study the conversation batch below and identify recurring needs that would benefit from a dedicated Agent/Skill.

            Look for:
            1. Topics the user asks about repeatedly across messages
            2. Multi-step workflows the user performs manually that could be automated
            3. Domain expertise the user frequently needs
            {{lowSkillsContext}}
            CONVERSATION BATCH:
            {{conversationBatch[..Math.Min(3000, conversationBatch.Length)]}}

            OUTPUT FORMAT (valid JSON only, no markdown fences):
            {
              "proposals": [
                {
                  "type": "skill",
                  "title": "Short kebab-case-name",
                  "content": "Full system prompt for this specialized agent (150-400 words). Include role, expertise, response style, and example behaviors.",
                  "rationale": "One sentence explaining what pattern triggered this proposal",
                  "trigger_keywords": ["keyword1", "keyword2", "keyword3"],
                  "confidence": 0.82
                }
              ]
            }

            Rules:
            - Only propose if genuinely useful (confidence >= 0.70)
            - Maximum 3 proposals
            - Return {"proposals": []} if no strong patterns found
            - type must be one of: "skill", "agent", "memory_pattern"
            """;

        try
        {
            var raw = await _aiService.ExecuteCliDirectAsync(prompt, "gemini");
            var json = JsonUtils.ExtractJson(raw);
            var root = JsonSerializer.Deserialize<ProposalRoot>(json, JsonOpts);
            return root?.Proposals?
                .Where(p => !string.IsNullOrWhiteSpace(p.Title) && !string.IsNullOrWhiteSpace(p.Content) && p.Confidence >= 0.70f)
                .Select(ToProposal)
                .ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Council] Gemini proposal phase failed");
            return [];
        }
    }

    private async Task<List<EvolutionProposal>> GetDeepSeekRefinementAsync(List<EvolutionProposal> proposals, string context)
    {
        if (proposals.Count == 0) return [];

        var proposalsJson = JsonSerializer.Serialize(proposals, JsonOpts);
        var prompt = $$"""
            You are a highly analytical AI Refiner. Your task is to refine and optimize the proposed AI agent skills.
            For each proposal, you must:
            1. Make the system instruction ("content") highly precise, action-oriented, and include edge cases.
            2. Optimize the trigger keywords list. Ensure they are relevant and specific.

            INPUT PROPOSALS:
            {{proposalsJson}}

            CONVERSATION CONTEXT SAMPLE:
            {{context[..Math.Min(500, context.Length)]}}

            OUTPUT FORMAT (valid JSON only, no markdown fences):
            {
              "proposals": [
                {
                  "type": "skill",
                  "title": "...",
                  "content": "Highly refined system instruction...",
                  "rationale": "...",
                  "trigger_keywords": [...],
                  "confidence": 0.85
                }
              ]
            }
            """;

        try
        {
            // Execute on deepseek (via opencode CLI)
            var raw = await _aiService.ExecuteCliDirectAsync(prompt, "opencode");
            var json = JsonUtils.ExtractJson(raw);
            var root = JsonSerializer.Deserialize<ProposalRoot>(json, JsonOpts);
            return root?.Proposals?
                .Where(p => !string.IsNullOrWhiteSpace(p.Title) && !string.IsNullOrWhiteSpace(p.Content))
                .Select(ToProposal)
                .ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Council] DeepSeek refinement phase failed, using raw proposals");
            return proposals;
        }
    }

    private async Task<List<EvolutionProposal>> GetClaudeValidationAsync(List<EvolutionProposal> proposals, string context)
    {
        var proposalsJson = JsonSerializer.Serialize(proposals.Select(p => new
        {
            p.Type, p.Title, p.Content, p.Rationale,
            trigger_keywords = p.TriggerKeywords, p.Confidence
        }), JsonOpts);

        var prompt = $$"""
            You are a strict security auditor and validator reviewing proposed AI skill evolutions.
            Your job is to audit and approve only high-quality, secure proposals.

            You MUST enforce the following constraints:
            1. PRIVACY & ISOLATION: The skill must NOT instruct agents to access, read, or search directories or files under the "memory/" path. Directly reading from "memory/" violates multi-tenant privacy.
            2. DATABASE SAFETY: The skill must NOT perform raw drop tables, unconstrained deletes, or any operation that could cause SQLite write locks (e.g. SQLite Error 5: 'database is locked').
            3. USEFULNESS: The skill must be genuinely useful and not already covered by general AI capabilities.

            If a proposal violates any security constraint, reject it immediately by setting confidence to 0.0.
            Otherwise, adjust confidence scores based on your assessment. Confidence >= 0.65 to approve.

            CONTEXT SAMPLE:
            {{context[..Math.Min(400, context.Length)]}}

            PROPOSALS TO AUDIT:
            {{proposalsJson}}

            OUTPUT FORMAT (valid JSON only):
            {
              "approved": [
                {
                  "type": "skill",
                  "title": "...",
                  "content": "...(improved and secured if needed)...",
                  "rationale": "...",
                  "trigger_keywords": [...],
                  "confidence": 0.80
                }
              ]
            }

            Return {"approved": []} if no proposals meet safety or quality standards.
            """;

        try
        {
            var raw = await _aiService.ExecuteCliDirectAsync(prompt, "claude");
            var json = JsonUtils.ExtractJson(raw);
            var root = JsonSerializer.Deserialize<ApprovalRoot>(json, JsonOpts);
            return root?.Approved?
                .Where(p => !string.IsNullOrWhiteSpace(p.Title) && p.Confidence >= 0.65f)
                .Select(ToProposal)
                .ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Council] Claude validation failed, using Gemini/DeepSeek proposals with high-confidence filter");
            return proposals.Where(p => p.Confidence >= 0.80f).ToList();
        }
    }

    private static EvolutionProposal ToProposal(ProposalItem p) => new(
        p.Type ?? "skill",
        p.Title ?? "",
        p.Content ?? "",
        p.Rationale ?? "",
        p.TriggerKeywords ?? [],
        p.Confidence);

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private record ProposalRoot(List<ProposalItem>? Proposals);
    private record ApprovalRoot(List<ProposalItem>? Approved);

    private record ProposalItem(
        string? Type,
        string? Title,
        string? Content,
        string? Rationale,
        [property: JsonPropertyName("trigger_keywords")] List<string>? TriggerKeywords,
        float Confidence);
}

