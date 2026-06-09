using AiChatApp.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiChatApp.Services.Infrastructure;

namespace AiChatApp.Services;

public class MemoryConsolidationService
{
    private readonly AiService _aiService;
    private readonly MemoryFileService _fileService;
    private readonly ILogger<MemoryConsolidationService> _logger;

    public MemoryConsolidationService(AiService aiService, MemoryFileService fileService, ILogger<MemoryConsolidationService> logger)
    {
        _aiService = aiService;
        _fileService = fileService;
        _logger = logger;
    }

    private class UserBatch
    {
        public List<string> Messages { get; } = new();
        public DateTime LastTriggerTime { get; set; } = DateTime.UtcNow;
        public readonly SemaphoreSlim Lock = new(1, 1);
    }

    private static readonly ConcurrentDictionary<int, UserBatch> _userBatches = new();
    private const int TriggerBatchSize = 5;
    private static readonly TimeSpan MaxWaitTime = TimeSpan.FromMinutes(10);
    private const int MaxConsolidationLength = 1000;

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";

    // Skip consolidation when the exchange carries too little information to produce useful memories.
    private static bool IsWorthConsolidating(string userMessage, string aiResponse) =>
        userMessage.Length + aiResponse.Length >= 100 &&
        !Regex.IsMatch(userMessage.Trim(), @"^(ok|okay|yes|no|thanks|thank you|got it|sure|great|good|hi|hello|ありがとう|はい|いいえ|わかった|了解|好的|谢谢|是|否)[\s。！!]*$", RegexOptions.IgnoreCase);

    public async Task TryConsolidateAsync(string userMessage, string aiResponse, int userId)
    {
        if (string.IsNullOrWhiteSpace(userMessage) || string.IsNullOrWhiteSpace(aiResponse)) return;
        if (!IsWorthConsolidating(userMessage, aiResponse)) return;

        var batch = _userBatches.GetOrAdd(userId, _ => new UserBatch());
        
        await batch.Lock.WaitAsync();
        try
        {
            batch.Messages.Add($"User: {Truncate(userMessage, 400)}\nAssistant: {Truncate(aiResponse, 600)}");

            bool shouldTrigger = batch.Messages.Count >= TriggerBatchSize || 
                                (DateTime.UtcNow - batch.LastTriggerTime > MaxWaitTime && batch.Messages.Any());

            if (!shouldTrigger) return;

            var conversationToProcess = string.Join("\n\n---\n\n", batch.Messages);
            batch.Messages.Clear();
            batch.LastTriggerTime = DateTime.UtcNow;

            // Trigger actual consolidation
            await PerformConsolidationAsync(conversationToProcess, userId);

            // Also run Ebbinghaus active consolidation and pruning asynchronously
            _ = Task.Run(async () => {
                try { await ConsolidateAndPruneMemoriesAsync(userId); }
                catch (Exception ex) { _logger.LogError(ex, "ConsolidateAndPruneMemoriesAsync failed"); }
            });
        }
        finally
        {
            batch.Lock.Release();
        }
    }

    private async Task PerformConsolidationAsync(string conversation, int userId)
    {
        string extractionPrompt = $$"""
            Extract key facts, user preferences, and important information from the following conversation for long-term memory.
            Also, extract entities and their relationships to build a knowledge graph (mindmap).
            If no new important information is found, return an empty array [].

            CONVERSATION BATCH:
            {{conversation}}

            OUTPUT FORMAT (JSON only):
            {
              "memories": [
                {"content": "Fact description", "tags": "comma,separated,tags", "relations": ["RelatedEntity1", "RelatedEntity2"]}
              ]
            }
            """;

        string provider = _aiService.DefaultProvider;
        string rawJson = await _aiService.ExecuteCliDirectAsync(extractionPrompt, provider);
        
        string jsonPart = JsonUtils.ExtractJson(rawJson);

        try
        {
            var root = JsonSerializer.Deserialize<ConsolidationRoot>(jsonPart,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (root?.Memories == null || !root.Memories.Any()) return;

            var existingMemories = await _fileService.GetMemoriesForUserAsync(userId);

            var byTag = existingMemories
                .Where(m => !string.IsNullOrWhiteSpace(m.Tags))
                .GroupBy(m => m.Tags.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First());
            
            var byContent = existingMemories
                .Where(m => !string.IsNullOrWhiteSpace(m.Content))
                .GroupBy(m => m.Content.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var item in root.Memories)
            {
                if (string.IsNullOrWhiteSpace(item.Content)) continue;

                byTag.TryGetValue((item.Tags ?? "").ToLowerInvariant(), out var existing);
                existing ??= byContent.GetValueOrDefault(item.Content.ToLowerInvariant());

                if (existing != null)
                {
                    existing.Content = item.Content;
                    existing.LastAccessedAt = DateTime.UtcNow;
                    existing.RelevanceScore = Math.Min(100, existing.RelevanceScore + 5);
                    
                    if (item.Relations != null)
                    {
                        var currentRelations = existing.Relations?.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => r.Trim()) ?? Enumerable.Empty<string>();
                        var newRelations = currentRelations.Union(item.Relations).Distinct();
                        existing.Relations = string.Join(",", newRelations);
                    }
                    await _fileService.WriteAsync(existing);
                }
                else
                {
                    var memory = new LongTermMemory
                    {
                        UserId = userId,
                        Content = item.Content,
                        Tags = item.Tags ?? "general",
                        Relations = item.Relations != null ? string.Join(",", item.Relations) : null,
                        RelevanceScore = 80,
                        CreatedAt = DateTime.UtcNow,
                        LastAccessedAt = DateTime.UtcNow,
                    };
                    await _fileService.WriteAsync(memory);
                }
            }
            _logger.LogInformation("[Memory] Batch consolidation completed for userId={UserId}, saved {Count} items.", userId, root.Memories.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Memory] Batch consolidation error: {Message}", ex.Message);
        }
    }

