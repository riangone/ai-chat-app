using AiChatApp.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
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

    public async Task TryConsolidateAsync(string userMessage, string aiResponse, int userId)
    {
        if (string.IsNullOrWhiteSpace(userMessage) || string.IsNullOrWhiteSpace(aiResponse)) return;

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
}
