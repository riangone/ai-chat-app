using AiChatApp.Data;
using AiChatApp.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AiChatApp.Services;

public class MemoryConsolidationService
{
    private readonly AppDbContext _db;
    private readonly AiService _ai;

    public MemoryConsolidationService(AppDbContext db, AiService ai)
    {
        _db = db;
        _ai = ai;
    }

    /// <summary>
    /// 1ターン終了後に呼び出す。AIが会話から記憶すべき事実を抽出し、LongTermMemoryに保存する。
    /// 抽出なしの場合は何もしない（無駄なDB書き込みを避ける）。
    /// </summary>
    public async Task TryConsolidateAsync(string userMessage, string aiResponse, int userId)
    {
        // 抽出プロンプト：JSON形式で返させる
        string extractionPrompt = $$"""
            以下の会話から、ユーザーについて長期的に記憶すべき「事実」「好み」「重要情報」を抽出してください。
            抽出できる情報がない場合は空配列を返してください。

            会話:
            ユーザー: {{userMessage}}
            AI: {{aiResponse}}

            以下のJSON形式のみで回答してください（説明不要）:
            [
              {"content": "事実の内容", "tags": "タグ1,タグ2,タグ3"},
              ...
            ]
            """;

        string rawJson = await _ai.ExecuteCliDirectAsync(extractionPrompt, "gemini");

        // JSONブロックを抽出（AIがマークダウンで包む場合に対応）
        int start = rawJson.IndexOf('[');
        int end = rawJson.LastIndexOf(']');
        if (start < 0 || end < 0 || end <= start) return;

        string jsonPart = rawJson[start..(end + 1)];

        try
        {
            var items = JsonSerializer.Deserialize<List<ConsolidationItem>>(jsonPart);
            if (items == null || !items.Any()) return;

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Content)) continue;

                // 既存の类似记忆与重复检查（标签完全匹配的记忆将覆盖）
                var existing = await _db.LongTermMemories
                    .FirstOrDefaultAsync(m => m.UserId == userId && m.Tags == item.Tags);

                if (existing != null)
                {
                    existing.Content = item.Content;
                    existing.LastAccessedAt = DateTime.UtcNow;
                }
                else
                {
                    _db.LongTermMemories.Add(new LongTermMemory
                    {
                        UserId = userId,
                        Content = item.Content,
                        Tags = item.Tags ?? "",
                        RelevanceScore = 80
                    });
                }
            }

            await _db.SaveChangesAsync();
        }
        catch (JsonException)
        {
            // AIが不正なJSONを返した場合は無視
        }
    }

    private record ConsolidationItem(string Content, string Tags);
}
