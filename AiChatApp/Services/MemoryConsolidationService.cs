using AiChatApp.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

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

    private int _consolidationCounter;
    private const int ConsolidationSampleRate = 10;

    public async Task TryConsolidateAsync(string userMessage, string aiResponse, int userId)
    {
        if (string.IsNullOrWhiteSpace(userMessage) || string.IsNullOrWhiteSpace(aiResponse)) return;
        if (userMessage.Length + aiResponse.Length < 100) return;

        Interlocked.Increment(ref _consolidationCounter);
        if (_consolidationCounter % ConsolidationSampleRate != 0) return;

        string extractionPrompt = $$"""
            Extract key facts, user preferences, and important information from the following conversation for long-term memory.
            Also, extract entities and their relationships to build a knowledge graph (mindmap).
            Include things like name, interests, tech stack, goals, or specific decisions.
            If no new important information is found, return an empty array [].

            CONVERSATION:
            User: {{userMessage}}
            Assistant: {{aiResponse}}

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

        rawJson = System.Text.RegularExpressions.Regex.Replace(rawJson, @"```(?:json)?\s*", "").Trim();

        int start = rawJson.IndexOf('{');
        int end = rawJson.LastIndexOf('}');
        if (start < 0 || end < 0 || end <= start)
        {
            _logger.LogDebug("[Memory] No JSON object found. start={Start}, end={End}", start, end);
            return;
        }

        string jsonPart = rawJson[start..(end + 1)];

        try
        {
            var root = JsonSerializer.Deserialize<ConsolidationRoot>(jsonPart,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (root?.Memories == null || !root.Memories.Any())
            {
                _logger.LogDebug("[Memory] No items extracted.");
                return;
            }

            var existingMemories = _fileService.GetMemoriesForUser(userId);

            foreach (var item in root.Memories)
            {
                if (string.IsNullOrWhiteSpace(item.Content)) continue;

                var existing = existingMemories.FirstOrDefault(m =>
                    m.Tags.Equals(item.Tags, StringComparison.OrdinalIgnoreCase) ||
                    m.Content.Equals(item.Content, StringComparison.OrdinalIgnoreCase));

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
