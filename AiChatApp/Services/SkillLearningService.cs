using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using AiChatApp.Data;
using AiChatApp.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using AiChatApp.Services.Infrastructure;

namespace AiChatApp.Services
{
    public class SkillLearningService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SkillLearningService> _logger;

        public SkillLearningService(IServiceProvider serviceProvider, ILogger<SkillLearningService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>
        /// 評価済みのエージェントステップから、新しいスキルを自動的に抽出して学習します。
        /// </summary>
        public async Task TryExtractSkillFromStepAsync(int agentStepId)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var aiService = scope.ServiceProvider.GetRequiredService<AiService>();

            try
            {
                // 1. エージェントステップと評価値、およびユーザーIDを取得
                var step = await db.AgentSteps
                    .Include(s => s.Evaluations)
                    .Include(s => s.Message)
                    .FirstOrDefaultAsync(s => s.Id == agentStepId);

                if (step == null || step.Message == null)
                {
                    _logger.LogWarning("[SkillLearning] Step {StepId} or message not found.", agentStepId);
                    return;
                }

                var session = await db.ChatSessions.FindAsync(step.Message.ChatSessionId);
                if (session == null)
                {
                    _logger.LogWarning("[SkillLearning] Chat session {SessionId} not found.", step.Message.ChatSessionId);
                    return;
                }

                int userId = session.UserId;

                // 2. 評価の品質チェック（AccuracyとHelpfulnessの平均スコアが0.8以上、または全体平均が0.8以上の場合のみ学習）
                if (step.Evaluations == null || !step.Evaluations.Any())
                {
                    _logger.LogInformation("[SkillLearning] Step {StepId} has no evaluations yet. Skipping skill extraction.", agentStepId);
                    return;
                }

                var keyScores = step.Evaluations
                    .Where(e => e.Criteria == "Accuracy" || e.Criteria == "Helpfulness")
                    .Select(e => e.Score)
                    .ToList();

                double avgScore = step.Evaluations.Average(e => e.Score);
                double keyAvgScore = keyScores.Any() ? keyScores.Average() : avgScore;

                if (keyAvgScore < 0.8 && avgScore < 0.8)
                {
                    _logger.LogInformation("[SkillLearning] Step {StepId} quality scores (avg: {Avg}, keyAvg: {KeyAvg}) are below threshold (0.8). Skipping extraction.", agentStepId, avgScore, keyAvgScore);
                    return;
                }

                _logger.LogInformation("[SkillLearning] High-quality step {StepId} detected (avg: {Avg}). Initiating AI skill extraction...", agentStepId, avgScore);

                // 3. AIにプロンプトを投げて、再利用可能な汎用スキルを抽出する
                string extractionPrompt = $$"""
                    Analyze the following successful execution step of an AI Agent.
                    Determine if this step demonstrates a generic, reusable strategy, template, or skill (e.g., specific code refactoring patterns, specialized tool usage, file search steps, custom CLI workarounds) that could benefit other agents or future prompts in this project.

                    If a reusable skill exists, extract it into a structured system instruction.
                    If the response is generic chat, trivial, or too task-specific to be reused, return `{"shouldCreate": false}`.

                    AGENT ROLE: {{step.Role}}
                    SYSTEM PROMPT IN USE:
                    {{step.Persona}}

                    USER INPUT TO AGENT:
                    {{step.Input}}

                    AGENT SUCCESSFUL OUTPUT:
                    {{step.Output}}

                    OUTPUT FORMAT (Strict JSON only):
                    {
                      "shouldCreate": true,
                      "name": "camel-case-identifier (e.g., dotnet-ef-migration-helper)",
                      "displayName": "Short Descriptive Title (e.g., EF Core Migration Assistant)",
                      "description": "System instructions that describe when and how to apply this skill/strategy. These will be injected into system prompts when trigger keywords match. Keep it actionable.",
                      "triggerKeywords": "comma-separated-keywords (e.g., ef migration, dotnet database, add-migration)"
                    }
                    """;

                string provider = aiService.DefaultProvider;
                string rawJson = await aiService.ExecuteCliDirectAsync(extractionPrompt, provider);
                string jsonPart = JsonUtils.ExtractJson(rawJson);

                if (string.IsNullOrWhiteSpace(jsonPart))
                {
                    _logger.LogWarning("[SkillLearning] LLM returned empty json for step {StepId}.", agentStepId);
                    return;
                }

                var extraction = JsonSerializer.Deserialize<SkillExtractionResult>(jsonPart, 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (extraction == null || !extraction.ShouldCreate || string.IsNullOrWhiteSpace(extraction.Name))
                {
                    _logger.LogInformation("[SkillLearning] LLM decided no reusable skill should be created for step {StepId}.", agentStepId);
                    return;
                }

                // 4. 重複チェック
                var existingSkill = await db.Skills.FirstOrDefaultAsync(s => s.UserId == userId && s.Name.ToLower() == extraction.Name.ToLower());
                if (existingSkill != null)
                {
                    _logger.LogInformation("[SkillLearning] Skill '{SkillName}' already exists. Updating its metrics instead.", extraction.Name);
                    existingSkill.SuccessCount++;
                    await db.SaveChangesAsync();
                    return;
                }

                // 5. 新規スキル登録（IsAutoGenerated=true, IsApproved=false でユーザー承認待ちとする）
                var newSkill = new Skill
                {
                    UserId = userId,
                    Name = extraction.Name,
                    Description = extraction.Description,
                    TriggerKeywords = extraction.TriggerKeywords ?? "",
                    IsEnabled = true,
                    IsAutoGenerated = true,
                    IsApproved = false, // ユーザーの承認待ち
                    UseCount = 0,
                    SuccessCount = 1,
                    LastUsedAt = DateTime.UtcNow
                };

                db.Skills.Add(newSkill);
                await db.SaveChangesAsync();

                _logger.LogInformation("[SkillLearning] Successfully extracted new auto-generated skill '{SkillName}' (ID: {SkillId}) from step {StepId}. Pending approval.", newSkill.Name, newSkill.Id, agentStepId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TryExtractSkillFromStepAsync for step {StepId}", agentStepId);
            }
        }

        private class SkillExtractionResult
        {
            public bool ShouldCreate { get; set; }
            public string Name { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string? TriggerKeywords { get; set; }
        }

        /// <summary>
        /// スキルの使用メトリクスを更新します。
        /// </summary>
        public async Task UpdateSkillMetricsAsync(int skillId, bool isUsed)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            try
            {
                var skill = await db.Skills.FindAsync(skillId);
                if (skill != null)
                {
                    if (isUsed)
                    {
                        skill.UseCount++;
                        skill.LastUsedAt = DateTime.UtcNow;
                    }
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update metrics for skill {SkillId}", skillId);
            }
        }

        /// <summary>
        /// スキルの成功統計を更新します。
        /// </summary>
        public async Task RecordSkillSuccessAsync(int skillId, bool success)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            try
            {
                var skill = await db.Skills.FindAsync(skillId);
                if (skill != null)
                {
                    if (success) skill.SuccessCount++;
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record success for skill {SkillId}", skillId);
            }
        }
    }
}
