# 完全実装設計書：Hermes Agent + Harness Engineering

本文書は `ARCHITECTURE_ANALYSIS.md` で特定されたギャップを埋め、設計思想を完全に実現するための詳細仕様書である。別のAIが本文書のみを読んでコードを実装できるよう、すべての変更を具体的なコードとともに記述する。

---

## 前提：既存コードの把握

実装者は以下のファイルを事前に読むこと：
- `AiChatApp/Services/AiService.cs`
- `AiChatApp/Program.cs`
- `AiChatApp/Data/AppDbContext.cs`
- `AiChatApp/Models/Message.cs`（User, ChatSession, Message が同居）
- `AiChatApp/Models/LongTermMemory.cs`
- `AiChatApp/Models/Skill.cs`
- `AiChatApp/wwwroot/index.html`

DBスキーマ変更を伴うため、実装完了後に `chat.db` を削除してアプリを再起動すること。

---

## フェーズ1：Hermes Agent の完全実装

### H1. データモデルの拡張

#### H1-1. `LongTermMemory` にタグ複数化・重みを追加

**ファイル**: `AiChatApp/Models/LongTermMemory.cs`

```csharp
namespace AiChatApp.Models;

public class LongTermMemory
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;       // カンマ区切りで複数タグを格納: "code,csharp,performance"
    public int RelevanceScore { get; set; } = 100;         // 重要度 0-100。低いものは検索時に除外
    public int AccessCount { get; set; } = 0;              // 参照された回数（将来的な鮮度管理用）
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
}
```

#### H1-2. `Skill` にユーザースコープとエージェントバインドを追加

**ファイル**: `AiChatApp/Models/Skill.cs`

```csharp
namespace AiChatApp.Models;

public class Skill
{
    public int Id { get; set; }
    public int UserId { get; set; }                         // ユーザースコープ（グローバルから変更）
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExampleInput { get; set; } = string.Empty;
    public string TriggerKeywords { get; set; } = string.Empty; // 発火キーワード（カンマ区切り）。空の場合は常に有効
    public bool IsEnabled { get; set; } = true;
    public string? BoundAgentRole { get; set; }             // null=全エージェント, "Executor"=Executorのみ

    public User? User { get; set; }
}
```

#### H1-3. `AgentStep` モデルを新規作成（エージェント実行ログ）

**ファイル**: `AiChatApp/Models/AgentStep.cs`（新規作成）

```csharp
namespace AiChatApp.Models;

public class AgentStep
{
    public int Id { get; set; }
    public int MessageId { get; set; }                      // 紐づくAIメッセージ
    public string Role { get; set; } = string.Empty;        // "Orchestrator" | "Executor" | "Reviewer"
    public string Persona { get; set; } = string.Empty;     // 使用したシステムプロンプト
    public string Input { get; set; } = string.Empty;       // このエージェントへの入力
    public string Output { get; set; } = string.Empty;      // このエージェントの出力
    public int AttemptNumber { get; set; } = 1;             // 差し戻し時は 2, 3...
    public bool WasAccepted { get; set; } = true;           // Reviewerが承認したか
    public int DurationMs { get; set; }                     // 実行時間（ミリ秒）
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Message? Message { get; set; }
}
```

---

### H2. AppDbContext の更新

**ファイル**: `AiChatApp/Data/AppDbContext.cs`

```csharp
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
    public DbSet<AgentStep> AgentSteps => Set<AgentStep>();   // 追加

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // HasData シードを削除。ユーザー登録時に個別作成する方式に変更。
        // （IMPROVEMENTS.md Issue 3 参照）
    }
}
```

---

### H3. 記憶検索エンジンの改善（多段マッチング）

**ファイル**: `AiChatApp/Services/MemorySearchService.cs`（新規作成）

