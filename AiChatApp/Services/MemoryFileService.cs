using AiChatApp.Models;
using System.Text.RegularExpressions;

namespace AiChatApp.Services;

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

    private string AppMemFilePath(int id) => Path.Combine(_memoryDir, $"mem_{id}.md");

    public async Task WriteAsync(LongTermMemory memory)
    {
        var shortDesc = memory.Content.Length > 80 ? memory.Content[..80] + "..." : memory.Content;
        var content = $"""
            ---
            name: {memory.Tags}
            description: {shortDesc}
            type: user
            appMemoryId: {memory.Id}
            ---

            {memory.Content}
            """;
        await File.WriteAllTextAsync(AppMemFilePath(memory.Id), content);
        await UpdateMemoryIndex();
    }

    public void Delete(int memoryId)
    {
        var path = AppMemFilePath(memoryId);
        if (File.Exists(path)) File.Delete(path);
        _ = UpdateMemoryIndex();
    }

    public void DeleteFile(string safeFileName)
    {
        var path = Path.Combine(_memoryDir, safeFileName);
        if (File.Exists(path)) File.Delete(path);
        _ = UpdateMemoryIndex();
    }

    // DB管理外のファイル記憶（Claude Code autoDreamが書いたもの）を読み込む
    public List<LongTermMemory> GetFileOnlyMemories(IEnumerable<int> dbIds)
    {
        var dbFiles = dbIds.Select(id => $"mem_{id}.md").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<LongTermMemory>();

        if (!Directory.Exists(_memoryDir)) return result;

        foreach (var filePath in Directory.GetFiles(_memoryDir, "*.md"))
        {
            var fileName = Path.GetFileName(filePath);
            if (fileName.Equals("MEMORY.md", StringComparison.OrdinalIgnoreCase)) continue;
            if (dbFiles.Contains(fileName)) continue;

            var text = File.ReadAllText(filePath);
            var (name, _, body) = ParseFrontmatter(text);

            // ファイル名をIDとして負の値でエンコード（UI用）
            result.Add(new LongTermMemory
            {
                Id = 0,
                UserId = 0,
                Content = string.IsNullOrWhiteSpace(body) ? text.Trim() : body,
                Tags = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(fileName) : name,
                RelevanceScore = 80,
                CreatedAt = File.GetCreationTime(filePath),
                LastAccessedAt = File.GetLastWriteTime(filePath),
                // SourceFile はモデルに追加しない→Tags末尾にエンコード
            });

            // ファイル名をTagsの末尾にエンコードして削除時に使えるようにする
            result[^1].Tags = result[^1].Tags + $"|file:{fileName}";
        }

        return result;
    }

    private static (string Name, string Description, string Body) ParseFrontmatter(string text)
    {
        if (!text.TrimStart().StartsWith("---")) return ("", "", text.Trim());

        var match = Regex.Match(text, @"^---\s*\n(.*?)\n---\s*\n?(.*)", RegexOptions.Singleline);
        if (!match.Success) return ("", "", text.Trim());

        var fm = match.Groups[1].Value;
        var body = match.Groups[2].Value.Trim();

        var name = Regex.Match(fm, @"^name:\s*(.+)$", RegexOptions.Multiline).Groups[1].Value.Trim();
        var desc = Regex.Match(fm, @"^description:\s*(.+)$", RegexOptions.Multiline).Groups[1].Value.Trim();

        return (name, desc, body);
    }

    private async Task UpdateMemoryIndex()
    {
        var indexPath = Path.Combine(_memoryDir, "MEMORY.md");
        var files = Directory.GetFiles(_memoryDir, "*.md")
            .Where(f => !Path.GetFileName(f).Equals("MEMORY.md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        var lines = new List<string> { "# Memory Index", "" };
        foreach (var filePath in files)
        {
            var fileName = Path.GetFileName(filePath);
            var text = File.ReadAllText(filePath);
            var (name, desc, _) = ParseFrontmatter(text);
            var title = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(fileName) : name;
            var hook = string.IsNullOrWhiteSpace(desc) ? title : desc[..Math.Min(80, desc.Length)];
            lines.Add($"- [{title}]({fileName}) — {hook}");
        }

        await File.WriteAllTextAsync(indexPath, string.Join("\n", lines) + "\n");
    }
}
