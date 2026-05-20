using AiChatApp.Data;
using AiChatApp.Models;
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

        // Seed default admin user if not exists
        if (!await db.Users.AnyAsync(u => u.Username == "admin"))
        {
            var adminPassword = Environment.GetEnvironmentVariable("ADMIN_INITIAL_PASSWORD") ?? "admin123";
            if (adminPassword == "admin123")
                app.Logger.LogWarning("Using default admin password 'admin123'. Set ADMIN_INITIAL_PASSWORD env var in production.");
            var admin = new User { Username = "admin", PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword), DefaultProvider = "" };
            db.Users.Add(admin);
            await db.SaveChangesAsync();
        }

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
            command.CommandText = "ALTER TABLE Users ADD COLUMN DefaultProvider TEXT NOT NULL DEFAULT '';";
            command.ExecuteNonQuery();
        }
        if (!usersColumns.Contains("LastBriefingContent"))
        {
            command.CommandText = "ALTER TABLE Users ADD COLUMN LastBriefingContent TEXT;";
            command.ExecuteNonQuery();
        }
        if (!usersColumns.Contains("BriefingUpdatedAt"))
        {
            command.CommandText = "ALTER TABLE Users ADD COLUMN BriefingUpdatedAt DATETIME;";
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
            command.CommandText = "ALTER TABLE ChatSessions ADD COLUMN PreferredProvider TEXT NOT NULL DEFAULT '';";
            command.ExecuteNonQuery();
        }

        // 3. Add AgentName to Messages if missing
        command.CommandText = "PRAGMA table_info(Messages);";
        var messageColumns = new List<string>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read()) messageColumns.Add(reader.GetString(1));
        }
        if (!messageColumns.Contains("AgentName"))
        {
            command.CommandText = "ALTER TABLE Messages ADD COLUMN AgentName TEXT;";
            command.ExecuteNonQuery();
        }

        // 4. Add Model to AgentSteps if missing
        command.CommandText = "PRAGMA table_info(AgentSteps);";
        var agentStepColumns = new List<string>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read()) agentStepColumns.Add(reader.GetString(1));
        }
        if (!agentStepColumns.Contains("Model"))
        {
            command.CommandText = "ALTER TABLE AgentSteps ADD COLUMN Model TEXT NOT NULL DEFAULT '';";
            command.ExecuteNonQuery();
        }
        if (!agentStepColumns.Contains("PromptTokens"))
        {
            command.CommandText = "ALTER TABLE AgentSteps ADD COLUMN PromptTokens INTEGER NOT NULL DEFAULT 0;";
            command.ExecuteNonQuery();
        }
        if (!agentStepColumns.Contains("CompletionTokens"))
        {
            command.CommandText = "ALTER TABLE AgentSteps ADD COLUMN CompletionTokens INTEGER NOT NULL DEFAULT 0;";
            command.ExecuteNonQuery();
        }
        if (!agentStepColumns.Contains("TotalTokens"))
        {
            command.CommandText = "ALTER TABLE AgentSteps ADD COLUMN TotalTokens INTEGER NOT NULL DEFAULT 0;";
            command.ExecuteNonQuery();
        }
        if (!agentStepColumns.Contains("Provider"))
        {
            command.CommandText = "ALTER TABLE AgentSteps ADD COLUMN Provider TEXT NOT NULL DEFAULT '';";
            command.ExecuteNonQuery();
        }

        // 5. Explicitly create Notes table if it doesn't exist (Existing logic)
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

        // 4. Ensure TodoItems table exists and add missing columns
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS TodoItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Description TEXT,
                IsCompleted INTEGER NOT NULL DEFAULT 0,
                CreatedAt DATETIME NOT NULL,
                UserId INTEGER
            );";
        command.ExecuteNonQuery();

        command.CommandText = "PRAGMA table_info(TodoItems);";
        var todoColumns = new List<string>();
        using (var reader = command.ExecuteReader()) { while (reader.Read()) todoColumns.Add(reader.GetString(1)); }
        if (!todoColumns.Contains("Description"))
        {
            command.CommandText = "ALTER TABLE TodoItems ADD COLUMN Description TEXT;";
            command.ExecuteNonQuery();
        }
        if (!todoColumns.Contains("UserId"))
        {
            command.CommandText = "ALTER TABLE TodoItems ADD COLUMN UserId INTEGER;";
            command.ExecuteNonQuery();
        }
        if (!todoColumns.Contains("DueDate"))
        {
            command.CommandText = "ALTER TABLE TodoItems ADD COLUMN DueDate TEXT;";
            command.ExecuteNonQuery();
        }
        if (!todoColumns.Contains("IsNotified"))
        {
            command.CommandText = "ALTER TABLE TodoItems ADD COLUMN IsNotified INTEGER NOT NULL DEFAULT 0;";
            command.ExecuteNonQuery();
        }

        // 5a. Add UpdatedAt to LongTermMemories if missing
        command.CommandText = "PRAGMA table_info(LongTermMemories);";
        var memoryColumns = new List<string>();
        using (var reader = command.ExecuteReader()) { while (reader.Read()) memoryColumns.Add(reader.GetString(1)); }
        if (!memoryColumns.Contains("UpdatedAt"))
        {
            command.CommandText = "ALTER TABLE LongTermMemories ADD COLUMN UpdatedAt DATETIME NOT NULL DEFAULT '2025-01-01 00:00:00';";
            command.ExecuteNonQuery();
        }

        // 5. Explicitly create InputHistories table if it doesn't exist
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS InputHistories (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId INTEGER NOT NULL,
                Content TEXT NOT NULL,
                UsedAt TEXT NOT NULL
            );";
        command.ExecuteNonQuery();

        // Ensure indexes exist on already-created database (EnsureCreated only applies to new DBs)
        foreach (var ddl in new[]
        {
            "CREATE INDEX IF NOT EXISTS IX_Messages_SessionTimestamp ON Messages (ChatSessionId, Timestamp);",
            "CREATE INDEX IF NOT EXISTS IX_ChatSessions_UserId       ON ChatSessions (UserId);",
            "CREATE INDEX IF NOT EXISTS IX_Skills_UserEnabled         ON Skills (UserId, IsEnabled);",
            "CREATE INDEX IF NOT EXISTS IX_SessionMemories_SessionId  ON SessionMemories (ChatSessionId);",
            "CREATE INDEX IF NOT EXISTS IX_AgentSteps_MessageId       ON AgentSteps (MessageId);",
            "CREATE INDEX IF NOT EXISTS IX_LongTermMemories_UserId    ON LongTermMemories (UserId);",
            "CREATE INDEX IF NOT EXISTS IX_InputHistories_UserId      ON InputHistories (UserId);",
        })
        {
            command.CommandText = ddl;
            command.ExecuteNonQuery();
        }

        // Initialize PipelineLoaderService
        var pipelineLoader = scope.ServiceProvider.GetRequiredService<PipelineLoaderService>();
        await pipelineLoader.LoadAllAsync();
        pipelineLoader.WatchForChanges(async fileName =>
        {
            await pipelineLoader.ReloadPipelineAsync(fileName);
        });

        // 5c. Ensure ProactiveSuggestions and SuggestionAction tables exist
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS ProactiveSuggestions (
                Id TEXT PRIMARY KEY,
                UserId INTEGER,
                Title TEXT NOT NULL,
                Content TEXT NOT NULL,
                Type TEXT NOT NULL,
                Timestamp TEXT NOT NULL,
                IsRead INTEGER NOT NULL DEFAULT 0,
                Metadata TEXT
            );";
        command.ExecuteNonQuery();

        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS SuggestionAction (
                Id TEXT PRIMARY KEY,
                Label TEXT NOT NULL,
                Command TEXT NOT NULL,
                Payload TEXT,
                Style TEXT NOT NULL,
                ProactiveSuggestionId TEXT,
                CONSTRAINT FK_SuggestionAction_ProactiveSuggestions_ProactiveSuggestionId FOREIGN KEY (ProactiveSuggestionId) REFERENCES ProactiveSuggestions (Id) ON DELETE CASCADE
            );";
        command.ExecuteNonQuery();

        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS PushSubscriptions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId INTEGER NOT NULL,
                Endpoint TEXT NOT NULL,
                P256dh TEXT NOT NULL,
                Auth TEXT NOT NULL,
                CreatedAt DATETIME NOT NULL,
                CONSTRAINT FK_PushSubscriptions_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE CASCADE
            );";
        command.ExecuteNonQuery();

        // 6. Add UserId and IsRead to ProactiveSuggestions if missing
        command.CommandText = "PRAGMA table_info(ProactiveSuggestions);";
        var suggestionsColumns = new List<string>();
        using (var reader = command.ExecuteReader()) { while (reader.Read()) suggestionsColumns.Add(reader.GetString(1)); }
        if (!suggestionsColumns.Contains("UserId"))
        {
            command.CommandText = "ALTER TABLE ProactiveSuggestions ADD COLUMN UserId INTEGER;";
            command.ExecuteNonQuery();
        }
        if (!suggestionsColumns.Contains("IsRead"))
        {
            command.CommandText = "ALTER TABLE ProactiveSuggestions ADD COLUMN IsRead INTEGER NOT NULL DEFAULT 0;";
            command.ExecuteNonQuery();
        }

        // Attachments table
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Attachments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                MessageId INTEGER,
                UserId INTEGER NOT NULL,
                FileName TEXT NOT NULL,
                StoredPath TEXT NOT NULL,
                FileSize INTEGER NOT NULL DEFAULT 0,
                MimeType TEXT NOT NULL DEFAULT '',
                FileType TEXT NOT NULL DEFAULT 'binary',
                UploadedAt DATETIME NOT NULL,
                CONSTRAINT FK_Attachments_Messages_MessageId FOREIGN KEY (MessageId) REFERENCES Messages (Id) ON DELETE SET NULL
            );";
        command.ExecuteNonQuery();
        command.CommandText = "CREATE INDEX IF NOT EXISTS IX_Attachments_UserId ON Attachments (UserId);";
        command.ExecuteNonQuery();
        command.CommandText = "CREATE INDEX IF NOT EXISTS IX_Attachments_MessageId ON Attachments (MessageId);";
        command.ExecuteNonQuery();

        // Invalidate policy cache when policy files change
        var policiesPath = Path.Combine(AppContext.BaseDirectory, "pipelines", "policies");
        if (Directory.Exists(policiesPath))
        {
            var policyWatcher = new FileSystemWatcher(policiesPath, "*.md")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            policyWatcher.Changed += (_, _) => AiChatApp.Services.AiPromptService.InvalidatePolicyCache();
            policyWatcher.Created += (_, _) => AiChatApp.Services.AiPromptService.InvalidatePolicyCache();
            policyWatcher.Deleted += (_, _) => AiChatApp.Services.AiPromptService.InvalidatePolicyCache();
        }
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