```csharp
using AiChatApp.Data;
using AiChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace AiChatApp.Services;

public class MemorySearchService
{
    private readonly AppDbContext _db;

    public MemorySearchService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// プロンプトに関連する記憶を多段マッチングで検索する。
    /// 優先度: (1)タグの完全一致 → (2)タグの部分一致 → (3)コンテンツ部分一致
    /// </summary>
    public async Task<List<LongTermMemory>> SearchAsync(string prompt, int userId, int maxResults = 5)
    {
        var allMemories = await _db.LongTermMemories
            .Where(m => m.UserId == userId && m.RelevanceScore > 20)
            .ToListAsync();

        // プロンプトを単語に分割（スペース・句読点で区切り）
        var promptWords = prompt
            .Split(new[] { ' ', '　', '、', '。', ',', '.', '!', '?', '\n' },
                   StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant())
            .Where(w => w.Length >= 2)
            .ToHashSet();

        var scored = allMemories.Select(m =>
        {
            var memTags = m.Tags
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().ToLowerInvariant())
                .ToList();

            int score = 0;

            // (1) タグとプロンプト単語の完全一致 → 高スコア
            foreach (var tag in memTags)
            {
                if (promptWords.Contains(tag)) score += 30;
            }

            // (2) プロンプト全体にタグが部分一致 → 中スコア
            foreach (var tag in memTags)
            {
                if (prompt.Contains(tag, StringComparison.OrdinalIgnoreCase)) score += 15;
            }

            // (3) コンテンツがプロンプト単語に含まれる → 低スコア
            foreach (var word in promptWords)
            {
                if (m.Content.Contains(word, StringComparison.OrdinalIgnoreCase)) score += 5;
            }

            // 記憶自体の重要度を乗算
            score = (int)(score * (m.RelevanceScore / 100.0));

            return new { Memory = m, Score = score };
        })
        .Where(x => x.Score > 0)
        .OrderByDescending(x => x.Score)
        .Take(maxResults)
        .ToList();

        // アクセス履歴を更新
        foreach (var item in scored)
        {
            item.Memory.AccessCount++;
            item.Memory.LastAccessedAt = DateTime.UtcNow;
        }
        if (scored.Any()) await _db.SaveChangesAsync();

        return scored.Select(x => x.Memory).ToList();
    }

    /// <summary>
    /// 有効スキルをトリガーキーワードで検索する。
    /// TriggerKeywords が空のスキルは常に有効扱い（グローバルスキル）。
    /// </summary>
    public async Task<List<Skill>> SearchSkillsAsync(string prompt, int userId, string? agentRole = null)
    {
        var skills = await _db.Skills
            .Where(s => s.UserId == userId && s.IsEnabled)
            .ToListAsync();

        return skills.Where(s =>
        {
            // エージェントロールフィルタ
            if (s.BoundAgentRole != null && s.BoundAgentRole != agentRole) return false;

            // トリガーキーワードが空 → 常に発火
            if (string.IsNullOrWhiteSpace(s.TriggerKeywords)) return true;

            var keywords = s.TriggerKeywords
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim());

            return keywords.Any(k =>
                prompt.Contains(k, StringComparison.OrdinalIgnoreCase));
        }).ToList();
    }
}
```

---

### H4. 記憶固化サービス（Memory Consolidation）

**ファイル**: `AiChatApp/Services/MemoryConsolidationService.cs`（新規作成）

```csharp
using AiChatApp.Data;
using AiChatApp.Models;
using System.Text.Json;

namespace AiChatApp.Services;

public class MemoryConsolidationService
{
    private readonly AppDbContext _db;
    private readonly AiService _ai;

    public MemoryConsolidationService(AppDbContext db, AiService ai)
    {
        _db = db;
        _ai = ai;
    }

    /// <summary>
    /// 1ターン終了後に呼び出す。AIが会話から記憶すべき事実を抽出し、LongTermMemoryに保存する。
    /// 抽出なしの場合は何もしない（無駄なDB書き込みを避ける）。
    /// </summary>
    public async Task TryConsolidateAsync(string userMessage, string aiResponse, int userId)
    {
        // 抽出プロンプト：JSON形式で返させる
        string extractionPrompt = $"""
            以下の会話から、ユーザーについて長期的に記憶すべき「事実」「好み」「重要情報」を抽出してください。
            抽出できる情報がない場合は空配列を返してください。

            会話:
            ユーザー: {userMessage}
            AI: {aiResponse}

            以下のJSON形式のみで回答してください（説明不要）:
            [
              {{"content": "事実の内容", "tags": "タグ1,タグ2,タグ3"}},
              ...
            ]
            """;

        string rawJson = await _ai.ExecuteCliDirectAsync(extractionPrompt, "gemini");

        // JSONブロックを抽出（AIがマークダウンで包む場合に対応）
        int start = rawJson.IndexOf('[');
        int end = rawJson.LastIndexOf(']');
        if (start < 0 || end < 0 || end <= start) return;

        string jsonPart = rawJson[start..(end + 1)];

        try
        {
            var items = JsonSerializer.Deserialize<List<ConsolidationItem>>(jsonPart);
            if (items == null || !items.Any()) return;

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Content)) continue;

                // 既存の類似記憶と重複チェック（タグが完全一致する記憶は上書き）
                var existing = await _db.LongTermMemories
                    .FirstOrDefaultAsync(m => m.UserId == userId && m.Tags == item.Tags);

                if (existing != null)
                {
                    existing.Content = item.Content;
                    existing.LastAccessedAt = DateTime.UtcNow;
                }
                else
                {
                    _db.LongTermMemories.Add(new LongTermMemory
                    {
                        UserId = userId,
                        Content = item.Content,
                        Tags = item.Tags ?? "",
                        RelevanceScore = 80
                    });
                }
            }

            await _db.SaveChangesAsync();
        }
        catch (JsonException)
        {
            // AIが不正なJSONを返した場合は無視
        }
    }

    private record ConsolidationItem(string Content, string Tags);
}
```

**注意**: `AiService` に `ExecuteCliDirectAsync` を公開メソッドとして追加する必要がある（後述 H5-1）。

---

### H5. AiService の完全改修

**ファイル**: `AiChatApp/Services/AiService.cs`（全面書き換え）