    private record ConsolidationRoot(List<ConsolidationItem> Memories);
    private record ConsolidationItem(string Content, string Tags, List<string>? Relations);

    public async Task ConsolidateAndPruneMemoriesAsync(int userId)
    {
        try
        {
            var memories = await _fileService.GetMemoriesForUserAsync(userId);
            if (!memories.Any()) return;

            var now = DateTime.UtcNow;
            var updatedMemories = new List<LongTermMemory>();
            var memoriesToDelete = new List<LongTermMemory>();

            // 1. 艾宾浩斯遗忘衰减 (Decay RelevanceScore based on time elapsed since LastAccessedAt)
            foreach (var mem in memories)
            {
                var daysElapsed = (now - mem.LastAccessedAt).TotalDays;
                if (daysElapsed >= 1)
                {
                    // 每天衰减 2 点相关性得分
                    int decayAmount = (int)(daysElapsed * 2);
                    mem.RelevanceScore = Math.Max(0, mem.RelevanceScore - decayAmount);
                    mem.UpdatedAt = now;
                }

                // 相关性过低的记忆被淘汰/归档
                if (mem.RelevanceScore < 20 && !string.IsNullOrEmpty(mem.SourceFile))
                {
                    memoriesToDelete.Add(mem);
                }
                else
                {
                    updatedMemories.Add(mem);
                }
            }

            // 执行物理删除
            foreach (var mem in memoriesToDelete)
            {
                _logger.LogInformation("[MemoryConsolidation] Pruning memory file {File} due to low relevance score ({Score})", mem.SourceFile, mem.RelevanceScore);
                await _fileService.DeleteByFileNameAsync(mem.SourceFile!, userId);
            }

            // 保存衰减后的记忆
            foreach (var mem in updatedMemories)
            {
                await _fileService.WriteAsync(mem);
            }

            // 2. 主动记忆整理 (Merge similar/conflicting memories)
            if (updatedMemories.Count > 3)
            {
                await PerformActiveConsolidationAsync(updatedMemories, userId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MemoryConsolidation] Active pruning and consolidation failed: {Message}", ex.Message);
        }
    }

    private async Task PerformActiveConsolidationAsync(List<LongTermMemory> activeMemories, int userId)
    {
        // 提取相似或容易冲突的记忆，送给 LLM 合并
        var groups = activeMemories
            .Where(m => !string.IsNullOrEmpty(m.Tags))
            .GroupBy(m => m.Tags.Split(',')[0].Trim().ToLowerInvariant())
            .Where(g => g.Count() > 1) // 相同主要标签有多条
            .ToList();

        foreach (var group in groups)
        {
            var memoriesToMerge = group.ToList();
            var memoryTexts = string.Join("\n\n", memoriesToMerge.Select(m => $"- [File: {m.SourceFile}] {m.Content}"));

            string mergePrompt = $$"""
                You are a Memory Consolidator Agent. Your task is to merge and resolve conflicts among multiple fragments of long-term memories under the tag/topic "{{group.Key}}".
                
                Combine them into a single, clean, structured facts list or unified memory text. Resolve any factual contradictions by prioritizing newer or more complete info.
                
                MEMORIES TO MERGE:
                {{memoryTexts}}
                
                OUTPUT FORMAT (Strict JSON only):
                {
                  "mergedContent": "Fully consolidated facts and narrative. No duplicate statements.",
                  "relations": ["Entity1", "Entity2"]
                }
                """;

            try
            {
                string provider = _aiService.DefaultProvider;
                string rawJson = await _aiService.ExecuteCliDirectAsync(mergePrompt, provider);
                string jsonPart = JsonUtils.ExtractJson(rawJson);

                using var doc = JsonDocument.Parse(jsonPart);
                var root = doc.RootElement;
                if (root.TryGetProperty("mergedContent", out var contentProp))
                {
                    var mergedContent = contentProp.GetString();
                    if (!string.IsNullOrWhiteSpace(mergedContent))
                    {
                        var relations = new List<string>();
                        if (root.TryGetProperty("relations", out var relProp))
                        {
                            foreach (var item in relProp.EnumerateArray())
                            {
                                relations.Add(item.GetString() ?? "");
                            }
                        }

                        // 保存合并后的第一个记忆，删除多余的记忆
                        var primary = memoriesToMerge[0];
                        primary.Content = mergedContent;
                        primary.Relations = relations.Any() ? string.Join(",", relations.Distinct()) : primary.Relations;
                        primary.RelevanceScore = Math.Min(100, primary.RelevanceScore + 10);
                        primary.LastAccessedAt = DateTime.UtcNow;
                        await _fileService.WriteAsync(primary);

                        for (int i = 1; i < memoriesToMerge.Count; i++)
                        {
                            if (!string.IsNullOrEmpty(memoriesToMerge[i].SourceFile))
                            {
                                await _fileService.DeleteByFileNameAsync(memoriesToMerge[i].SourceFile!, userId);
                            }
                        }

                        _logger.LogInformation("[MemoryConsolidation] Successfully merged {Count} memories under tag '{Tag}' into '{File}'", memoriesToMerge.Count, group.Key, primary.SourceFile);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[MemoryConsolidation] Error merging group '{Tag}': {Message}", group.Key, ex.Message);
            }
        }
    }
}
