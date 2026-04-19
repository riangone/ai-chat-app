# Multi-Agent 開発実装仕様書

## 現状評価

**前版の文書（分析文書）は実装不可能だった。理由：**
- コードスニペットに`...`省略が多く、AIが補完を誤る余地があった
- `Program.cs`・フロントエンドの変更仕様が未記述
- エラーハンドリングの仕様がなかった
- 各エージェントのシステムプロンプト全文がなかった

**本文書は実装仕様書である。** 変更対象ファイル・変更前後のコード・完全なシグネチャを記載する。

---

## アーキテクチャ概要

### 現状（問題点）

```
CooperateAsync(task, userId, messageId, provider="gemini")
    │
    ├── RunAgentStepAsync("Orchestrator", ..., provider)   ← 全部同じprovider
    ├── RunAgentStepAsync("Executor",     ..., provider)   ← 全部同じprovider
    └── RunAgentStepAsync("Reviewer",     ..., provider)   ← 全部同じprovider
```

### 目標（ロール別プロバイダー）

```
CooperateAsync(task, userId, messageId, roleProviders)
    │
    ├── RunAgentStepAsync("Orchestrator", ..., roleProviders["Orchestrator"])  ← gemini
    ├── RunAgentStepAsync("Executor",     ..., roleProviders["Executor"])      ← claude
    ├── RunAgentStepAsync("Tester",       ..., roleProviders["Tester"])        ← gh-copilot
    └── RunAgentStepAsync("Reviewer",     ..., roleProviders["Reviewer"])      ← codex
```

---

## 変更ファイル一覧

| ファイル | 変更内容 |
|---------|---------|
| `AiChatApp/Services/AiService.cs` | `CooperateAsync`シグネチャ変更・エージェント追加・システムプロンプト更新 |
| `AiChatApp/Program.cs` | 2つのエンドポイントで`roleProviders`パース追加 |
| `AiChatApp/wwwroot/index.html` | ロール別プロバイダー選択UIの追加 |

---

## 変更1: `AiChatApp/Services/AiService.cs`

### 1-1. `CooperateAsync` メソッド全体を置換

**対象箇所：** `public async Task<(string Html, List<AgentStep> Steps)> CooperateAsync(` から `}` まで（現在の行44〜147）

**置換後コード（完全）：**