```csharp
using System.Diagnostics;
using System.Text;
using AiChatApp.Data;
using AiChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace AiChatApp.Services;

public class AiService
{
    private readonly AppDbContext _db;
    private readonly MemorySearchService _memorySearch;

    public AiService(AppDbContext db, MemorySearchService memorySearch)
    {
        _db = db;
        _memorySearch = memorySearch;
    }

    // ─────────────────────────────────────────
    // 公開API
    // ─────────────────────────────────────────

    /// <summary>通常チャット。会話履歴・記憶・スキルを注入してAIを呼び出す。</summary>
    public async Task<string> GetResponseAsync(
        string prompt, int userId, int? chatSessionId, string provider = "gemini")
    {
        var systemPrompt = await BuildSystemPromptAsync(prompt, userId, agentRole: null);
        var history = await BuildHistoryBlockAsync(chatSessionId, limit: 10);
        string fullPrompt = string.IsNullOrEmpty(history)
            ? prompt
            : $"{history}\nUser: {prompt}";

        return await ExecuteCliDirectAsync(fullPrompt, provider, systemPrompt);
    }

    /// <summary>
    /// Harness Cooperative Mode。
    /// Orchestrator → Executor（最大2回リトライ）→ Reviewer の3段パイプライン。
    /// 各ステップの結果を AgentStep としてDBに記録する。
    /// </summary>
    public async Task<(string Html, List<AgentStep> Steps)> CooperateAsync(
        string task, int userId, int messageId, string provider = "gemini")
    {
        var steps = new List<AgentStep>();

        // ─── Step 1: Orchestrator ───
        string orchestratorPersona = """
            あなたはタスク分解の専門家（Orchestrator）です。
            ユーザーのタスクを分析し、以下のJSON形式でのみ回答してください：
            {
              "plan": "全体方針の概要（1-2文）",
              "subtasks": [
                {"id": 1, "description": "サブタスクの説明", "expectedOutput": "期待される成果物"},
                ...
              ]
            }
            """;

        var orchStep = await RunAgentStepAsync(
            role: "Orchestrator",
            persona: orchestratorPersona,
            input: task,
            messageId: messageId,
            provider: provider,
            userId: userId
        );
        steps.Add(orchStep);

        // JSONパース試行（失敗時はテキストプランとして継続）
        string planSummary = orchStep.Output;
        string subtaskBlock = task; // フォールバック
        try
        {
            var planDoc = System.Text.Json.JsonDocument.Parse(ExtractJson(orchStep.Output));
            planSummary = planDoc.RootElement.GetProperty("plan").GetString() ?? orchStep.Output;
            var subtasks = planDoc.RootElement.GetProperty("subtasks").EnumerateArray()
                .Select(s => s.GetProperty("description").GetString() ?? "")
                .Where(s => !string.IsNullOrEmpty(s));
            subtaskBlock = string.Join("\n", subtasks.Select((s, i) => $"{i + 1}. {s}"));
        }
        catch { /* JSONでない場合はそのまま使用 */ }

        // ─── Step 2: Executor（最大2回）───
        string executorPersona = """
            あなたは実装の専門家（Executor）です。
            Orchestratorの計画に基づいて、コード・文章・分析など具体的な成果物を作成してください。
            品質を重視し、完全な実装を提供してください。
            """;

        AgentStep execStep = null!;
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            string execInput = attempt == 1
                ? $"計画:\n{subtaskBlock}\n\n原タスク:\n{task}"
                : $"計画:\n{subtaskBlock}\n\n原タスク:\n{task}\n\n前回の実行結果（改善が必要）:\n{execStep.Output}\n\n上記を改善して再実装してください。";

            execStep = await RunAgentStepAsync(
                role: "Executor",
                persona: executorPersona,
                input: execInput,
                messageId: messageId,
                provider: provider,
                userId: userId,
                attemptNumber: attempt
            );
            steps.Add(execStep);

            // Reviewerに品質チェックを依頼（最終ループでは不要）
            if (attempt < 2)
            {
                bool qualityOk = await QuickQualityCheckAsync(task, execStep.Output, provider);
                if (qualityOk) break;
                execStep.WasAccepted = false;
                await _db.SaveChangesAsync();
            }
        }

        // ─── Step 3: Reviewer ───
        string reviewerPersona = """
            あなたは評審の専門家（Reviewer）です。
            以下の情報を受け取り、ユーザーへの最終的な回答を作成してください。
            - 元のタスクの要件が満たされているか確認する
            - 不完全な点があれば補足する
            - Markdownで整形して読みやすく提示する
            """;

        var reviewStep = await RunAgentStepAsync(
            role: "Reviewer",
            persona: reviewerPersona,
            input: $"元タスク:\n{task}\n\n計画:\n{planSummary}\n\n実行結果:\n{execStep.Output}",
            messageId: messageId,
            provider: provider,
            userId: userId
        );
        steps.Add(reviewStep);
        reviewStep.WasAccepted = true;
        await _db.SaveChangesAsync();

        // ─── HTML構造を生成 ───
        string html = BuildCooperativeHtml(steps, reviewStep.Output);
        return (html, steps);
    }

    /// <summary>外部から直接CLIを呼べる公開メソッド（MemoryConsolidationServiceから使用）</summary>
    public Task<string> ExecuteCliDirectAsync(string prompt, string provider, string? systemPrompt = null)
        => ExecuteCliAsync(prompt, provider, systemPrompt);

    // ─────────────────────────────────────────
    // 内部実装
    // ─────────────────────────────────────────

    private async Task<AgentStep> RunAgentStepAsync(
        string role, string persona, string input, int messageId,
        string provider, int userId, int attemptNumber = 1)
    {
        // ロールに応じたスキルをシステムプロンプトに追加
        var roleSkills = await _memorySearch.SearchSkillsAsync(input, userId, agentRole: role);
        string fullPersona = persona;
        if (roleSkills.Any())
        {
            fullPersona += "\n\n[追加スキル指示]:\n" +
                string.Join("\n", roleSkills.Select(s => $"- {s.Description}"));
        }

        var sw = Stopwatch.StartNew();
        string output = await ExecuteCliAsync(input, provider, fullPersona);
        sw.Stop();

        var step = new AgentStep
        {
            MessageId = messageId,
            Role = role,
            Persona = fullPersona,
            Input = input,
            Output = output,
            AttemptNumber = attemptNumber,
            WasAccepted = true,
            DurationMs = (int)sw.ElapsedMilliseconds
        };
        _db.AgentSteps.Add(step);
        await _db.SaveChangesAsync();

        return step;
    }

    private async Task<bool> QuickQualityCheckAsync(string originalTask, string execution, string provider)
    {
        string checkPrompt = $"""
            以下のタスクに対して実行結果が十分か判断してください。
            タスク: {originalTask}
            実行結果: {execution}
            
            結果が十分であれば "OK"、不十分であれば "RETRY" のみを返してください。
            """;

        string result = await ExecuteCliAsync(checkPrompt, provider, systemPrompt: null);
        return result.Contains("OK", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> BuildSystemPromptAsync(string prompt, int userId, string? agentRole)
    {
        var memories = await _memorySearch.SearchAsync(prompt, userId);
        var skills = await _memorySearch.SearchSkillsAsync(prompt, userId, agentRole);

        var sb = new StringBuilder("あなたは高度なAIアシスタントです。");

        if (memories.Any())
        {
            sb.Append("\n\n[ユーザーの既知情報・長期記憶]:\n");
            foreach (var m in memories) sb.Append($"- {m.Content}\n");
        }

        if (skills.Any())
        {
            sb.Append("\n\n[有効なスキル指示]:\n");
            foreach (var s in skills) sb.Append($"- {s.Description}\n");
        }

        return sb.ToString();
    }

    private async Task<string> BuildHistoryBlockAsync(int? chatSessionId, int limit)
    {
        if (!chatSessionId.HasValue) return "";

        var msgs = await _db.Messages
            .Where(m => m.ChatSessionId == chatSessionId.Value)
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .OrderBy(m => m.Timestamp)
            .ToListAsync();

        if (!msgs.Any()) return "";

        var sb = new StringBuilder("[会話履歴]:\n");
        foreach (var m in msgs)
            sb.Append($"{(m.IsAi ? "Assistant" : "User")}: {m.Content}\n");

        return sb.ToString();
    }

    private static string BuildCooperativeHtml(List<AgentStep> steps, string finalOutput)
    {
        var sb = new StringBuilder("<div class='space-y-4'>");

        foreach (var step in steps.Where(s => s.Role != "Reviewer"))
        {
            string badgeClass = step.Role == "Orchestrator" ? "badge-info" : "badge-success";
            string retryBadge = step.AttemptNumber > 1
                ? $"<span class='badge badge-warning badge-xs ml-1'>Retry #{step.AttemptNumber}</span>" : "";
            string acceptedIcon = step.WasAccepted ? "" :
                "<span class='badge badge-error badge-xs ml-1'>Rejected</span>";

            sb.Append($"""
                <div class='collapse collapse-arrow bg-base-300/30 border border-base-content/10'>
                    <input type='checkbox' />
                    <div class='collapse-title text-sm font-medium flex items-center gap-2'>
                        <span class='badge {badgeClass} badge-sm'>{step.Role}</span>
                        {retryBadge}{acceptedIcon}
                        <span class='opacity-50 text-xs ml-auto'>{step.DurationMs}ms</span>
                    </div>
                    <div class='collapse-content text-xs opacity-80'>
                        <pre class='whitespace-pre-wrap font-sans'>{System.Net.WebUtility.HtmlEncode(step.Output)}</pre>
                    </div>
                </div>
                """);
        }

        sb.Append($"""
            <div class='divider text-xs opacity-30'>FINAL OUTPUT</div>
            <div class='prose prose-sm max-w-none'>{System.Net.WebUtility.HtmlEncode(finalOutput)}</div>
            </div>
            """);

        return sb.ToString();
    }

    private static string ExtractJson(string text)
    {
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start >= 0 && end > start) return text[start..(end + 1)];
        return "{}";
    }

    private async Task<string> ExecuteCliAsync(string prompt, string provider, string? systemPrompt = null)
    {
        // Issue 1 修正済み: ArgumentList を使用してインジェクションを防ぐ
        string fileName = provider switch
        {
            "gh-copilot" => "copilot",
            "claude" => "claude",
            _ => "gemini"
        };

        string fullPrompt = string.IsNullOrEmpty(systemPrompt)
            ? prompt
            : $"System: {systemPrompt}\n\nUser: {prompt}";

        var processInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // ArgumentList でシェル展開を回避（Issue 1 の修正）
        processInfo.ArgumentList.Add("-p");
        processInfo.ArgumentList.Add(fullPrompt);

        if (provider == "claude")
            processInfo.ArgumentList.Add("--dangerously-skip-permissions");
        else
            processInfo.ArgumentList.Add("--yolo");

        try
        {
            using var process = Process.Start(processInfo);
            if (process == null) return $"Error: Could not start {provider} CLI.";

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(error))
                return $"Error from {provider}: {error}";

            return string.IsNullOrWhiteSpace(output) ? "No response received." : output;
        }
        catch (Exception ex)
        {
            return $"Exception: {ex.Message}";
        }
    }
}
```

