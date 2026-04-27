using AiChatApp.Data;
using AiChatApp.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AiChatApp.Services;

public class MemoryConsolidationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AiService _aiService;
    private readonly IConfiguration _config;
    private readonly MemoryFileService _fileService;

    public MemoryConsolidationService(IServiceScopeFactory scopeFactory, AiService aiService, IConfiguration config, MemoryFileService fileService)
    {
        _scopeFactory = scopeFactory;
        _aiService = aiService;
        _config = config;
        _fileService = fileService;
    }

    /// <summary>
    /// 1ターン終了後に呼び出す。AIが会話から記憶すべき事実を抽出し、LongTermMemoryに保存する。
    /// </summary>
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

        // markdownコードブロック除去
        rawJson = System.Text.RegularExpressions.Regex.Replace(rawJson, @"```(?:json)?\s*", "").Trim();

        // JSONブロックを抽出
        int start = rawJson.IndexOf('[');
        int end = rawJson.LastIndexOf(']');
        if (start < 0 || end < 0 || end <= start)
        {
            Console.WriteLine($"[Memory] No JSON array found in output. start={start}, end={end}");
            return;
        }

        string jsonPart = rawJson[start..(end + 1)];
        Console.WriteLine($"[Memory] Parsed JSON: {jsonPart[..Math.Min(200, jsonPart.Length)]}");

        try
        {
            var items = JsonSerializer.Deserialize<List<ConsolidationItem>>(jsonPart, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (items == null || !items.Any())
            {
                Console.WriteLine("[Memory] Empty or null items after deserialization.");
                return;
            }

            // 新しいスコープを作成してDbContextを取得（fire-and-forgetでも安全に使用できる）
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Content)) continue;

                var existing = await db.LongTermMemories
                    .FirstOrDefaultAsync(m => m.UserId == userId && (m.Tags == item.Tags || m.Content == item.Content));

                if (existing != null)
                {
                    existing.Content = item.Content;
                    existing.LastAccessedAt = DateTime.UtcNow;
                    existing.RelevanceScore = Math.Min(100, existing.RelevanceScore + 5);
                }
                else
                {
                    db.LongTermMemories.Add(new LongTermMemory
                    {
                        UserId = userId,
                        Content = item.Content,
                        Tags = item.Tags ?? "general",
                        RelevanceScore = 80,
                        CreatedAt = DateTime.UtcNow,
                        LastAccessedAt = DateTime.UtcNow
                    });
                }
            }

            await db.SaveChangesAsync();
            Console.WriteLine($"[Memory] Saved {items.Count} memory items for userId={userId}");

            // ファイルにも同期
            foreach (var mem in db.LongTermMemories.Where(m => m.UserId == userId).OrderByDescending(m => m.Id).Take(items.Count))
                await _fileService.WriteAsync(mem);
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