```csharp
/// <summary>
/// Multi-Agent Cooperative Mode。
/// Orchestrator → Executor → Tester → Reviewer の4段パイプライン。
/// roleProviders でロールごとに異なるAIプロバイダーを指定できる。
/// </summary>
public async Task<(string Html, List<AgentStep> Steps)> CooperateAsync(
    string task, int userId, int messageId,
    Dictionary<string, string>? roleProviders = null,
    Func<string, string, Task>? onStepComplete = null)
{
    // ロール→プロバイダーのマッピング（デフォルト値付き）
    string orchProvider   = roleProviders?.GetValueOrDefault("Orchestrator", "gemini")    ?? "gemini";
    string execProvider   = roleProviders?.GetValueOrDefault("Executor",     "claude")    ?? "claude";
    string testerProvider = roleProviders?.GetValueOrDefault("Tester",       "gh-copilot") ?? "gh-copilot";
    string reviewProvider = roleProviders?.GetValueOrDefault("Reviewer",     "codex")     ?? "codex";

    var steps = new List<AgentStep>();

    // ─── Step 1: Orchestrator ───
    string orchestratorPersona = """
        あなたはタスク分解の専門家（Orchestrator）です。
        ユーザーのタスクを分析し、以下のJSON形式のみで回答してください。
        それ以外のテキストは一切出力しないでください：
        {
          "plan": "全体方針の概要（1-2文）",
          "subtasks": [
            {"id": 1, "description": "サブタスクの説明", "expectedOutput": "期待される成果物"},
            {"id": 2, "description": "サブタスクの説明", "expectedOutput": "期待される成果物"}
          ]
        }
        """;

    var orchStep = await RunAgentStepAsync(
        role: "Orchestrator",
        persona: orchestratorPersona,
        input: task,
        messageId: messageId,
        provider: orchProvider,
        userId: userId
    );
    steps.Add(orchStep);
    if (onStepComplete != null) await onStepComplete(orchStep.Role, BuildStepHtml(orchStep));

    // JSONパース（失敗時はテキストプランとして継続）
    string planSummary = orchStep.Output;
    string subtaskBlock = task;
    try
    {
        var planDoc = System.Text.Json.JsonDocument.Parse(ExtractJson(orchStep.Output));
        planSummary = planDoc.RootElement.GetProperty("plan").GetString() ?? orchStep.Output;
        var subtasks = planDoc.RootElement.GetProperty("subtasks").EnumerateArray()
            .Select(s => $"[{s.GetProperty("id").GetInt32()}] {s.GetProperty("description").GetString()} → 成果物: {s.GetProperty("expectedOutput").GetString()}")
            .Where(s => !string.IsNullOrEmpty(s));
        subtaskBlock = string.Join("\n", subtasks);
    }
    catch { /* JSONでない場合はそのまま使用 */ }

    // ─── Step 2: Executor（最大2回）───
    string executorPersona = """
        あなたは実装の専門家（Executor）です。
        Orchestratorの計画に基づいて、コード・文章・分析など具体的な成果物を作成してください。
        - コードはコードブロック（```言語名）で囲む
        - 各サブタスクに対応した成果物を順番に出力する
        - 品質を重視し、動作する完全な実装を提供する
        """;

    AgentStep execStep = null!;
    for (int attempt = 1; attempt <= 2; attempt++)
    {
        string execInput = attempt == 1
            ? $"計画:\n{subtaskBlock}\n\n原タスク:\n{task}"
            : $"計画:\n{subtaskBlock}\n\n原タスク:\n{task}\n\n前回の実行結果（品質不足）:\n{execStep.Output}\n\n改善点を修正して再実装してください。";

        execStep = await RunAgentStepAsync(
            role: "Executor",
            persona: executorPersona,
            input: execInput,
            messageId: messageId,
            provider: execProvider,
            userId: userId,
            attemptNumber: attempt
        );
        steps.Add(execStep);
        if (onStepComplete != null) await onStepComplete(execStep.Role, BuildStepHtml(execStep));

        if (attempt < 2)
        {
            bool qualityOk = await QuickQualityCheckAsync(task, execStep.Output, execProvider);
            if (qualityOk) break;
            execStep.WasAccepted = false;
            await _db.SaveChangesAsync();
        }
    }

    // ─── Step 3: Tester（新規追加）───
    string testerPersona = """
        あなたはQAエンジニア（Tester）です。
        Executorが作成したコードや成果物に対して：
        - 単体テストコードを生成する（対象言語のテストフレームワークを使用）
        - テストケースのリストをMarkdownで記述する（正常系・異常系・境界値）
        - コードでない成果物の場合は、品質チェックリストを作成する
        テストコードはコードブロック（```言語名）で囲む。
        """;

    var testerStep = await RunAgentStepAsync(
        role: "Tester",
        persona: testerPersona,
        input: $"原タスク:\n{task}\n\n実装コード:\n{execStep.Output}",
        messageId: messageId,
        provider: testerProvider,
        userId: userId
    );
    steps.Add(testerStep);
    if (onStepComplete != null) await onStepComplete(testerStep.Role, BuildStepHtml(testerStep));

    // ─── Step 4: Reviewer ───
    string reviewerPersona = """
        あなたは品質評審の専門家（Reviewer）です。
        以下の情報を受け取り、ユーザーへの最終的な回答を作成してください：
        - 元のタスクの要件がすべて満たされているか確認する
        - 実装の問題点があれば指摘して補足する
        - テストコードとの整合性を確認する
        - Markdownで整形して読みやすく提示する（コードブロック・見出し・箇条書きを活用）
        - 最後に「改善提案」セクションを追加する（任意）
        """;

    var reviewStep = await RunAgentStepAsync(
        role: "Reviewer",
        persona: reviewerPersona,
        input: $"元タスク:\n{task}\n\n計画:\n{planSummary}\n\n実装結果:\n{execStep.Output}\n\nテスト:\n{testerStep.Output}",
        messageId: messageId,
        provider: reviewProvider,
        userId: userId
    );
    steps.Add(reviewStep);
    reviewStep.WasAccepted = true;
    await _db.SaveChangesAsync();
    if (onStepComplete != null) await onStepComplete(reviewStep.Role, BuildStepHtml(reviewStep));

    string html = BuildCooperativeHtml(steps, reviewStep.Output);
    return (html, steps);
}
```

