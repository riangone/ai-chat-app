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

        // Explicitly create Notes table if it doesn't exist
        using var command = db.Database.GetDbConnection().CreateCommand();
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
        db.Database.OpenConnection();
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
