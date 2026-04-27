using AiChatApp.Models;
using System.Text.Json;

namespace AiChatApp.Services;

public class MemoryConsolidationService
{
    private readonly AiService _aiService;
    private readonly IConfiguration _config;
    private readonly MemoryFileService _fileService;

    public MemoryConsolidationService(AiService aiService, IConfiguration config, MemoryFileService fileService)
    {
        _aiService = aiService;
        _config = config;
        _fileService = fileService;
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

        string provider = _config["MemoryProvider"] ?? "claude";
        string rawJson = await _aiService.ExecuteCliDirectAsync(extractionPrompt, provider);
        Console.WriteLine($"[Memory] Raw {provider} output ({rawJson.Length} chars): {rawJson[..Math.Min(300, rawJson.Length)]}");

        rawJson = System.Text.RegularExpressions.Regex.Replace(rawJson, @"```(?:json)?\s*", "").Trim();

        int start = rawJson.IndexOf('[');
        int end = rawJson.LastIndexOf(']');
        if (start < 0 || end < 0 || end <= start)
        {
            Console.WriteLine($"[Memory] No JSON array found. start={start}, end={end}");
            return;
        }

        string jsonPart = rawJson[start..(end + 1)];

        try
        {
            var items = JsonSerializer.Deserialize<List<ConsolidationItem>>(jsonPart,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (items == null || !items.Any())
            {
                Console.WriteLine("[Memory] No items extracted.");
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

            Console.WriteLine($"[Memory] Saved {items.Count} memory items for userId={userId}");
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[Memory] JSON parse error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Memory] Unexpected error: {ex.Message}");
        }
    }

    private record ConsolidationItem(string Content, string Tags);
}