---

### H6. DI登録の更新

**ファイル**: `AiChatApp/Program.cs`（サービス登録部分のみ）

```csharp
// 変更前
builder.Services.AddScoped<AiService>();

// 変更後
builder.Services.AddScoped<MemorySearchService>();
builder.Services.AddScoped<MemoryConsolidationService>();
builder.Services.AddScoped<AiService>();
```

---

### H7. `/api/chat` エンドポイントの更新

**ファイル**: `AiChatApp/Program.cs`（`/api/chat` エンドポイント部分）

```csharp
app.MapPost("/api/chat", async (HttpContext context, AppDbContext db, AiService ai,
    MemoryConsolidationService consolidation, ClaimsPrincipal user) =>
{
    var form = await context.Request.ReadFormAsync();
    var content = form["content"].ToString();
    var provider = form["provider"].ToString() is { Length: > 0 } p ? p : "gemini";
    var isCooperative = form["mode"] == "cooperative";
    int? sessionId = int.TryParse(form["sessionId"].ToString(), out var sid) ? sid : null;
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // セッション取得または作成
    ChatSession? session = sessionId.HasValue
        ? await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId)
        : await db.ChatSessions.Where(s => s.UserId == userId).OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync();

    if (session == null)
    {
        session = new ChatSession
        {
            UserId = userId,
            Title = content.Length > 20 ? content[..20] + "..." : content
        };
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync();
    }

    // ユーザーメッセージを保存
    var uMsg = new Message { ChatSessionId = session.Id, Content = content, IsAi = false };
    db.Messages.Add(uMsg);
    await db.SaveChangesAsync();

    string aiResponse;
    if (isCooperative)
    {
        // AIメッセージを先にDBに仮保存してIDを取得（AgentStepが参照するため）
        var aMsg = new Message { ChatSessionId = session.Id, Content = "", IsAi = true };
        db.Messages.Add(aMsg);
        await db.SaveChangesAsync();

        var (html, _) = await ai.CooperateAsync(content, userId, aMsg.Id, provider);
        aMsg.Content = html;
        await db.SaveChangesAsync();

        aiResponse = html;
        // 記憶固化はCooperativeモードでは最終出力のみ対象
        _ = Task.Run(() => consolidation.TryConsolidateAsync(content, html, userId));

        return Results.Content(RenderMessage(aMsg), "text/html");
    }
    else
    {
        aiResponse = await ai.GetResponseAsync(content, userId, session.Id, provider);
        var aMsg = new Message { ChatSessionId = session.Id, Content = aiResponse, IsAi = true };
        db.Messages.Add(aMsg);
        await db.SaveChangesAsync();

        // 記憶固化をバックグラウンドで非同期実行（レスポンスをブロックしない）
        _ = Task.Run(() => consolidation.TryConsolidateAsync(content, aiResponse, userId));

        return Results.Content(RenderMessage(aMsg), "text/html");
    }
}).DisableAntiforgery().RequireAuthorization();
```

