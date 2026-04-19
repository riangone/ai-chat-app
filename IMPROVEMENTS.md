# AiChatApp 改善仕様書

本文書は、コードレビューで特定された問題点の修正仕様を記述する。実装者はこの仕様に従ってコードを変更すること。

---

## Issue 1: コマンドインジェクション脆弱性（優先度：Critical）

### 問題

`AiChatApp/Services/AiService.cs` の `ExecuteCliAsync` メソッド（111行目）において、ユーザー入力を単純な文字列エスケープ（`Replace("\"", "\\\"")`）でシェル引数に埋め込んでいる。バッククォート、`$()`、改行などを含む入力でOSコマンドインジェクションが可能。

```csharp
// 現在の危険なコード
string escapedPrompt = fullPrompt.Replace("\"", "\\\"");
string arguments = $"-p \"{escapedPrompt}\" --yolo";
```

### 修正仕様

`ProcessStartInfo.ArgumentList` を使用してシェル経由の引数展開を回避する。`Arguments` プロパティは使用しない。

```csharp
// 修正後
var processInfo = new ProcessStartInfo
{
    FileName = fileName,
    RedirectStandardInput = true,   // プロンプトは標準入力で渡す
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false,
    CreateNoWindow = true
};

// プロバイダーごとのフラグのみ ArgumentList に追加（ユーザー入力は含めない）
if (provider == "gh-copilot")
{
    processInfo.FileName = "copilot";
    processInfo.ArgumentList.Add("--yolo");
}
else if (provider == "claude")
{
    processInfo.FileName = "claude";
    processInfo.ArgumentList.Add("--dangerously-skip-permissions");
}
else
{
    processInfo.FileName = "gemini";
    processInfo.ArgumentList.Add("--yolo");
}
```

ただし、対象CLIが標準入力からのプロンプト受付をサポートしない場合は、`-p` フラグ用に `ArgumentList.Add("-p")` + `ArgumentList.Add(fullPrompt)` の2要素追加方式を使うこと（シェル展開が発生しないため安全）。

```csharp
// -p フラグを使う場合の安全な方式
processInfo.ArgumentList.Add("-p");
processInfo.ArgumentList.Add(fullPrompt);  // エスケープ不要
```

`Arguments` プロパティへの文字列連結は絶対に使用しないこと。

---

## Issue 2: 会話コンテキストの欠如（優先度：High）

### 問題

`AiService.GetResponseAsync` は毎回1ターン分のメッセージしか送信しない。AIは直前の会話を参照できず、文脈を持った返答ができない。

### 修正仕様

#### 2-1. `GetResponseAsync` のシグネチャ変更

`chatSessionId` パラメータを追加し、DBから過去メッセージを取得する。

```csharp
// 変更前
public async Task<string> GetResponseAsync(string prompt, int userId, string provider = "gemini")

// 変更後
public async Task<string> GetResponseAsync(string prompt, int userId, int? chatSessionId, string provider = "gemini")
```

#### 2-2. 過去メッセージの取得と注入

`BuildEnhancedPrompt` 内で、直近 **10件** のメッセージをDBから取得し、会話履歴としてプロンプトに含める。

```csharp
private async Task<(string system, string full)> BuildEnhancedPrompt(
    string prompt, int userId, int? chatSessionId)
{
    // 既存のメモリ・スキル取得処理はそのまま維持 ...

    // 会話履歴の取得（直近10件、時系列順）
    string historyBlock = "";
    if (chatSessionId.HasValue)
    {
        var recentMessages = await _dbContext.Messages
            .Where(m => m.ChatSessionId == chatSessionId.Value)
            .OrderByDescending(m => m.Timestamp)
            .Take(10)
            .OrderBy(m => m.Timestamp)  // 取得後に昇順に戻す
            .ToListAsync();

        if (recentMessages.Any())
        {
            var sb2 = new StringBuilder();
            sb2.Append("\n[会話履歴]:\n");
            foreach (var msg in recentMessages)
            {
                string role = msg.IsAi ? "Assistant" : "User";
                sb2.Append($"{role}: {msg.Content}\n");
            }
            historyBlock = sb2.ToString();
        }
    }

    // システムプロンプトに履歴を追加
    sb.Append(historyBlock);

    return (sb.ToString(), prompt);
}
```

#### 2-3. 呼び出し元の修正

`Program.cs` の `/api/chat` エンドポイント（122行目付近）で `chatSessionId` を渡す。

```csharp
// 変更前
aiResponse = await ai.GetResponseAsync(content, userId, provider);

// 変更後
aiResponse = await ai.GetResponseAsync(content, userId, session.Id, provider);
```

---

## Issue 3: スキルのスコープ問題（優先度：Medium）

