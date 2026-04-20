using AiChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace AiChatApp.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<LongTermMemory> LongTermMemories => Set<LongTermMemory>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<AgentStep> AgentSteps => Set<AgentStep>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<AgentProfile> AgentProfiles => Set<AgentProfile>();
    public DbSet<SessionMemory> SessionMemories => Set<SessionMemory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // HasData シードを削除。ユーザー登録時に個別作成する方式に変更。
    }
}