---

### H8. ユーザー登録時のデフォルトスキル作成

**ファイル**: `AiChatApp/Program.cs`（`/api/auth/register` エンドポイント）

```csharp
app.MapPost("/api/auth/register", async ([FromForm] string username, [FromForm] string password, AppDbContext db) =>
{
    if (await db.Users.AnyAsync(u => u.Username == username))
        return Results.BadRequest("User already exists");

    var user = new User
    {
        Username = username,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(password)
    };
    db.Users.Add(user);
    await db.SaveChangesAsync(); // IDを確定させる

    // ユーザー固有のデフォルトスキルを作成
    db.Skills.AddRange(
        new Skill
        {
            UserId = user.Id,
            Name = "CodeOptimizer",
            Description = "あなたはコード最適化の専門家です。パフォーマンス・可読性・安全性を分析して改善提案を行ってください。",
            ExampleInput = "このコードを最適化して",
            TriggerKeywords = "code,コード,最適化,optimize,refactor",
            BoundAgentRole = "Executor"
        },
        new Skill
        {
            UserId = user.Id,
            Name = "CreativeWriter",
            Description = "あなたは創造的なライターです。生き生きとした表現と文学的な言語を使って文章を作成してください。",
            ExampleInput = "夕焼けについて書いて",
            TriggerKeywords = "write,書いて,creative,文章,story"
        },
        new Skill
        {
            UserId = user.Id,
            Name = "Translator",
            Description = "あなたは多言語の翻訳専門家です。正確で自然な翻訳を提供してください。",
            ExampleInput = "翻訳して",
            TriggerKeywords = "翻訳,translate,translation"
        }
    );
    await db.SaveChangesAsync();

    return Results.Redirect("/login");
}).DisableAntiforgery();
```