### 問題

`Skills` テーブルに `UserId` が存在しないため、あるユーザーがスキルを ON/OFF すると全ユーザーに影響する。

### 修正仕様

#### 3-1. `Skill` モデルの変更

`AiChatApp/Models/Skill.cs` に `UserId` を追加する。

```csharp
public class Skill
{
    public int Id { get; set; }
    public int UserId { get; set; }          // 追加
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExampleInput { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    public User? User { get; set; }           // ナビゲーションプロパティ（任意）
}
```

#### 3-2. シードデータの削除

`AppDbContext.OnModelCreating` の `HasData` シードを削除する。ユーザーごとにスキルを持つ設計では、グローバルシードデータは不適切。

代わりに、新規ユーザー登録時（`/api/auth/register` エンドポイント）にデフォルトスキル3件をそのユーザーのIDで挿入する。

```csharp
// /api/auth/register 内、db.SaveChangesAsync() の前に追加
var defaultSkills = new List<Skill>
{
    new Skill { UserId = user.Id, Name = "CodeOptimizer", Description = "你是一个代码优化专家，请分析代码的性能、可读性和安全性并给出改进建议。", ExampleInput = "优化这段代码: for(int i=0; i<list.Count; i++)" },
    new Skill { UserId = user.Id, Name = "CreativeWriter", Description = "你是一个富有创意的作家，擅长使用生动、形象的语言进行文学创作。", ExampleInput = "写一段关于夕阳的描写" },
    new Skill { UserId = user.Id, Name = "Translator",     Description = "你是一个精通多国语言的翻译官，请提供准确且信达雅的翻译。", ExampleInput = "翻译: Hello, how are you?" }
};
db.Skills.AddRange(defaultSkills);
```

#### 3-3. スキル関連APIの修正

`/api/skills` と `/api/skills/toggle/{id}` に `UserId` フィルタを追加する。

```csharp
// GET /api/skills
var skills = await db.Skills
    .Where(s => s.UserId == userId)   // 追加
    .ToListAsync();

// POST /api/skills/toggle/{id}
var skill = await db.Skills
    .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);  // FindAsync から変更
```

#### 3-4. `AiService.BuildEnhancedPrompt` の修正

スキル取得時に `UserId` フィルタを追加。

```csharp
var skills = await _dbContext.Skills
    .Where(s => s.IsEnabled && s.UserId == userId)  // userId フィルタ追加
    .ToListAsync();
```

#### 3-5. DBマイグレーション

スキーマ変更のため、既存の `chat.db` を削除してアプリを再起動する（開発環境）。本番環境では EF Core マイグレーションを追加する。

```bash
dotnet ef migrations add AddUserIdToSkills --project AiChatApp
dotnet ef database update --project AiChatApp
```

---

## Issue 4: `CooperateAsync` のプロバイダーハードコード（優先度：Medium）

### 問題

`AiService.CooperateAsync`（69行目）は常に `gemini` を使用する。`Program.cs` はフォームから `provider` を受け取り `isCooperative` 分岐に入るが、`CooperateAsync` にプロバイダーを渡していない。

### 修正仕様

#### 4-1. `CooperateAsync` のシグネチャ変更

```csharp
// 変更前
public async Task<string> CooperateAsync(string task, int userId)

// 変更後
public async Task<string> CooperateAsync(string task, int userId, string provider = "gemini")
```

#### 4-2. 内部の `CallAgentAsync` 呼び出しに `provider` を渡す

```csharp
string plan      = await CallAgentAsync("Orchestrator", plannerPersona,  $"请规划以下任务：{task}",                                              provider);
string execution = await CallAgentAsync("Executor",     coderPersona,    $"根据此规划执行任务：\n规划内容：{plan}\n原始任务：{task}",             provider);
string finalResult = await CallAgentAsync("Reviewer",   reviewerPersona, $"请评审以下内容并给出最终回复：\n规划：{plan}\n执行结果：{execution}\n原始需求：{task}", provider);
```

#### 4-3. `Program.cs` の呼び出し側を修正

```csharp
// 変更前
aiResponse = await ai.CooperateAsync(content, userId);

// 変更後
aiResponse = await ai.CooperateAsync(content, userId, provider);
```

---

## Issue 5: `chat.db` の Git 管理除外（優先度：Low）

### 問題

SQLiteのデータベースファイル `chat.db` が Git で追跡されている（`git status` で `M AiChatApp/chat.db` が表示）。ユーザーデータや認証情報が誤ってリポジトリに混入するリスクがある。

### 修正仕様

#### 5-1. `.gitignore` に追記

プロジェクトルートの `.gitignore`（存在しない場合は新規作成）に以下を追加する。

