using AiChatApp.Data;
using AiChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace AiChatApp.Services;

public class ProjectService
{
    private readonly AppDbContext _db;

    public ProjectService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<Project>> GetUserProjectsAsync(int userId)
    {
        return await _db.Projects
            .Include(p => p.Agents)
            .Where(p => p.UserId == userId)
            .ToListAsync();
    }

    public async Task<Project?> GetProjectAsync(int projectId, int userId)
    {
        return await _db.Projects
            .Include(p => p.Agents)
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId);
    }

    public async Task<Project> CreateProjectAsync(string name, string rootPath, int userId)
    {
        // Ensure directory exists
        if (!Directory.Exists(rootPath))
        {
            Directory.CreateDirectory(rootPath);
        }

        var project = new Project
        {
            Name = name,
            RootPath = Path.GetFullPath(rootPath),
            UserId = userId
        };

        _db.Projects.Add(project);
        
        // Add a default agent for the project
        project.Agents.Add(new AgentProfile
        {
            RoleName = "Architect",
            SystemPrompt = "You are the project architect. Focus on high-level structure and design patterns.",
            Color = "info"
        });

        await _db.SaveChangesAsync();
        return project;
    }

    public async Task<AgentProfile> AddAgentAsync(int projectId, string roleName, string systemPrompt, string color, string? preferredProvider = null)
    {
        var agent = new AgentProfile
        {
            ProjectId = projectId,
            RoleName = roleName,
            SystemPrompt = systemPrompt,
            Color = color,
            PreferredProvider = preferredProvider
        };

        _db.AgentProfiles.Add(agent);
        await _db.SaveChangesAsync();
        return agent;
    }

    public async Task DeleteProjectAsync(int projectId, int userId)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId);
        if (project != null)
        {
            _db.Projects.Remove(project);
            await _db.SaveChangesAsync();
        }
    }

    public async Task DeleteAgentAsync(int agentId, int userId)
    {
        var agent = await _db.AgentProfiles
            .Include(a => a.Project)
            .FirstOrDefaultAsync(a => a.Id == agentId && a.Project!.UserId == userId);
        if (agent != null)
        {
            _db.AgentProfiles.Remove(agent);
            await _db.SaveChangesAsync();
        }
    }
}