---

### H9. スキルAPIのユーザースコープ対応

**ファイル**: `AiChatApp/Program.cs`（スキル関連エンドポイント）

```csharp
// GET /api/skills
app.MapGet("/api/skills", async (AppDbContext db, ClaimsPrincipal user) =>
{
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var skills = await db.Skills.Where(s => s.UserId == userId).ToListAsync();
    // (HTMLテンプレートは現状維持、TriggerKeywordsの表示を追加)
    return Results.Content(string.Concat(skills.Select(s => $@"
        <div class='flex items-center justify-between p-3 bg-base-200 rounded-lg'>
            <div>
                <div class='font-bold text-sm'>{s.Name}</div>
                <div class='text-xs opacity-60'>{s.Description}</div>
                {(string.IsNullOrEmpty(s.TriggerKeywords) ? "" : $"<div class='text-xs opacity-40 mt-1'>Keywords: {s.TriggerKeywords}</div>")}
            </div>
            <input type='checkbox' class='toggle toggle-primary toggle-sm' {(s.IsEnabled ? "checked" : "")}
                   hx-post='/api/skills/toggle/{s.Id}' hx-swap='none' />
        </div>")), "text/html");
}).RequireAuthorization();

// POST /api/skills/toggle/{id}
app.MapPost("/api/skills/toggle/{id}", async (int id, AppDbContext db, ClaimsPrincipal user) =>
{
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var skill = await db.Skills.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
    if (skill != null)
    {
        skill.IsEnabled = !skill.IsEnabled;
        await db.SaveChangesAsync();
    }
    return Results.Ok();
}).RequireAuthorization();
```

---

## フェーズ2：Harness SSEストリーミングの実装

### S1. バックエンド：SSEエンドポイントの追加

**ファイル**: `AiChatApp/Program.cs`（新規エンドポイント追加）

CooperativeモードのSSEエンドポイント。各エージェントステップが完了するたびにフロントへHTMLチャンクを送信する。

```csharp
app.MapPost("/api/chat/cooperate/stream", async (
    HttpContext context, AppDbContext db, AiService ai,
    MemoryConsolidationService consolidation, ClaimsPrincipal user) =>
{
    var form = await context.Request.ReadFormAsync();
    var content = form["content"].ToString();
    var provider = form["provider"].ToString() is { Length: > 0 } p ? p : "gemini";
    int? sessionId = int.TryParse(form["sessionId"].ToString(), out var sid) ? sid : null;
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    context.Response.Headers.Append("Content-Type", "text/event-stream");
    context.Response.Headers.Append("Cache-Control", "no-cache");
    context.Response.Headers.Append("X-Accel-Buffering", "no");

    async Task SendEvent(string eventName, string data)
    {
        await context.Response.WriteAsync($"event: {eventName}\ndata: {data.Replace("\n", "\\n")}\n\n");
        await context.Response.Body.FlushAsync();
    }

    // セッション作成
    ChatSession? session = sessionId.HasValue
        ? await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId)
        : null;
    if (session == null)
    {
        session = new ChatSession { UserId = userId, Title = content.Length > 20 ? content[..20] + "..." : content };
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync();
    }

    var uMsg = new Message { ChatSessionId = session.Id, Content = content, IsAi = false };
    db.Messages.Add(uMsg);
    var aMsg = new Message { ChatSessionId = session.Id, Content = "", IsAi = true };
    db.Messages.Add(aMsg);
    await db.SaveChangesAsync();

    // セッションIDをフロントへ通知（新規作成時）
    await SendEvent("session", session.Id.ToString());

    // 各ステップの開始通知
    await SendEvent("step-start", "Orchestrator");
    var (html, steps) = await ai.CooperateAsync(content, userId, aMsg.Id, provider,
        onStepComplete: async (role, stepHtml) =>
        {
            await SendEvent("step-complete", $"{role}|{stepHtml}");
        });

    aMsg.Content = html;
    await db.SaveChangesAsync();

    await SendEvent("final", html);
    await SendEvent("done", "");

    _ = Task.Run(() => consolidation.TryConsolidateAsync(content, html, userId));
}).DisableAntiforgery().RequireAuthorization();
```

**AiService.CooperateAsync のシグネチャ変更**（コールバック追加）:

```csharp
public async Task<(string Html, List<AgentStep> Steps)> CooperateAsync(
    string task, int userId, int messageId, string provider = "gemini",
    Func<string, string, Task>? onStepComplete = null)
{
    // ... 各 RunAgentStepAsync の後に以下を追加 ...
    if (onStepComplete != null)
        await onStepComplete(step.Role, BuildStepHtml(step));
    // ...
}
```

---

### S2. フロントエンド：Cooperativeモードのストリーミング化

**ファイル**: `AiChatApp/wwwroot/index.html`