```
# SQLite database
*.db
*.db-shm
*.db-wal
```

#### 5-2. 既存の追跡を解除

```bash
git rm --cached AiChatApp/chat.db
git commit -m "chore: remove chat.db from version control"
```

---

## Issue 6: AIレスポンスのストリーミング対応（優先度：Low）

### 問題

現状、AI CLIの応答が完全に返るまでHTTPレスポンスが返らない。長い応答では数十秒UIがフリーズする。

### 修正仕様

#### 6-1. バックエンド：SSE（Server-Sent Events）エンドポイントの追加

既存の `POST /api/chat` はそのまま維持し、新たにストリーミング専用エンドポイントを追加する。

```csharp
app.MapPost("/api/chat/stream", async (HttpContext context, AppDbContext db, AiService ai, ClaimsPrincipal user) => {
    var form = await context.Request.ReadFormAsync();
    var content = form["content"].ToString();
    var provider = form["provider"].ToString() ?? "gemini";
    var sessionId = int.TryParse(form["sessionId"].ToString(), out var sid) ? sid : (int?)null;
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    context.Response.Headers.Append("Content-Type", "text/event-stream");
    context.Response.Headers.Append("Cache-Control", "no-cache");
    context.Response.Headers.Append("X-Accel-Buffering", "no");

    await foreach (var chunk in ai.GetResponseStreamAsync(content, userId, sessionId, provider))
    {
        await context.Response.WriteAsync($"data: {chunk}\n\n");
        await context.Response.Body.FlushAsync();
    }

    await context.Response.WriteAsync("data: [DONE]\n\n");
}).DisableAntiforgery().RequireAuthorization();
```

#### 6-2. `AiService` にストリーミングメソッドを追加

```csharp
public async IAsyncEnumerable<string> GetResponseStreamAsync(
    string prompt, int userId, int? chatSessionId, string provider = "gemini")
{
    var (systemPrompt, finalPrompt) = await BuildEnhancedPrompt(prompt, userId, chatSessionId);
    
    // ArgumentList で安全に引数構築（Issue 1 の修正と組み合わせる）
    var processInfo = new ProcessStartInfo
    {
        FileName = provider == "gh-copilot" ? "copilot" : provider == "claude" ? "claude" : "gemini",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    processInfo.ArgumentList.Add("-p");
    processInfo.ArgumentList.Add($"System: {systemPrompt}\n\nUser: {finalPrompt}");
    if (provider == "claude") processInfo.ArgumentList.Add("--dangerously-skip-permissions");
    else processInfo.ArgumentList.Add("--yolo");

    using var process = Process.Start(processInfo)
        ?? throw new InvalidOperationException("Process start failed");

    var buffer = new char[64];
    int read;
    while ((read = await process.StandardOutput.ReadAsync(buffer, 0, buffer.Length)) > 0)
    {
        yield return new string(buffer, 0, read);
    }
    await process.WaitForExitAsync();
}
```

#### 6-3. フロントエンド：`wwwroot/index.html` の修正

フォーム送信をHTMXから `fetch` + `EventSource` に切り替え、受信チャンクをチャットバブルに追記する。

```javascript
// チャット送信フォームのsubmitイベントを上書き
async function sendMessageStream(formData) {
    const sessionId = document.getElementById('chat-box').dataset.sessionId;
    formData.append('sessionId', sessionId);

    // ユーザーメッセージを即時表示
    appendMessage(formData.get('content'), false);

    // AIバブルを空で追加
    const aiBubble = appendMessage('', true);
    const contentEl = aiBubble.querySelector('.chat-bubble');

    const response = await fetch('/api/chat/stream', {
        method: 'POST',
        body: formData
    });

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let accumulated = '';

    while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        const text = decoder.decode(value);
        // SSEフォーマット "data: ...\n\n" をパース
        for (const line of text.split('\n')) {
            if (line.startsWith('data: ') && line !== 'data: [DONE]') {
                accumulated += line.slice(6);
                contentEl.innerHTML = marked.parse(accumulated);
            }
        }
    }
}
```

---

## 実装順序の推奨

| 順序 | Issue | 理由 |
|------|-------|------|
| 1 | Issue 1（コマンドインジェクション） | セキュリティ上の緊急対応 |
| 2 | Issue 5（.gitignore） | コミット前に対処すべき |
| 3 | Issue 2（会話コンテキスト） | 機能上の根本的な欠陥 |
| 4 | Issue 3（スキルスコープ） | DBスキーマ変更を伴うため早めに |
| 5 | Issue 4（プロバイダーハードコード） | 小さな修正 |
| 6 | Issue 6（ストリーミング） | 影響範囲が大きいため最後 |
