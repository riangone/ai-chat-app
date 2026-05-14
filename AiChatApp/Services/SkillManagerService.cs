using System.Text;
using AiChatApp.Models;

namespace AiChatApp.Services;

public class SkillManagerService
{
    private readonly string _basePath;
    private readonly string _userPath;

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
        var skills = new List<SkillInfo>();
        await LoadFromDir(_basePath, skills, isSystem: true);
        
        if (userId.HasValue)
        {
            var userPath = Path.Combine(_userPath, userId.Value.ToString());
            await LoadFromDir(userPath, skills, isSystem: false);
        }
        else
        {
            // 如果未指定 userId，可能是在管理员上下文或旧代码中，
            // 默认加载所有用户的（这取决于具体需求，这里我们倾向于只加载系统技能）
            // 或者我们可以遍历所有子目录。为了向后兼容，我们暂时不加载。
        }
        return skills;
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
