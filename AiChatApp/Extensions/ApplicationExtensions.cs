using AiChatApp.Data;
using AiChatApp.Services.Harness;
using Microsoft.EntityFrameworkCore;

namespace AiChatApp.Extensions;

public static class ApplicationExtensions
{
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        // Ensure database connection is open for manual schema updates
        db.Database.OpenConnection();
        using var command = db.Database.GetDbConnection().CreateCommand();

        // 1. Add DefaultProvider to Users if missing
        command.CommandText = "PRAGMA table_info(Users);";
        var usersColumns = new List<string>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read()) usersColumns.Add(reader.GetString(1));
        }
        if (!usersColumns.Contains("DefaultProvider"))
        {
            command.CommandText = "ALTER TABLE Users ADD COLUMN DefaultProvider TEXT NOT NULL DEFAULT 'gemini';";
            command.ExecuteNonQuery();
        }

        // 2. Add PreferredProvider to ChatSessions if missing
        command.CommandText = "PRAGMA table_info(ChatSessions);";
        var sessionColumns = new List<string>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read()) sessionColumns.Add(reader.GetString(1));
        }
        if (!sessionColumns.Contains("PreferredProvider"))
        {
            command.CommandText = "ALTER TABLE ChatSessions ADD COLUMN PreferredProvider TEXT NOT NULL DEFAULT 'gemini';";
            command.ExecuteNonQuery();
        }

        // 3. Explicitly create Notes table if it doesn't exist (Existing logic)
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Notes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Content TEXT,
                UserId INTEGER NOT NULL,
                CreatedAt DATETIME NOT NULL,
                UpdatedAt DATETIME NOT NULL,
                CONSTRAINT FK_Notes_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
            );";
        command.ExecuteNonQuery();

        // 4. Explicitly create InputHistories table if it doesn't exist
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS InputHistories (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId INTEGER NOT NULL,
                Content TEXT NOT NULL,
                UsedAt DATETIME NOT NULL,
                CONSTRAINT FK_InputHistories_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
            );";
        command.ExecuteNonQuery();

        // Initialize PipelineLoaderService
        var pipelineLoader = scope.ServiceProvider.GetRequiredService<PipelineLoaderService>();
        await pipelineLoader.LoadAllAsync();
        pipelineLoader.WatchForChanges(async fileName =>
        {
            await pipelineLoader.ReloadPipelineAsync(fileName);
        });
    }

    public static string GetRelativeTime(this DateTime dateTime)
    {
        var span = DateTime.UtcNow - dateTime;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
        return dateTime.ToString("MMM dd");
    }
}
