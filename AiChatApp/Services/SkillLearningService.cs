using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using AiChatApp.Data;
using AiChatApp.Models;

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
        /// AIのインタラクション結果から学習し、スキルの統計を更新します。
        /// </summary>
        public async Task LearnFromInteractionAsync(string taskDescription, string finalResult, List<AgentStep> steps, int userId)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            try
            {
                _logger.LogInformation("Learning from interaction for user {UserId}. Task: {Task}", userId, taskDescription);

                // インタラクションが成功したかどうかを簡易的に判断 (Reviewerがいる場合や、QualityCheckをパスした場合など)
                bool isSuccess = steps.All(s => s.WasAccepted);

                // 直近に使用されたスキル（検索結果に含まれていたもの）の統計を更新
                // ここでは AiService 側で個別に UpdateSkillMetricsAsync が呼ばれることを想定し、
                // 全体的な成功・失敗のフィードバックのみを行う
                
                // 本来は、どのタスクでどのスキルが使われ、それが成功に寄与したかを分析するロジックをここに書く
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during skill learning process");
            }
            await Task.CompletedTask;
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
