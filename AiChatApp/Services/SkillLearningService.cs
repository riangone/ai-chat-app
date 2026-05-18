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