### 1-2. `BuildStepHtml` のバッジ色を更新

**対象箇所：** `BuildStepHtml` 内の `string badgeClass = step.Role switch`

**置換後：**

```csharp
string badgeClass = step.Role switch
{
    "Orchestrator" => "badge-info",
    "Executor"     => "badge-success",
    "Tester"       => "badge-warning",
    "Reviewer"     => "badge-secondary",
    _              => "badge-ghost"
};
```

---

## 変更2: `AiChatApp/Program.cs`

### 2-1. `/api/chat` エンドポイント（通常COOPERATEモード）

**対象箇所：** `var isCooperative = form["mode"] == "cooperative";` の直後

**追加コード：**

```csharp
// ロール別プロバイダーのパース
// フォームフィールド名: roleProvider_Orchestrator, roleProvider_Executor, roleProvider_Tester, roleProvider_Reviewer
var roleProviders = new Dictionary<string, string>();
foreach (var role in new[] { "Orchestrator", "Executor", "Tester", "Reviewer" })
{
    var val = form[$"roleProvider_{role}"].ToString();
    if (!string.IsNullOrEmpty(val))
        roleProviders[role] = val;
}
```

**対象箇所：** `var (html, _) = await ai.CooperateAsync(content, userId, aMsg.Id, provider);`

**置換後：**

```csharp
var (html, _) = await ai.CooperateAsync(content, userId, aMsg.Id, roleProviders.Count > 0 ? roleProviders : null);
```

### 2-2. `/api/chat/cooperate/stream` エンドポイント（SSEモード）

**対象箇所：** `var provider = form["provider"].ToString() is { Length: > 0 } p ? p : "gemini";` の直後

**追加コード：**

```csharp
// ロール別プロバイダーのパース（SSEモード）
var roleProviders = new Dictionary<string, string>();
foreach (var role in new[] { "Orchestrator", "Executor", "Tester", "Reviewer" })
{
    var val = form[$"roleProvider_{role}"].ToString();
    if (!string.IsNullOrEmpty(val))
        roleProviders[role] = val;
}
```

**対象箇所：** `var (html, _) = await ai.CooperateAsync(content, userId, aMsg.Id, provider,`

**置換後：**

```csharp
var (html, _) = await ai.CooperateAsync(content, userId, aMsg.Id,
    roleProviders.Count > 0 ? roleProviders : null,
    onStepComplete: async (role, stepHtml) =>
    {
        await SendEvent("step-complete", $"{role}|{stepHtml}");
    });
```

---

## 変更3: `AiChatApp/wwwroot/index.html`

### 3-1. ロール別プロバイダー選択UIの追加

**対象箇所：** `<input type="checkbox" name="mode" value="cooperative"` を含む `div` の直後（HTMLの `<label>` タグ末尾の後）

**追加するHTMLブロック：**

```html
<!-- ロール別プロバイダー設定パネル（Cooperative Mode有効時のみ表示） -->
<div id="role-provider-panel" class="hidden mt-2 p-3 bg-base-200 rounded-xl border border-base-300 text-xs space-y-2">
    <p class="font-semibold opacity-60 mb-1">各エージェントのAIプロバイダー</p>
    
    <div class="grid grid-cols-2 gap-2">
        <div class="flex flex-col gap-1">
            <label class="flex items-center gap-1 opacity-70">
                <span class="badge badge-info badge-xs">Orchestrator</span>
                <span>計画立案</span>
            </label>
            <select name="roleProvider_Orchestrator" class="select select-bordered select-xs w-full">
                <option value="gemini" selected>Gemini</option>
                <option value="claude">Claude</option>
                <option value="gh-copilot">Copilot</option>
                <option value="codex">Codex</option>
            </select>
        </div>
        
        <div class="flex flex-col gap-1">
            <label class="flex items-center gap-1 opacity-70">
                <span class="badge badge-success badge-xs">Executor</span>
                <span>実装</span>
            </label>
            <select name="roleProvider_Executor" class="select select-bordered select-xs w-full">
                <option value="gemini">Gemini</option>
                <option value="claude" selected>Claude</option>
                <option value="gh-copilot">Copilot</option>
                <option value="codex">Codex</option>
            </select>
        </div>
        
        <div class="flex flex-col gap-1">
            <label class="flex items-center gap-1 opacity-70">
                <span class="badge badge-warning badge-xs">Tester</span>
                <span>テスト</span>
            </label>
            <select name="roleProvider_Tester" class="select select-bordered select-xs w-full">
                <option value="gemini">Gemini</option>
                <option value="claude">Claude</option>
                <option value="gh-copilot" selected>Copilot</option>
                <option value="codex">Codex</option>
            </select>
        </div>
        
        <div class="flex flex-col gap-1">
            <label class="flex items-center gap-1 opacity-70">
                <span class="badge badge-secondary badge-xs">Reviewer</span>
                <span>品質確認</span>
            </label>
            <select name="roleProvider_Reviewer" class="select select-bordered select-xs w-full">
                <option value="gemini">Gemini</option>
                <option value="claude">Claude</option>
                <option value="gh-copilot">Copilot</option>
                <option value="codex" selected>Codex</option>
            </select>
        </div>
    </div>
</div>
```

