using System.Collections.Concurrent;
using System.Text;
using AiChatApp.Models;

namespace AiChatApp.Services;

public class SkillManagerService
{
    private readonly string _basePath;
    private readonly string _userPath;

    // Cache key: null → system-only, int → userId. TTL = 1 min.
    private readonly ConcurrentDictionary<int, (List<SkillInfo> Skills, DateTime Expiry)> _userCache = new();
    private (List<SkillInfo> Skills, DateTime Expiry) _systemCache = (new(), DateTime.MinValue);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SkillManagerService(IConfiguration config)
    {
        var currentDir = Directory.GetCurrentDirectory();
        var root = currentDir.EndsWith("AiChatApp") ? currentDir : Path.Combine(currentDir, "AiChatApp");

        _basePath = Path.Combine(root, "AgentSkills", "System");
        _userPath = Path.Combine(root, "AgentSkills", "User");

        Console.WriteLine($"[SkillManager] Base Path: {_basePath}");
        if (!Directory.Exists(_basePath)) Directory.CreateDirectory(_basePath);
        if (!Directory.Exists(_userPath)) Directory.CreateDirectory(_userPath);
    }

    public async Task<List<SkillInfo>> GetAllSkillsAsync(int? userId = null)
    {
        var now = DateTime.UtcNow;

        // Fast path: cache hit (no lock needed for reads)
        if (_systemCache.Expiry > now)
        {
            var sys = _systemCache.Skills;
            if (!userId.HasValue) return sys;
            if (_userCache.TryGetValue(userId.Value, out var uc) && uc.Expiry > now)
                return sys.Concat(uc.Skills).ToList();
        }

        // Slow path: rebuild cache under lock
        await _lock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (_systemCache.Expiry <= now)
            {
                var sys = new List<SkillInfo>();
                await LoadFromDir(_basePath, sys, isSystem: true);
                _systemCache = (sys, now + CacheTtl);
            }

            if (userId.HasValue)
            {
                if (!_userCache.TryGetValue(userId.Value, out var uc) || uc.Expiry <= now)
                {
                    var usr = new List<SkillInfo>();
                    await LoadFromDir(Path.Combine(_userPath, userId.Value.ToString()), usr, isSystem: false);
                    _userCache[userId.Value] = (usr, now + CacheTtl);
                }
            }
        }
        finally { _lock.Release(); }

        if (!userId.HasValue) return _systemCache.Skills;
        _userCache.TryGetValue(userId.Value, out var cached);
        return _systemCache.Skills.Concat(cached.Skills ?? Enumerable.Empty<SkillInfo>()).ToList();
    }

    private void InvalidateCache(int? userId = null)
    {
        _systemCache = (new(), DateTime.MinValue);
        if (userId.HasValue) _userCache.TryRemove(userId.Value, out _);
        else _userCache.Clear();
    }

    private async Task LoadFromDir(string path, List<SkillInfo> list, bool isSystem)
    {
        if (!Directory.Exists(path)) return;
        foreach (var dir in Directory.GetDirectories(path))
        {
            var skillFile = Path.Combine(dir, "SKILL.md");
            if (!File.Exists(skillFile)) continue;

            var content = await File.ReadAllTextAsync(skillFile);
            var info = ParseSkillFile(Path.GetFileName(dir), content);
            info.IsSystem = isSystem;
            info.Path = dir;
            list.Add(info);
        }
    }

    public async Task SaveSkillAsync(string name, string content, int? userId = null, bool isSystem = false)
    {
        string targetDir;
        if (isSystem)
        {
            targetDir = Path.Combine(_basePath, name);
        }
        else if (userId.HasValue)
        {
            targetDir = Path.Combine(_userPath, userId.Value.ToString(), name);
        }
        else
        {
            throw new ArgumentException("userId is required for user skills.");
        }

        if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

        var filePath = Path.Combine(targetDir, "SKILL.md");
        await File.WriteAllTextAsync(filePath, content);
        InvalidateCache(isSystem ? null : userId);
    }

    public void DeleteSkill(string name, int? userId = null, bool isSystem = false)
    {
        string targetDir;
        if (isSystem)
        {
            targetDir = Path.Combine(_basePath, name);
        }
        else if (userId.HasValue)
        {
            targetDir = Path.Combine(_userPath, userId.Value.ToString(), name);
        }
        else
        {
            return;
        }

        if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
        InvalidateCache(isSystem ? null : userId);
    }

    private SkillInfo ParseSkillFile(string dirName, string content)
    {
        var info = new SkillInfo { Name = dirName, Prompt = content };
        if (content.StartsWith("---"))
        {
            var endIdx = content.IndexOf("---", 3);
            if (endIdx > 0)
            {
                var yaml = content.Substring(3, endIdx - 3);
                foreach (var line in yaml.Split('\n'))
                {
                    if (line.StartsWith("name:")) info.DisplayName = line.Replace("name:", "").Trim();
                    if (line.StartsWith("description:")) info.Description = line.Replace("description:", "").Trim();
                }
                info.Prompt = content.Substring(endIdx + 3).Trim();
            }
        }
        if (string.IsNullOrEmpty(info.DisplayName)) info.DisplayName = dirName;
        return info;
    }
}

public class SkillInfo
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Prompt { get; set; } = "";
    public bool IsSystem { get; set; }
    public string Path { get; set; } = "";
}
