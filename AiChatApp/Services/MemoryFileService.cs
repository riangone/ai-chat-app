using AiChatApp.Models;
using System.Text.RegularExpressions;

namespace AiChatApp.Services;

/// <summary>
/// mdファイルを唯一の記憶ストアとして管理する。DBは使用しない。
/// </summary>
public class MemoryFileService
{
    private readonly string _memoryDir;

    public MemoryFileService(IConfiguration config)
    {
        var dir = config["MemoryDir"] ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude", "projects", "-home-ubuntu-ws-ai-chat-app", "memory");
        _memoryDir = dir.Replace("~/", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/");
        Directory.CreateDirectory(_memoryDir);
    }

    public string MemoryDir => _memoryDir;

    // ─── 読み込み ───────────────────────────────────────────────────────────

    /// <summary>指定ユーザーの記憶を全件返す。userId=0のファイルは全ユーザーに共有。</summary>
    public List<LongTermMemory> GetMemoriesForUser(int userId)
    {
        if (!Directory.Exists(_memoryDir)) return [];

        var result = new List<LongTermMemory>();
        foreach (var filePath in Directory.GetFiles(_memoryDir, "*.md"))
        {
            var fileName = Path.GetFileName(filePath);
            if (fileName.Equals("MEMORY.md", StringComparison.OrdinalIgnoreCase)) continue;

            var mem = ParseFile(filePath);
            if (mem == null) continue;
            if (mem.UserId != 0 && mem.UserId != userId) continue;

            result.Add(mem);
        }

        return [.. result.OrderByDescending(m => m.CreatedAt)];
    }

    /// <summary>プロンプトに関連する記憶を多段スコアリングで検索する。</summary>
    public async Task<List<LongTermMemory>> SearchAsync(string prompt, int userId, int maxResults = 5)
    {
        var all = GetMemoriesForUser(userId).Where(m => m.RelevanceScore > 20).ToList();

        var promptWords = prompt
            .Split(new[] { ' ', '　', '、', '。', ',', '.', '!', '?', '\n' },
                   StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant())
            .Where(w => w.Length >= 2)
            .ToHashSet();

        var scored = all.Select(m =>
        {
            var memTags = m.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().ToLowerInvariant()).ToList();
            int score = 0;
            foreach (var tag in memTags)
            {
                if (promptWords.Contains(tag)) score += 30;
                if (prompt.Contains(tag, StringComparison.OrdinalIgnoreCase)) score += 15;
            }
            foreach (var word in promptWords)
                if (m.Content.Contains(word, StringComparison.OrdinalIgnoreCase)) score += 5;
            score = (int)(score * (m.RelevanceScore / 100.0));
            return (Memory: m, Score: score);
        })
        .Where(x => x.Score > 0)
        .OrderByDescending(x => x.Score)
        .Take(maxResults)
        .ToList();

        // キーワードマッチが0件の場合、関連度スコード上位のメモリをフォールバックとして使用
        if (scored.Count == 0)
        {
            var fallback = all
                .OrderByDescending(m => m.RelevanceScore)
                .Take(maxResults)
                .Select(m => (Memory: m, Score: 0))
                .ToList();
            scored = fallback;
        }

        // アクセス数をファイルに更新
        var updateTasks = scored.Select(x =>
        {
            x.Memory.AccessCount++;
            x.Memory.LastAccessedAt = DateTime.UtcNow;
            return WriteAsync(x.Memory);
        });
        await Task.WhenAll(updateTasks);

        return scored.Select(x => x.Memory).ToList();
    }

    // ─── 書き込み ───────────────────────────────────────────────────────────

