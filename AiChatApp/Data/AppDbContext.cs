using AiChatApp.Models;
using AiChatApp.Models.Harness;
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
    public DbSet<Evaluation> Evaluations => Set<Evaluation>();
    public DbSet<InputHistory> InputHistories => Set<InputHistory>();
    public DbSet<TodoItem> TodoItems => Set<TodoItem>();
    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // HasData シードを削除。ユーザー登録時に個別作成する方式に変更。

        // Composite index for the most common query: session messages ordered by time
        modelBuilder.Entity<Message>()
            .HasIndex(m => new { m.ChatSessionId, m.Timestamp });

        modelBuilder.Entity<ChatSession>()
            .HasIndex(s => s.UserId);

        modelBuilder.Entity<Skill>()
            .HasIndex(s => new { s.UserId, s.IsEnabled });

        modelBuilder.Entity<SessionMemory>()
            .HasIndex(m => m.ChatSessionId);

        modelBuilder.Entity<AgentStep>()
            .HasIndex(s => s.MessageId);

        modelBuilder.Entity<LongTermMemory>()
            .HasIndex(m => m.UserId);

        modelBuilder.Entity<InputHistory>()
            .HasIndex(h => h.UserId);
    }
}
