using AiChatApp.Data;
using AiChatApp.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace AiChatApp.Services;

public class SessionMemoryService
{
    private readonly AppDbContext _db;

    public SessionMemoryService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// エージェントがメモリに書き込む
    /// </summary>
    public async Task WriteAsync(int sessionId, string agentName, string key, string value)
    {
        var entry = await _db.SessionMemories
            .FirstOrDefaultAsync(m => m.ChatSessionId == sessionId && m.Key == key);

        if (entry != null)
        {
            entry.Value = value;
            entry.AgentName = agentName;
            entry.CreatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.SessionMemories.Add(new SessionMemory
            {
                ChatSessionId = sessionId,
                AgentName = agentName,
                Key = key,
                Value = value
            });
        }

        await _db.SaveChangesAsync();
    }

    private const int SessionMemoryContextLimit = 30;

    /// <summary>
    /// 全エージェントからメモリを読む（プロンプト注入用）。最新 30 件のみ取得してコンテキスト肥大を防ぐ。
    /// </summary>
    public async Task<string> ReadAllAsContextAsync(int sessionId)
    {
        var memories = await _db.SessionMemories
            .Where(m => m.ChatSessionId == sessionId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(SessionMemoryContextLimit)
            .ToListAsync();

        if (memories.Count == 0) return "";

        memories.Reverse(); // chronological order for readability
        var sb = new StringBuilder("\n[共有セッションメモリ]:\n");
        foreach (var m in memories)
        {
            sb.AppendLine($"- {m.Key}: {m.Value} (by {m.AgentName})");
        }

        return sb.ToString();
    }

    private static readonly HashSet<string> LowValueKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "status", "ok", "done", "acknowledged", "progress", "step", "result_summary", "note"
    };

    public async Task PromoteToLongTermAsync(int sessionId, int userId)
    {
        var memories = await _db.SessionMemories
            .Where(m => m.ChatSessionId == sessionId && !m.IsPromoted)
            .ToListAsync();

        foreach (var m in memories)
        {
            if (LowValueKeys.Contains(m.Key)) continue;
            if (string.IsNullOrWhiteSpace(m.Value) || m.Value.Length < 10) continue;

            _db.LongTermMemories.Add(new LongTermMemory
            {
                UserId = userId,
                Content = $"[Session:{sessionId}] {m.Key}: {m.Value}"
            });
            m.IsPromoted = true;
        }

        await _db.SaveChangesAsync();
    }
}