    /// <summary>記憶をファイルに書き込む。SourceFileが設定されていれば上書き、なければ新規ファイル生成。</summary>
    public async Task WriteAsync(LongTermMemory memory)
    {
        var fileName = memory.SourceFile ?? GenerateFileName(memory.Tags);
        var filePath = Path.Combine(_memoryDir, fileName);

        var shortDesc = memory.Content.Length > 80 ? memory.Content[..80] + "..." : memory.Content;
        var fm = $"""
            ---
            name: {memory.Tags}
            description: {shortDesc}
            type: user
            userId: {memory.UserId}
            tags: {memory.Tags}
            relevanceScore: {memory.RelevanceScore}
            accessCount: {memory.AccessCount}
            createdAt: {memory.CreatedAt:O}
            lastAccessedAt: {memory.LastAccessedAt:O}
            ---

            {memory.Content}
            """;

        await File.WriteAllTextAsync(filePath, fm);
        memory.SourceFile = fileName;
        await RefreshIndexAsync();
    }

    // ─── 削除 ───────────────────────────────────────────────────────────────

    public void DeleteByFileName(string safeFileName)
    {
        if (safeFileName.Contains('/') || safeFileName.Contains('\\') || safeFileName.Contains("..")) return;
        var path = Path.Combine(_memoryDir, safeFileName);
        if (File.Exists(path)) File.Delete(path);
        _ = RefreshIndexAsync();
    }

    // ─── ファイル名生成 ──────────────────────────────────────────────────────

    private static string GenerateFileName(string tags)
    {
        var slug = Regex.Replace(tags.Split(',')[0].Trim().ToLowerInvariant(), @"[^\w]", "_");
        slug = slug.Length > 20 ? slug[..20] : slug;
        var suffix = Guid.NewGuid().ToString("N")[..6];
        return $"mem_{slug}_{suffix}.md";
    }

    // ─── パース ──────────────────────────────────────────────────────────────

    public LongTermMemory? ParseFile(string filePath)
    {
        try
        {
            var text = File.ReadAllText(filePath);
            var match = Regex.Match(text, @"^---\s*\n(.*?)\n---\s*\n?(.*)", RegexOptions.Singleline);

            string fm = "", body = text.Trim();
            if (match.Success)
            {
                fm = match.Groups[1].Value;
                body = match.Groups[2].Value.Trim();
            }

            string Get(string key, string def = "") =>
                Regex.Match(fm, $@"^{key}:\s*(.+)$", RegexOptions.Multiline).Groups[1].Value.Trim() is { Length: > 0 } v ? v : def;

            int GetInt(string key, int def = 0) =>
                int.TryParse(Get(key), out var v) ? v : def;

            DateTime GetDate(string key) =>
                DateTime.TryParse(Get(key), out var v) ? v.ToUniversalTime() : File.GetCreationTimeUtc(filePath);

            var tags = Get("tags");
            if (string.IsNullOrWhiteSpace(tags)) tags = Get("name");
            if (string.IsNullOrWhiteSpace(tags)) tags = Path.GetFileNameWithoutExtension(filePath);

            return new LongTermMemory
            {
                Id = 0,
                UserId = GetInt("userId", 0),
                Content = body,
                Tags = tags,
                RelevanceScore = GetInt("relevanceScore", 80),
                AccessCount = GetInt("accessCount", 0),
                CreatedAt = GetDate("createdAt"),
                LastAccessedAt = GetDate("lastAccessedAt"),
                SourceFile = Path.GetFileName(filePath),
            };
        }
        catch { return null; }
    }

    // ─── インデックス更新 ────────────────────────────────────────────────────

    public async Task RefreshIndexAsync()
    {
        var indexPath = Path.Combine(_memoryDir, "MEMORY.md");
        var files = Directory.GetFiles(_memoryDir, "*.md")
            .Where(f => !Path.GetFileName(f).Equals("MEMORY.md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f);

        var lines = new List<string> { "# Memory Index", "" };
        foreach (var filePath in files)
        {
            var mem = ParseFile(filePath);
            if (mem == null) continue;
            var fileName = Path.GetFileName(filePath);
            var title = mem.Tags.Split(',')[0].Trim();
            var hook = mem.Content.Length > 60 ? mem.Content[..60].Replace('\n', ' ') + "..." : mem.Content.Replace('\n', ' ');
            lines.Add($"- [{title}]({fileName}) — {hook}");
        }

        await File.WriteAllTextAsync(indexPath, string.Join("\n", lines) + "\n");
    }
}