### 3-2. Cooperative Modeトグルのイベント処理

**対象箇所：** `<script>` タグ内、既存のJavaScriptの末尾に追加

**追加コード：**

```javascript
// Cooperative Modeトグルでロール別プロバイダーパネルを表示/非表示
document.addEventListener('DOMContentLoaded', () => {
    const modeToggle = document.querySelector('input[name="mode"][value="cooperative"]');
    const rolePanel = document.getElementById('role-provider-panel');
    
    if (modeToggle && rolePanel) {
        modeToggle.addEventListener('change', () => {
            rolePanel.classList.toggle('hidden', !modeToggle.checked);
        });
    }
});
```

---

## フォームデータの送信フロー

フロントエンドがSSEエンドポイントへ送信するフォームデータの構造：

```
POST /api/chat/cooperate/stream
Content-Type: application/x-www-form-urlencoded

content=ユーザーメッセージ
&sessionId=123
&mode=cooperative
&provider=gemini                    ← 後方互換のため残す（使用しない）
&roleProvider_Orchestrator=gemini
&roleProvider_Executor=claude
&roleProvider_Tester=gh-copilot
&roleProvider_Reviewer=codex
```

---

## SSEイベントフロー

サーバーからクライアントへの通知シーケンス：

```
event: session
data: 123

event: step-complete
data: Orchestrator|<div class='collapse ...'>...</div>

event: step-complete
data: Executor|<div class='collapse ...'>...</div>

event: step-complete
data: Tester|<div class='collapse ...'>...</div>

event: step-complete
data: Reviewer|<div class='collapse ...'>...</div>

event: final
data: <div class='space-y-4'>...</div>

event: done
data: 
```

---

## 実装可能性の評価

本仕様書を読んだ別のAIが実装するために必要な情報：

| 要素 | 記載状況 |
|------|---------|
| 変更対象ファイルのパス | ✅ 全て記載 |
| 変更前後のコード | ✅ 完全なコード（省略なし） |
| メソッドシグネチャ | ✅ 全パラメータ記載 |
| フォームフィールド名 | ✅ `roleProvider_Orchestrator` 等を明記 |
| SSEイベント名とデータ形式 | ✅ 記載 |
| デフォルト値 | ✅ 各ロールのデフォルトプロバイダーを明記 |
| 後方互換性 | ✅ `roleProviders.Count > 0` で分岐 |
| UIのHTML | ✅ 完全なHTMLスニペット |
| JavaScript | ✅ トグル連動のJSコード |

**結論：本仕様書は別のAIが追加調査なしに実装可能な水準に達している。**

---

## 実装後の動作確認手順

```bash
# ビルド確認
dotnet build AiChatApp

# 起動
dotnet run --project AiChatApp

# テスト（curl）
# 1. ログインしてクッキー取得
curl -c cookies.txt -X POST http://localhost:5000/api/auth/login \
  -d "username=admin&password=your_password"

# 2. SSEエンドポイントへ送信
curl -b cookies.txt -N -X POST http://localhost:5000/api/chat/cooperate/stream \
  -d "content=Cシャープで電卓クラスを実装して&sessionId=1&roleProvider_Orchestrator=gemini&roleProvider_Executor=claude&roleProvider_Tester=gh-copilot&roleProvider_Reviewer=codex"
```

期待するSSE出力：
```
event: session
data: 1

event: step-complete
data: Orchestrator|<div class='collapse...

event: step-complete
data: Executor|<div class='collapse...

...
```