既存の `<form id="chat-form">` の `hx-post` 属性を削除し、カスタムJavaScriptで送信を処理する。

#### S2-1. フォームのHTMX属性を変更

```html
<!-- 変更前 -->
<form id="chat-form" hx-post="/api/chat" hx-target="#chat-box" hx-swap="beforeend" ...>

<!-- 変更後：通常モードはHTMX継続、コールバックをoverride -->
<form id="chat-form" hx-post="/api/chat" hx-target="#chat-box" hx-swap="beforeend"
      hx-indicator="#loading-spinner"
      hx-on::before-request="return handleBeforeRequest(event)"
      class="...">
```

#### S2-2. JavaScriptを `</body>` 直前に追加

```javascript
<script>
// Cooperativeモードが有効なら独自のSSE処理を行い、HTMXを止める
function handleBeforeRequest(event) {
    const isCooperative = document.querySelector('input[name="mode"]')?.checked;
    if (!isCooperative) return true; // 通常モード: HTMXに任せる

    event.preventDefault();
    sendCooperativeMessage();
    return false;
}

async function sendCooperativeMessage() {
    const form = document.getElementById('chat-form');
    const input = document.getElementById('chat-input');
    const chatBox = document.getElementById('chat-box');
    const content = input.value.trim();
    if (!content) return;

    // UI無効化
    document.getElementById('send-btn').disabled = true;
    document.getElementById('loading-spinner').style.display = 'inline-block';

    // ユーザーメッセージを即時表示
    const sessionId = document.getElementById('session-id-input').value;
    chatBox.insertAdjacentHTML('beforeend', renderUserBubble(content));
    input.value = '';
    resetInputHeight();
    chatBox.scrollTop = chatBox.scrollHeight;

    // エージェント進行中バブルを追加
    const progressId = 'progress-' + Date.now();
    chatBox.insertAdjacentHTML('beforeend', renderProgressBubble(progressId));
    chatBox.scrollTop = chatBox.scrollHeight;

    const formData = new FormData(form);
    formData.set('content', content);

    try {
        const response = await fetch('/api/chat/cooperate/stream', {
            method: 'POST',
            body: formData
        });

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        const progressEl = document.getElementById(progressId);

        while (true) {
            const { done, value } = await reader.read();
            if (done) break;

            const text = decoder.decode(value, { stream: true });
            const lines = text.split('\n');
            let eventName = '';

            for (const line of lines) {
                if (line.startsWith('event: ')) {
                    eventName = line.slice(7).trim();
                } else if (line.startsWith('data: ')) {
                    const data = line.slice(6).trim();
                    handleSSEEvent(eventName, data, progressEl, progressId, chatBox);
                }
            }
        }
    } catch (e) {
        console.error('SSE error:', e);
    } finally {
        document.getElementById('send-btn').disabled = false;
        document.getElementById('loading-spinner').style.display = 'none';
    }
}

function handleSSEEvent(event, data, progressEl, progressId, chatBox) {
    if (event === 'session') {
        document.getElementById('session-id-input').value = data;
        // サイドバーのチャットリストを更新
        htmx.trigger(document.body, 'loadChatList');
    } else if (event === 'step-start') {
        if (progressEl) {
            progressEl.querySelector('.step-status').textContent = `${data} thinking...`;
        }
    } else if (event === 'step-complete') {
        const [role, stepHtml] = data.split('|', 2);
        if (progressEl) {
            progressEl.insertAdjacentHTML('beforeend',
                `<div class="step-result mt-2">${stepHtml.replace(/\\n/g, '\n')}</div>`);
            chatBox.scrollTop = chatBox.scrollHeight;
        }
    } else if (event === 'final') {
        // プログレスバブルを最終出力で置き換え
        const finalHtml = data.replace(/\\n/g, '\n');
        const progressContainer = document.getElementById(progressId);
        if (progressContainer) {
            progressContainer.outerHTML = renderAiBubble(finalHtml);
        }
        // Markdownレンダリング適用
        document.querySelectorAll('.markdown').forEach(el => {
            if (el.dataset.rendered !== 'true') {
                el.innerHTML = marked.parse(el.textContent);
                el.dataset.rendered = 'true';
            }
        });
        chatBox.scrollTop = chatBox.scrollHeight;
    }
}

function renderUserBubble(content) {
    return `<div class="chat chat-end group message-bubble-container">
        <div class="chat-bubble shadow-sm bg-primary text-primary-content markdown leading-relaxed p-3 md:p-4 rounded-[18px] rounded-tr-none">
            ${escapeHtml(content)}
        </div>
    </div>`;
}

function renderProgressBubble(id) {
    return `<div id="${id}" class="chat chat-start group message-bubble-container">
        <div class="chat-bubble shadow-sm bg-base-200 text-base-content border border-base-300 leading-relaxed p-3 md:p-4 rounded-[18px] rounded-bl-none">
            <div class="flex items-center gap-2">
                <span class="loading loading-dots loading-xs"></span>
                <span class="step-status text-xs opacity-60">Orchestrator thinking...</span>
            </div>
        </div>
    </div>`;
}

function renderAiBubble(htmlContent) {
    return `<div class="chat chat-start group message-bubble-container">
        <div class="chat-bubble shadow-sm bg-base-200 text-base-content border border-base-300 markdown leading-relaxed p-3 md:p-4 rounded-[18px] rounded-bl-none">
            ${htmlContent}
        </div>
    </div>`;
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}
</script>
```

