using System.Text;
using AiChatApp.Models;

namespace AiChatApp.Services;

public class SkillManagerService
{
    private readonly string _basePath;
    private readonly string _userPath;

    public SkillManagerService()
    {
        var currentDir = Directory.GetCurrentDirectory();
        // Detect if we are in the root or in the project subdir
        var root = currentDir.EndsWith("AiChatApp") ? currentDir : Path.Combine(currentDir, "AiChatApp");
        
        _basePath = Path.Combine(root, "AgentSkills", "System");
        _userPath = Path.Combine(root, "AgentSkills", "User");
        
        Console.WriteLine($"[SkillManager] Base Path: {_basePath}");
        if (!Directory.Exists(_basePath)) Directory.CreateDirectory(_basePath);
        if (!Directory.Exists(_userPath)) Directory.CreateDirectory(_userPath);
    }

    public async Task<List<SkillInfo>> GetAllSkillsAsync()
    {
        var skills = new List<SkillInfo>();
        await LoadFromDir(_basePath, skills, isSystem: true);
        await LoadFromDir(_userPath, skills, isSystem: false);
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

    public async Task SaveSkillAsync(string name, string content, bool isSystem = false)
    {
        var targetDir = Path.Combine(isSystem ? _basePath : _userPath, name);
        if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
        
        var filePath = Path.Combine(targetDir, "SKILL.md");
        await File.WriteAllTextAsync(filePath, content);
    }

    public void DeleteSkill(string name, bool isSystem = false)
    {
        var targetDir = Path.Combine(isSystem ? _basePath : _userPath, name);
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
