using AiChatApp.Models;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;

namespace AiChatApp.Services;

/// <summary>
/// 各ユーザーごとのサブディレクトリを用いて、mdファイルを物理的・論理的に隔離して管理する記憶ストア。
/// </summary>
public class MemoryFileService : IDisposable
{
    private readonly string _memoryDir;
    private readonly ConcurrentDictionary<int, List<LongTermMemory>> _userCaches = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _userLocks = new();

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

        // 旧バージョンのフラットな記憶ファイルを、ユーザー別の物理サブディレクトリへ移行
        MigrateOldMemories();
    }

    private SemaphoreSlim GetUserLock(int userId)
    {
        return _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
    }

    private void MigrateOldMemories()
    {
        try
        {
            if (!Directory.Exists(_memoryDir)) return;

            foreach (var filePath in Directory.GetFiles(_memoryDir, "*.md"))
            {
                var fileName = Path.GetFileName(filePath);
                if (fileName.Equals("MEMORY.md", StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(filePath);
                    continue;
                }

                var text = File.ReadAllText(filePath);
                var match = Regex.Match(text, @"^---\s*\n(.*?)\n---\s*\n?(.*)", RegexOptions.Singleline);
                if (!match.Success) continue;

                var fm = match.Groups[1].Value;
                string Get(string key) =>
                    Regex.Match(fm, $@"^{key}:\s*(.+)$", RegexOptions.Multiline).Groups[1].Value.Trim() is { Length: > 0 } v ? v : "";

                int.TryParse(Get("userId"), out var userId);

                var targetDir = userId == 0 
                    ? Path.Combine(_memoryDir, "shared") 
                    : Path.Combine(_memoryDir, $"user_{userId}");

                Directory.CreateDirectory(targetDir);
                var targetPath = Path.Combine(targetDir, fileName);

                if (!File.Exists(targetPath))
                {
                    File.Move(filePath, targetPath);
                }
                else
                {
                    File.Delete(filePath);
                }
            }
        }
        catch { /* 静かに無視 */ }
    }

    private async Task EnsureUserCacheLoadedAsync(int userId)
    {
        if (_userCaches.ContainsKey(userId)) return;

        var userLock = GetUserLock(userId);
        await userLock.WaitAsync();
        try
        {
            if (_userCaches.ContainsKey(userId)) return;

            var result = new List<LongTermMemory>();

            // 1. 共有メモリのロード (shared/)
            var sharedDir = Path.Combine(_memoryDir, "shared");
            if (Directory.Exists(sharedDir))
            {
                foreach (var filePath in Directory.GetFiles(sharedDir, "*.md"))
                {
                    var fileName = Path.GetFileName(filePath);
                    if (fileName.Equals("MEMORY.md", StringComparison.OrdinalIgnoreCase)) continue;

                    var mem = await ParseFileAsync(filePath);
                    if (mem != null)
                    {
                        mem.UserId = 0;
                        result.Add(mem);
                    }
                }
            }

            // 2. ユーザー個別メモリのロード (user_{userId}/)
            var userDir = Path.Combine(_memoryDir, $"user_{userId}");
            if (Directory.Exists(userDir))
            {
                foreach (var filePath in Directory.GetFiles(userDir, "*.md"))
                {
                    var fileName = Path.GetFileName(filePath);
                    if (fileName.Equals("MEMORY.md", StringComparison.OrdinalIgnoreCase)) continue;

                    var mem = await ParseFileAsync(filePath);
                    if (mem != null)
                    {
                        mem.UserId = userId;
                        result.Add(mem);
                    }
                }
            }

            _userCaches[userId] = result;
        }
        finally
        {
            userLock.Release();
        }
    }

    public string MemoryDir => _memoryDir;

    // ─── 読み込み ───────────────────────────────────────────────────────────

    /// <summary>指定ユーザーの記憶を全件返す。userId=0のファイルは全ユーザーに共有。</summary>
    public async Task<List<LongTermMemory>> GetMemoriesForUserAsync(int userId, string? agentRole = null)
    {
        await EnsureUserCacheLoadedAsync(userId);

        if (_userCaches.TryGetValue(userId, out var list))
        {
            return list
                .Where(m => agentRole == null || m.BoundAgentRole == null || m.BoundAgentRole == agentRole)
                .OrderByDescending(m => m.CreatedAt)
                .ToList();
        }
        return new List<LongTermMemory>();
    }

    private const int MaxScoringCandidates = 150;

    /// <summary>プロンプトに関連する記憶を多段スコアリングで検索する。</summary>
    public async Task<List<LongTermMemory>> SearchAsync(string prompt, int userId, int maxResults = 5, string? agentRole = null)
    {
        // Cap candidates before the O(n) scoring loop to prevent unbounded CPU growth.
        var all = (await GetMemoriesForUserAsync(userId, agentRole))
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
        await EnsureUserCacheLoadedAsync(memory.UserId);

        var fileName = memory.SourceFile ?? GenerateFileName(memory.Tags);
        
        var targetDir = memory.UserId == 0 
            ? Path.Combine(_memoryDir, "shared") 
            : Path.Combine(_memoryDir, $"user_{memory.UserId}");

        Directory.CreateDirectory(targetDir);
        var filePath = Path.Combine(targetDir, fileName);

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
            boundAgentRole: {memory.BoundAgentRole ?? ""}
            ---

            {memory.Content}
            """;

        await File.WriteAllTextAsync(filePath, fm);
        memory.SourceFile = fileName;

        // キャッシュ更新
        if (memory.UserId == 0)
        {
            _userCaches.Clear();
        }
        else
        {
            var userLock = GetUserLock(memory.UserId);
            await userLock.WaitAsync();
            try
            {
                if (_userCaches.TryGetValue(memory.UserId, out var list))
                {
                    if (!list.Any(m => m.SourceFile == fileName))
                    {
                        list.Add(memory);
                    }
                }
            }
            finally
            {
                userLock.Release();
            }
        }

        await RefreshUserIndexAsync(memory.UserId);
    }

    // ─── 削除 ───────────────────────────────────────────────────────────────

    public async Task DeleteByFileNameAsync(string safeFileName, int userId)
    {
        if (safeFileName.Contains('/') || safeFileName.Contains('\\') || safeFileName.Contains("..")) return;

        var targetDir = userId == 0 
            ? Path.Combine(_memoryDir, "shared") 
            : Path.Combine(_memoryDir, $"user_{userId}");

        var path = Path.Combine(targetDir, safeFileName);
        if (File.Exists(path)) File.Delete(path);

        // キャッシュから削除
        if (userId == 0)
        {
            _userCaches.Clear();
        }
        else
        {
            var userLock = GetUserLock(userId);
            await userLock.WaitAsync();
            try
            {
                if (_userCaches.TryGetValue(userId, out var list))
                {
                    list.RemoveAll(m => m.SourceFile == safeFileName);
                }
            }
            finally
            {
                userLock.Release();
            }
        }

        await RefreshUserIndexAsync(userId);
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
                BoundAgentRole = Get("boundAgentRole") is { Length: > 0 } bar ? bar : null,
                SourceFile = Path.GetFileName(filePath),
            };
        }
        catch { return null; }
    }

    // ─── インデックス更新 ────────────────────────────────────────────────────

    public async Task RefreshUserIndexAsync(int userId)
    {
        await EnsureUserCacheLoadedAsync(userId);

        var targetDir = userId == 0 
            ? Path.Combine(_memoryDir, "shared") 
            : Path.Combine(_memoryDir, $"user_{userId}");

        Directory.CreateDirectory(targetDir);
        var indexPath = Path.Combine(targetDir, "MEMORY.md");

        if (_userCaches.TryGetValue(userId, out var list))
        {
            var lines = new List<string> { $"# {(userId == 0 ? "Shared" : $"User {userId}")} Memory Index", "" };

            var sorted = list.OrderBy(m => m.SourceFile).ToList();
            foreach (var mem in sorted)
            {
                var title = mem.Tags.Split(',')[0].Trim();
                var hook = mem.Content.Length > 60 ? mem.Content[..60].Replace('\n', ' ') + "..." : mem.Content.Replace('\n', ' ');
                lines.Add($"- [{title}]({mem.SourceFile}) — {hook}");
            }

            await File.WriteAllTextAsync(indexPath, string.Join("\n", lines) + "\n");
        }
    }

    public void Dispose()
    {
        foreach (var userLock in _userLocks.Values)
        {
            userLock.Dispose();
        }
    }
}
