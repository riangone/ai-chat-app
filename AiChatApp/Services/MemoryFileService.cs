using AiChatApp.Models;
using System.Text.RegularExpressions;

namespace AiChatApp.Services;

/// <summary>
/// mdファイルを唯一の記憶ストアとして管理する。DBは使用しない。
/// </summary>
public class MemoryFileService : IDisposable
{
    private readonly string _memoryDir;
    private List<LongTermMemory>? _cache;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private readonly FileSystemWatcher? _watcher;

    public MemoryFileService(IConfiguration config)
    {
        var dir = config["MemoryDir"];
        if (string.IsNullOrEmpty(dir))
        {
            dir = Path.Combine(AppContext.BaseDirectory, "memory");
        }

        if (!Path.IsPathRooted(dir))
        {
            dir = Path.GetFullPath(dir, Directory.GetCurrentDirectory());
        }

        _memoryDir = dir.Replace("~/", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/");
        Directory.CreateDirectory(_memoryDir);

        try
        {
            _watcher = new FileSystemWatcher(_memoryDir, "*.md")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Deleted += OnFileChanged;
            _watcher.Renamed += OnFileChanged;
        }
        catch { /* Watcher might fail in some environments */ }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (e.Name?.Equals("MEMORY.md", StringComparison.OrdinalIgnoreCase) == true) return;
        _cacheLock.Wait();
        try
        {
            _cache = null; // Invalidate cache
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task EnsureCacheLoadedAsync()
    {
        if (_cache != null) return;
        await _cacheLock.WaitAsync();
        try
        {
            if (_cache != null) return;
            var result = new List<LongTermMemory>();
            if (Directory.Exists(_memoryDir))
            {
                foreach (var filePath in Directory.GetFiles(_memoryDir, "*.md"))
                {
                    var fileName = Path.GetFileName(filePath);
                    if (fileName.Equals("MEMORY.md", StringComparison.OrdinalIgnoreCase)) continue;

                    var mem = await ParseFileAsync(filePath);
                    if (mem != null) result.Add(mem);
                }
            }
            _cache = result;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public string MemoryDir => _memoryDir;

    // ─── 読み込み ───────────────────────────────────────────────────────────

    /// <summary>指定ユーザーの記憶を全件返す。userId=0のファイルは全ユーザーに共有。</summary>
    public async Task<List<LongTermMemory>> GetMemoriesForUserAsync(int userId)
    {
        await EnsureCacheLoadedAsync();

        return _cache!
            .Where(m => m.UserId == 0 || m.UserId == userId)
            .OrderByDescending(m => m.CreatedAt)
            .ToList();
    }

    private const int MaxScoringCandidates = 150;

    /// <summary>プロンプトに関連する記憶を多段スコアリングで検索する。</summary>
    public async Task<List<LongTermMemory>> SearchAsync(string prompt, int userId, int maxResults = 5)
    {
        // Cap candidates before the O(n) scoring loop to prevent unbounded CPU growth.
        var all = (await GetMemoriesForUserAsync(userId))
            .Where(m => m.RelevanceScore > 20)
            .OrderByDescending(m => m.RelevanceScore)
            .Take(MaxScoringCandidates)
            .ToList();

        // 单词分割（主要针对英文）
        var wordTokens = prompt
            .Split(new[] { ' ', '　', '、', '。', ',', '.', '!', '?', '\n', '\r', '\t' },
                   StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant())
            .Where(w => w.Length >= 2)
            .ToHashSet();

        // 字符级分割（针对中日韩等无空格语言）
        var charTokens = new HashSet<string>();
        if (prompt.Any(c => c > 127)) // 包含非 ASCII 字符
        {
            for (int i = 0; i < prompt.Length - 1; i++)
            {
                if (!char.IsWhiteSpace(prompt[i]))
                    charTokens.Add(prompt.Substring(i, 1));
                
                // 二元语法 (Bigram) 提高精准度
                if (!char.IsWhiteSpace(prompt[i]) && !char.IsWhiteSpace(prompt[i+1]))
                    charTokens.Add(prompt.Substring(i, 2));
            }
        }

        var scored = all.Select(m =>
        {
            var memTags = m.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().ToLowerInvariant()).ToList();
            int score = 0;

            // 标签匹配 (权重最高)
            foreach (var tag in memTags)
            {
                if (wordTokens.Contains(tag)) score += 50;
                if (charTokens.Contains(tag)) score += 40;
                if (prompt.Contains(tag, StringComparison.OrdinalIgnoreCase)) score += 30;
            }

            // 内容匹配
            foreach (var word in wordTokens)
                if (m.Content.Contains(word, StringComparison.OrdinalIgnoreCase)) score += 10;
            
            foreach (var token in charTokens)
                if (token.Length > 1 && m.Content.Contains(token, StringComparison.OrdinalIgnoreCase)) score += 5;

            // 基础相关度加权
            score = (int)(score * (m.RelevanceScore / 100.0));
            
            // 访问次数加成
            score += Math.Min(m.AccessCount, 20);

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

        // アクセス数をキャッシュのみ更新（ディスクIO削減）
        foreach (var x in scored)
        {
            x.Memory.AccessCount++;
            x.Memory.LastAccessedAt = DateTime.UtcNow;
        }

        return scored.Select(x => x.Memory).ToList();
    }

    // ─── 書き込み ───────────────────────────────────────────────────────────

    /// <summary>記憶をファイルに書き込む。SourceFileが設定されていれば上書き、なければ新規ファイル生成。</summary>
    public async Task WriteAsync(LongTermMemory memory)
    {
        await EnsureCacheLoadedAsync();

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
            relations: {memory.Relations}
            relevanceScore: {memory.RelevanceScore}
            accessCount: {memory.AccessCount}
            createdAt: {memory.CreatedAt:O}
            lastAccessedAt: {memory.LastAccessedAt:O}
            ---

            {memory.Content}
            """;

        await File.WriteAllTextAsync(filePath, fm);
        memory.SourceFile = fileName;

        // キャッシュ更新
        await _cacheLock.WaitAsync();
        try
        {
            if (_cache != null && !_cache.Any(m => m.SourceFile == fileName))
            {
                _cache.Add(memory);
            }
        }
        finally
        {
            _cacheLock.Release();
        }

        await RefreshIndexAsync();
    }

    // ─── 削除 ───────────────────────────────────────────────────────────────

    public async Task DeleteByFileNameAsync(string safeFileName)
    {
        if (safeFileName.Contains('/') || safeFileName.Contains('\\') || safeFileName.Contains("..")) return;
        var path = Path.Combine(_memoryDir, safeFileName);
        if (File.Exists(path)) File.Delete(path);

        // キャッシュから削除
        await _cacheLock.WaitAsync();
        try
        {
            _cache?.RemoveAll(m => m.SourceFile == safeFileName);
        }
        finally
        {
            _cacheLock.Release();
        }

        await RefreshIndexAsync();
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

    public async Task<LongTermMemory?> ParseFileAsync(string filePath)
    {
        try
        {
            var text = await File.ReadAllTextAsync(filePath);
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

            var relations = Get("relations");

            return new LongTermMemory
            {
                Id = 0,
                UserId = GetInt("userId", 0),
                Content = body,
                Tags = tags,
                Relations = string.IsNullOrWhiteSpace(relations) ? null : relations,
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
        await EnsureCacheLoadedAsync();
        if (_cache == null) return;

        var indexPath = Path.Combine(_memoryDir, "MEMORY.md");
        var lines = new List<string> { "# Memory Index", "" };

        // キャッシュを使用してインデックス生成（ファイル再読み込み不要）
        var sorted = _cache.OrderBy(m => m.SourceFile).ToList();
        foreach (var mem in sorted)
        {
            var title = mem.Tags.Split(',')[0].Trim();
            var hook = mem.Content.Length > 60 ? mem.Content[..60].Replace('\n', ' ') + "..." : mem.Content.Replace('\n', ' ');
            lines.Add($"- [{title}]({mem.SourceFile}) — {hook}");
        }

        await File.WriteAllTextAsync(indexPath, string.Join("\n", lines) + "\n");
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _cacheLock?.Dispose();
    }
}