---

## フェーズ3：記憶管理UIの拡張

### M1. 記憶追加フォームのタグ複数入力対応

**ファイル**: `AiChatApp/wwwroot/index.html`（記憶追加モーダル部分）

既存の記憶追加フォームの `tags` フィールドのプレースホルダーを変更し、カンマ区切りを明示する：

```html
<!-- 変更前 -->
<input type="text" name="tags" placeholder="Tags (e.g. work, code)" ... />

<!-- 変更後 -->
<input type="text" name="tags" 
       placeholder="タグをカンマ区切りで入力 (例: code,csharp,performance)" 
       class="input input-bordered input-sm w-full" />
<p class="text-xs opacity-50 mt-1">複数タグを入力するとより精度の高い記憶検索が行われます</p>
```

### M2. 記憶の重要度表示

`/api/memories` のHTMLレスポンス（`Program.cs`）に重要度バーを追加：

```csharp
// 既存のメモリレンダリング部分に RelevanceScore の表示を追加
$@"<div class='flex flex-col gap-1 p-3 bg-base-200 rounded-lg group'>
    <div class='flex justify-between items-start'>
        <span class='text-xs font-bold text-primary'>{m.Tags}</span>
        <div class='flex items-center gap-2'>
            <div class='w-12 bg-base-300 rounded-full h-1'>
                <div class='bg-primary h-1 rounded-full' style='width:{m.RelevanceScore}%'></div>
            </div>
            <button hx-delete='/api/memories/{m.Id}' hx-target='closest div' hx-swap='outerHTML' 
                    class='btn btn-ghost btn-xs text-error opacity-0 group-hover:opacity-100'>Delete</button>
        </div>
    </div>
    <p class='text-sm'>{m.Content}</p>
    <p class='text-xs opacity-30'>参照 {m.AccessCount}回 · {m.CreatedAt:yyyy/MM/dd}</p>
</div>"
```

---

## フェーズ4：.gitignore の修正

**ファイル**: プロジェクトルートの `.gitignore`（存在しない場合は新規作成）

```
# SQLite database files
*.db
*.db-shm
*.db-wal
```

既存のDBをGit管理から除外：
```bash
git rm --cached AiChatApp/chat.db
```

---

## 実装チェックリスト

### フェーズ1（Hermes Agent）
- [ ] `LongTermMemory.cs` にフィールド追加（RelevanceScore, AccessCount, LastAccessedAt）
- [ ] `Skill.cs` に UserId, TriggerKeywords, BoundAgentRole を追加
- [ ] `AgentStep.cs` を新規作成
- [ ] `AppDbContext.cs` に AgentSteps 追加、HasData シード削除
- [ ] `MemorySearchService.cs` を新規作成
- [ ] `MemoryConsolidationService.cs` を新規作成
- [ ] `AiService.cs` を全面改修
- [ ] `Program.cs`：DI登録更新
- [ ] `Program.cs`：/api/chat エンドポイント更新（history注入・consolidation呼び出し）
- [ ] `Program.cs`：/api/auth/register にデフォルトスキル作成を追加
- [ ] `Program.cs`：/api/skills と /api/skills/toggle にUserIdフィルタ追加

### フェーズ2（Harnessストリーミング）
- [ ] `Program.cs`：/api/chat/cooperate/stream エンドポイント追加
- [ ] `AiService.CooperateAsync` にコールバック引数追加
- [ ] `index.html`：フォームにSSEハンドラを追加
- [ ] `index.html`：renderProgressBubble等のJS関数を追加

### フェーズ3（記憶UI）
- [ ] `index.html`：タグ入力フィールドのプレースホルダー更新
- [ ] `Program.cs`：/api/memories の HTML に重要度・参照回数表示追加

### フェーズ4（インフラ）
- [ ] `.gitignore` に *.db 追加
- [ ] `git rm --cached AiChatApp/chat.db` 実行
- [ ] `chat.db` 削除してアプリ再起動（スキーマ再生成）

---

## 注意事項

1. **DBの再作成**: モデル変更後は必ず `chat.db` を削除してからアプリを起動すること。`EnsureCreated()` はスキーマ変更を自動適用しない。

2. **MemoryConsolidationService の非同期呼び出し**: `Task.Run()` でバックグラウンド実行しているが、アプリケーション終了時にタスクが途中でキャンセルされる可能性がある。本番環境では `IHostedService` や `BackgroundService` に移行することを推奨。

3. **SSEとHTMX の共存**: 通常モード（cooperative=false）は引き続きHTMXで動作する。SSEは cooperative=true の場合のみ `fetch` API で処理する。`hx-on::before-request` で分岐する点に注意。

4. **AiService のコンストラクタ変更**: `MemorySearchService` が新たな依存関係として追加されるため、DI登録順序が重要。`MemorySearchService` を先に登録すること。
