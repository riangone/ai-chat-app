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

    public async Task TryConsolidateAsync(string userMessage, string aiResponse, int userId)
    {
        if (string.IsNullOrWhiteSpace(userMessage) || string.IsNullOrWhiteSpace(aiResponse)) return;

        string extractionPrompt = $$"""
            Extract key facts, user preferences, and important information from the following conversation for long-term memory.
            Include things like name, interests, tech stack, goals, or specific decisions.
            If no new important information is found, return an empty array [].

            CONVERSATION:
            User: {{userMessage}}
            Assistant: {{aiResponse}}

            OUTPUT FORMAT (JSON only):
            [
              {"content": "Description of the fact in the same language as the conversation", "tags": "comma,separated,tags"},
              ...
            ]
            """;

        string provider = _aiService.DefaultProvider;
        string rawJson = await _aiService.ExecuteCliDirectAsync(extractionPrompt, provider);
        _logger.LogDebug("[Memory] Raw {Provider} output ({Length} chars): {Preview}", provider, rawJson.Length, rawJson[..Math.Min(300, rawJson.Length)]);

        rawJson = System.Text.RegularExpressions.Regex.Replace(rawJson, @"```(?:json)?\s*", "").Trim();

        int start = rawJson.IndexOf('[');
        int end = rawJson.LastIndexOf(']');
        if (start < 0 || end < 0 || end <= start)
        {
            _logger.LogDebug("[Memory] No JSON array found. start={Start}, end={End}", start, end);
            return;
        }

        string jsonPart = rawJson[start..(end + 1)];

        try
        {
            var items = JsonSerializer.Deserialize<List<ConsolidationItem>>(jsonPart,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (items == null || !items.Any())
            {
                _logger.LogDebug("[Memory] No items extracted.");
                return;
            }

            var existingMemories = _fileService.GetMemoriesForUser(userId);

            foreach (var item in items)
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
                    await _fileService.WriteAsync(existing);
                }
                else
                {
                    var memory = new LongTermMemory
                    {
                        UserId = userId,
                        Content = item.Content,
                        Tags = item.Tags ?? "general",
                        RelevanceScore = 80,
                        CreatedAt = DateTime.UtcNow,
                        LastAccessedAt = DateTime.UtcNow,
                    };
                    await _fileService.WriteAsync(memory);
                }
            }

            _logger.LogDebug("[Memory] Saved {Count} memory items for userId={UserId}", items.Count, userId);
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

    private record ConsolidationItem(string Content, string Tags);
}
