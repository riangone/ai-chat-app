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

    // Per-user cooldown: each user triggers consolidation at most once per interval
    private static readonly ConcurrentDictionary<int, DateTime> _lastConsolidationTime = new();
    private static readonly TimeSpan ConsolidationMinInterval = TimeSpan.FromMinutes(5);
    private const int MaxConsolidationLength = 800;

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";

    public async Task TryConsolidateAsync(string userMessage, string aiResponse, int userId)
    {
        if (string.IsNullOrWhiteSpace(userMessage) || string.IsNullOrWhiteSpace(aiResponse)) return;
        if (userMessage.Length + aiResponse.Length < 100) return;

        if (_lastConsolidationTime.TryGetValue(userId, out var last) &&
            DateTime.UtcNow - last < ConsolidationMinInterval) return;
        _lastConsolidationTime[userId] = DateTime.UtcNow;

        var truncatedUser = Truncate(userMessage, MaxConsolidationLength);
        var truncatedAi   = Truncate(aiResponse,  MaxConsolidationLength);

        string extractionPrompt = $$"""
            Extract key facts, user preferences, and important information from the following conversation for long-term memory.
            Also, extract entities and their relationships to build a knowledge graph (mindmap).
            Include things like name, interests, tech stack, goals, or specific decisions.
            If no new important information is found, return an empty array [].

            CONVERSATION:
            User: {{truncatedUser}}
            Assistant: {{truncatedAi}}

            OUTPUT FORMAT (JSON only):
            {
              "memories": [
                {"content": "Fact description", "tags": "comma,separated,tags", "relations": ["RelatedEntity1", "RelatedEntity2"]}
              ]
            }
            """;

        string provider = _aiService.DefaultProvider;
        string rawJson = await _aiService.ExecuteCliDirectAsync(extractionPrompt, provider);
        _logger.LogDebug("[Memory] Raw {Provider} output ({Length} chars): {Preview}", provider, rawJson.Length, rawJson[..Math.Min(300, rawJson.Length)]);

        string jsonPart = JsonUtils.ExtractJson(rawJson);

        try
        {
            var root = JsonSerializer.Deserialize<ConsolidationRoot>(jsonPart,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (root?.Memories == null || !root.Memories.Any())
            {
                _logger.LogDebug("[Memory] No items extracted.");
                return;
            }

            var existingMemories = await _fileService.GetMemoriesForUserAsync(userId);

            // O(1) lookup: index by normalised tag and by normalised content
            var byTag     = existingMemories
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
                    
                    // Merge relations
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

            _logger.LogDebug("[Memory] Saved {Count} memory items for userId={UserId}", root.Memories.Count, userId);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[Memory] JSON parse error: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Memory] Unexpected error: {Message}", ex.Message);
        }
    }

    private record ConsolidationRoot(List<ConsolidationItem> Memories);
    private record ConsolidationItem(string Content, string Tags, List<string>? Relations);
}
