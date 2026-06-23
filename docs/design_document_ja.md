# ai-chat-pro (AiChatApp) — システム設計ドキュメント

> **目的**: 本ドキュメントは `ai-chat-pro` の設計・実装を詳細に記述し、他のAIが同等のシステムを再現できるよう作成されています。

---

## 目次

1. [プロジェクト概要](#1-プロジェクト概要)
2. [技術スタック](#2-技術スタック)
3. [ディレクトリ構成](#3-ディレクトリ構成)
4. [データモデル](#4-データモデル)
5. [バックエンドアーキテクチャ](#5-バックエンドアーキテクチャ)
6. [AIプロバイダー統合](#6-aiプロバイダー統合)
7. [メモリシステム](#7-メモリシステム)
8. [マルチエージェントシステム](#8-マルチエージェントシステム)
9. [フロントエンドアーキテクチャ](#9-フロントエンドアーキテクチャ)
10. [リアルタイム通信](#10-リアルタイム通信)
11. [バックグラウンドサービス](#11-バックグラウンドサービス)
12. [エンドポイント一覧](#12-エンドポイント一覧)
13. [認証・認可](#13-認証認可)
14. [設定管理](#14-設定管理)
15. [起動フロー](#15-起動フロー)
16. [実装上の重要なパターン](#16-実装上の重要なパターン)

---

## 1. プロジェクト概要

**ai-chat-pro** は個人向けAIアシスタントフレームワークです。複数のAI CLIプロバイダー（Gemini、Claude、Codex、Copilot等）をバックエンドとして使用し、マルチエージェント協調、長期記憶、スキルシステムを備えます。

### 主な特徴

| 特徴 | 説明 |
|------|------|
| マルチプロバイダー | gemini / claude / codex / copilot など複数CLIをプロバイダーとして抽象化 |
| マルチエージェント | Orchestrator → Executor → Reviewer の3段階パイプライン |
| 長期記憶 | マークダウンファイル + SQLite によるハイブリッド記憶層 |
| スキルシステム | キーワードトリガー型のプロンプト拡張 |
| SSEストリーミング | リアルタイム応答ストリーミング |
| PWA | Service Worker + Web Push 通知 |
| Headroom圧縮 | トークン使用量60〜95%削減のコンテキスト圧縮 |
| 自己進化 | Multi-AI Council による自律的スキル・記憶の生成と最適化 |

---

## 2. 技術スタック

```
バックエンド:
  - ASP.NET Core 10 (Minimal API)
  - Entity Framework Core + SQLite
  - SignalR (WebSocket)
  - BCrypt.Net (パスワードハッシュ)
  - Cookie認証

フロントエンド:
  - HTMX (サーバーサイドHTMLフラグメント方式)
  - Tailwind CSS + DaisyUI
  - marked.js (Markdownレンダリング)
  - Service Worker (PWA)

AI CLIプロバイダー:
  - antigravity / gemini (デフォルト)
  - claude (Claude CLI)
  - codex (OpenAI Codex CLI)
  - copilot (GitHub Copilot CLI)
  - opencode (OpenCode CLI)

補助ツール:
  - headroom-ai (Python: コンテキスト圧縮)
```

---

## 3. ディレクトリ構成

```
AiChatApp/
├── Program.cs                    # エントリポイント（サービス登録・ルートマッピングのみ）
├── appsettings.json              # 設定ファイル
├── Data/
│   └── AppDbContext.cs           # EF Core DbContext
├── Models/                       # エンティティクラス群
│   ├── Message.cs                # ChatSession, Message
│   ├── LongTermMemory.cs         # 長期記憶エンティティ
│   ├── Skill.cs                  # スキルエンティティ
│   ├── AgentProfile.cs           # プロジェクト固有エージェント定義
│   ├── AgentStep.cs              # エージェント実行ステップログ
│   ├── SessionMemory.cs          # セッション内短期記憶
│   ├── Project.cs                # プロジェクト管理
│   ├── Crew.cs                   # Crew（エージェントチーム）
│   ├── TodoItem.cs, Note.cs      # 生産性ツール
│   ├── FinancialAsset.cs         # 金融資産管理
│   ├── Attachment.cs             # 添付ファイル
│   ├── PushSubscription.cs       # Web Push
│   └── ProactiveSuggestion.cs    # プロアクティブ提案
├── Endpoints/                    # Minimal API エンドポイント（静的クラス）
│   ├── AuthEndpoints.cs
│   ├── ChatEndpoints.cs
│   ├── MemoryEndpoints.cs
│   ├── SkillEndpoints.cs
│   ├── CliEndpoints.cs
│   ├── HarnessEndpoints.cs
│   ├── TodoEndpoints.cs
│   ├── NotesEndpoints.cs
│   ├── FileManagerEndpoints.cs
│   ├── FinanceEndpoints.cs
│   ├── BriefingEndpoints.cs
│   ├── CrewEndpoints.cs
│   └── ...その他
├── Services/
│   ├── AiService.cs              # AI応答のオーケストレーター
│   ├── AiPromptService.cs        # システムプロンプト構築
│   ├── AiCollaborationService.cs # マルチエージェント協調
│   ├── AiResponseProcessor.cs    # AI応答の後処理
│   ├── MemorySearchService.cs    # 記憶検索（ファイル+グラフ）
│   ├── MemoryFileService.cs      # 記憶ファイルI/O
│   ├── MemoryGraphService.cs     # 記憶グラフ（知識グラフ）
│   ├── MemoryConsolidationService.cs # 自動記憶統合
│   ├── SessionMemoryService.cs   # セッション内短期記憶
│   ├── SkillManagerService.cs    # スキル管理（DB+ファイルシステム）
│   ├── SkillLearningService.cs   # スキル学習・統計
│   ├── MultiAiCouncilService.cs  # 多数決型AI評議会
│   ├── AssistantToolService.cs   # ツール呼び出し処理
│   ├── AttachmentService.cs      # 添付ファイル処理
│   ├── ProactiveBrainService.cs  # プロアクティブ提案
│   ├── ProjectService.cs         # プロジェクト管理
│   ├── ProjectApiController.cs   # プロジェクトAPIコントローラー
│   ├── ProjectPulseService.cs    # Gitコミット監視（BG）
│   ├── FileWatcherService.cs     # ファイルシステム監視（BG）
│   ├── ReminderService.cs        # リマインダー（BG）
│   ├── NewsBriefingScheduler.cs  # ニュースブリーフィング（BG）
│   ├── NewsCacheService.cs       # ニュースキャッシュ
│   ├── FinanceDataService.cs     # 金融データサービス
│   ├── WebPushService.cs         # Web Push通知
│   ├── Infrastructure/
│   │   ├── CliExecutorService.cs # CLI実行エンジン（予熱プール付き）
│   │   ├── HeadroomCompressionService.cs # コンテキスト圧縮
│   │   ├── BackgroundTaskTracker.cs      # バックグラウンドタスク追跡
│   │   ├── ProviderRegistry.cs           # プロバイダー設定管理
│   │   ├── LocalizationRegistry.cs       # 多言語フラグメント管理
│   │   ├── HtmlUtils.cs                  # HTMLユーティリティ
│   │   └── JsonUtils.cs                  # JSONユーティリティ
│   └── Harness/
│       ├── PipelineLoaderService.cs      # パイプライン定義ロード
│       ├── EvalService.cs                # エージェント評価
│       ├── SchemaValidationService.cs    # JSON出力スキーマ検証
│       ├── ToolExecutorService.cs        # パイプラインツール実行
│       └── PromptEvolutionService.cs     # プロンプト自動進化（BG）
├── Hubs/
│   └── ProactiveAgentHub.cs      # SignalR ハブ
├── Extensions/
│   ├── ServiceExtensions.cs      # DIコンテナ登録
│   └── ApplicationExtensions.cs  # DB初期化・スキーマ補正
├── AgentSkills/                  # ファイルシステムベースエージェント定義
│   ├── System/                   # システム標準エージェント（各サブディレクトリ = 1エージェント）
│   │   ├── dotnet-backend-architect/SKILL.md
│   │   ├── code-reviewer-pro/SKILL.md
│   │   ├── security-auditor-shannon/SKILL.md
│   │   └── ...
│   └── User/                     # ユーザー定義エージェント
├── pipelines/                    # ハーネスパイプライン定義
│   ├── default.json              # デフォルト3段階パイプライン
│   ├── prompts/                  # 各ステージのプロンプトテンプレート
│   │   ├── stage_orchestrator.md
│   │   ├── stage_executor.md
│   │   └── stage_reviewer.md
│   ├── schemas/                  # JSON出力スキーマ
│   └── policies/                 # 環境ポリシー（*.md）
├── memory/                       # ユーザーごとの長期記憶ファイル（.md）
├── Scripts/
│   └── headroom_compress.py      # コンテキスト圧縮スクリプト
└── wwwroot/                      # 静的ファイル（フロントエンド）
    ├── index.html                # メインSPA
    ├── login.html, register.html
    ├── manifest.json             # PWAマニフェスト
    ├── sw.js                     # Service Worker
    ├── translations.js           # 多言語対応
    ├── push-notifications.js     # Web Push登録
    ├── lib/                      # サードパーティライブラリ（HTMX, marked.js等）
    └── components/               # UIコンポーネント
```

---

## 4. データモデル

### 主要エンティティ

#### User
```csharp
public class User
{
    public int Id { get; set; }
    public string Username { get; set; }
    public string PasswordHash { get; set; }
    public bool IsAdmin { get; set; }
    public string DefaultProvider { get; set; }  // ユーザー固有デフォルトAIプロバイダー
    public string? LastBriefingContent { get; set; }
    public DateTime? BriefingUpdatedAt { get; set; }
}
```

#### ChatSession
```csharp
public class ChatSession
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? ProjectId { get; set; }          // プロジェクト紐付け
    public string Title { get; set; }
    public string PreferredProvider { get; set; } // セッション固有プロバイダー
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<Message> Messages { get; set; }
    public Project? Project { get; set; }
}
```

#### Message
```csharp
public class Message
{
    public int Id { get; set; }
    public int ChatSessionId { get; set; }
    public string Content { get; set; }
    public bool IsAi { get; set; }
    public string? AgentName { get; set; }        // 応答したエージェント名
    public DateTime Timestamp { get; set; }
    public List<AgentStep> AgentSteps { get; set; }
    public List<Attachment> Attachments { get; set; }
}
```

#### LongTermMemory（長期記憶）
```csharp
public class LongTermMemory
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Content { get; set; }
    public string Tags { get; set; }              // カンマ区切りタグ
    public string? Relations { get; set; }        // 知識グラフ用エンティティ（カンマ区切り）
    public int RelevanceScore { get; set; }       // 関連度スコア（0-100）
    public int AccessCount { get; set; }
    public DateTime LastAccessedAt { get; set; }
    public string? BoundAgentRole { get; set; }   // 特定エージェントロールへのバインド
    [NotMapped] public string? SourceFile { get; set; }
}
```

#### Skill（スキル）
```csharp
public class Skill
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string TriggerKeywords { get; set; }   // カンマ区切り。空=常に有効（グローバル）
    public bool IsEnabled { get; set; }
    public string? BoundAgentRole { get; set; }   // null=全エージェント
    // Hermesラーニングループ
    public int UseCount { get; set; }
    public int SuccessCount { get; set; }
    public bool IsAutoGenerated { get; set; }     // AIが自動生成したスキル
    public bool IsApproved { get; set; }
}
```

#### AgentProfile（プロジェクト固有エージェント）
```csharp
public class AgentProfile
{
    public int Id { get; set; }
    public string RoleName { get; set; }
    public string Goal { get; set; }
    public string Backstory { get; set; }
    public string SystemPrompt { get; set; }
    public int ProjectId { get; set; }
    public bool IsActive { get; set; }
    public string? Color { get; set; }
    public string? PreferredProvider { get; set; }
}
```

#### AgentStep（エージェント実行ステップ）
```csharp
public class AgentStep
{
    public int Id { get; set; }
    public int MessageId { get; set; }
    public string AgentRole { get; set; }         // Orchestrator / Executor / Reviewer
    public string Model { get; set; }
    public string Provider { get; set; }
    public string SystemPrompt { get; set; }
    public string Input { get; set; }
    public string Output { get; set; }
    public int DurationMs { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    // 評価スコア（EvalService）
    public double? AccuracyScore { get; set; }
    public double? SafetyScore { get; set; }
    public double? FormatScore { get; set; }
    public double? HelpfulnessScore { get; set; }
}
```

#### その他のエンティティ

| エンティティ | 説明 |
|------------|------|
| `SessionMemory` | セッション内短期記憶（チャットセッションIDで紐付け） |
| `Project` | プロジェクト管理（RootPath, Agents, カスタムエージェント） |
| `Crew` | エージェントチーム定義（CrewAI風） |
| `TodoItem` | ToDoリスト |
| `Note` | メモ・ノート |
| `FinancialAsset` | 金融資産追跡 |
| `Attachment` | 添付ファイルメタデータ |
| `PushSubscription` | Web Push通知登録 |
| `ProactiveSuggestion` | プロアクティブエージェントの提案 |
| `PromptVariant` | プロンプト進化バリアント（A/Bテスト） |
| `Evaluation` | ステップ評価結果 |
| `InputHistory` | 入力履歴（オートコンプリート用） |

### DBインデックス設計

```csharp
// 最も多用されるクエリ用複合インデックス
Message: (ChatSessionId, Timestamp)
ChatSession: (UserId)
Skill: (UserId, IsEnabled)
SessionMemory: (ChatSessionId)
AgentStep: (MessageId)
LongTermMemory: (UserId)
InputHistory: (UserId)
Attachment: (UserId), (MessageId)
PromptVariant: (TemplatePath, Status)
```

---

## 5. バックエンドアーキテクチャ

### Minimal API パターン

`Program.cs` はサービス登録とルートマッピングのみ。実ロジックは全て `Endpoints/*.cs` の静的クラスに分離。

```csharp
// Program.cs の構造
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProjectServices(builder.Configuration);

var app = builder.Build();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles(...);

await app.InitializeDatabaseAsync();

app.MapAuthEndpoints();
app.MapChatEndpoints();
// ... 全エンドポイント登録
app.MapHub<ProactiveAgentHub>("/hub/proactive-agent");
app.Run();
```

### サービスのライフタイム

| ライフタイム | サービス |
|------------|--------|
| **Singleton** | `ICliExecutor`, `HeadroomCompressionService`, `MemoryFileService`, `MemoryGraphService`, `SkillManagerService`, `PipelineLoaderService`, `ProactiveBrainService`, `WebPushService`, `NewsCacheService`, `IBackgroundTaskTracker` |
| **Scoped** | `AiService`, `AiPromptService`, `AiCollaborationService`, `AiResponseProcessor`, `MemorySearchService`, `MemoryConsolidationService`, `SessionMemoryService`, `SkillLearningService`, `MultiAiCouncilService`, `EvalService`, `ProjectService`, `AttachmentService`, `FinanceDataService`, `AssistantToolService` |
| **HostedService** | `PromptEvolutionService`, `NewsBriefingScheduler`, `FileWatcherService`*, `ProjectPulseService`*, `ReminderService`* |

> *`ProactiveSettings:Enabled=true` の場合のみ登録

### DB初期化戦略（マイグレーション不使用）

1. `EnsureCreated()` でスキーマを自動生成
2. `PRAGMA table_info` で列の存在チェック
3. 不足列は `ALTER TABLE ADD COLUMN` で追補
4. 新列追加は `ApplicationExtensions.InitializeDatabaseAsync` に記述

```csharp
// 例：列追加パターン
command.CommandText = "PRAGMA table_info(Users);";
// ... 列リスト取得
if (!usersColumns.Contains("DefaultProvider"))
{
    command.CommandText = "ALTER TABLE Users ADD COLUMN DefaultProvider TEXT NOT NULL DEFAULT '';";
    command.ExecuteNonQuery();
}
```

---

## 6. AIプロバイダー統合

### CLIExecutorService — 中核実行エンジン

全プロバイダーはシングルショット方式（リクエストごとに新プロセス）。

#### プロバイダー別起動コマンド

| プロバイダー名 | コマンド | プロンプト渡し方式 |
|-------------|--------|---------------|
| `antigravity` / `gemini` | `antigravity -p "..." --yolo` | CLI引数（stdin不可） |
| `claude` | `claude -p "" --dangerously-skip-permissions` | stdin |
| `codex` | `codex exec --dangerously-bypass-approvals-and-sandbox` | stdin |
| `copilot` | `gh copilot --yolo --silent` | CLI引数 |
| `opencode` | `opencode` | CLI引数 |

#### プロセス予熱プール（Pre-warm Pool）

stdin方式プロバイダーのレイテンシ削減のために予熱済みプロセスを管理。

```
キー形式: "{provider}_{workingDir}_{outputFormat}"

起動フロー:
1. リクエスト到着 → ClaimWarmProcess(key) で既起動プロセスを取得
2. なければ冷起動
3. 処理開始直後に SchedulePreWarm() で次のリクエスト用プロセスを非同期起動
4. 予熱プロセスが死んでいた場合は自動的に冷起動にフォールバック

実測効果: 冷起動 ~12-15s → 予熱済み ~11s（1〜3s削減）
非対応: opencode, copilot, antigravity, gemini（引数渡しのため）
```

#### ICliExecutor インターフェース

```csharp
public interface ICliExecutor
{
    // 単一応答
    Task<CliResult> ExecuteAsync(
        string prompt, string provider,
        string? systemPrompt = null, string? userPrompt = null,
        string? workingDirectory = null, bool agentMode = false, string? outputFormat = null);
    
    // ストリーミング応答
    IAsyncEnumerable<StreamChunk> ExecuteStreamAsync(
        string prompt, string provider, ...);
}

public record CliResult(string Output, string Model, int PromptTokens, int CompletionTokens, int TotalTokens);
public record StreamChunk(string? Text, string? Model, int PromptTokens, int CompletionTokens, int TotalTokens);
```

### AiService — AI処理オーケストレーター

```csharp
// 通常応答フロー
public async Task<string> GetResponseAsync(string prompt, int userId, int? chatSessionId, ...)
{
    // 1. 管理者権限チェック（コード変更要求の場合）
    // 2. プロバイダー決定（agentProfile.PreferredProvider > 引数 > デフォルト）
    // 3. セッション・プロジェクト情報取得
    // 4. AiPromptService.BuildSystemPromptAsync() でシステムプロンプト構築
    // 5. HeadroomCompressionService.CompressAsync() でコンテキスト圧縮
    // 6. CliExecutorService.ExecuteAsync() でAI呼び出し
    // 7. AgentStep をDBに記録
    // 8. AssistantToolService.ExecuteToolCallsAsync() でツール実行
    // 9. MemoryConsolidationService.TryConsolidateAsync() で記憶統合（fire-and-forget）
    // 10. タイトル自動生成（最初のメッセージ時）
}
```

### Headroom コンテキスト圧縮

```
仕組み:
- Scripts/headroom_compress.py を Python サブプロセスとして実行
- システムプロンプト + 会話履歴を OpenAI 形式 JSON で渡す
- 圧縮結果: CompressedSystemPrompt + CompressedHistory を返す

設定:
  AiSettings:HeadroomCompression:Enabled: true
  AiSettings:HeadroomCompression:MinChars: 800  # これ以下なら圧縮しない

効果: トークン使用量 60〜95% 削減
フォールバック: headroom-ai 未インストール時は非圧縮で続行
```

---

## 7. メモリシステム

### 3層メモリアーキテクチャ

```
Layer 1: セッション記憶 (SessionMemory)
  - スコープ: チャットセッション内
  - ストレージ: SQLite (SessionMemories テーブル)
  - 用途: 会話コンテキストの短期維持

Layer 2: 長期記憶 (LongTermMemory)
  - スコープ: ユーザーレベル（永続）
  - ストレージ: memory/*.md ファイル（プライマリ）+ SQLite（インデックス）
  - 用途: ユーザーの知識・好み・プロジェクト情報の蓄積

Layer 3: 知識グラフ (MemoryGraph)
  - スコープ: ユーザーレベル
  - ストレージ: メモリ内キャッシュ（TTL: 10分）
  - 用途: 記憶間の関係性探索・関連記憶の拡張取得
```

### 記憶ファイル形式（memory/*.md）

```markdown
---
tags: project,architecture,ai-chat-pro
relations: AiService,CliExecutorService,HTMX
boundAgentRole: Executor        # オプション（特定エージェントにバインド）
relevanceScore: 100
createdAt: 2025-01-01T00:00:00Z
updatedAt: 2025-01-01T00:00:00Z
---

記憶の内容をMarkdownで記述
```

### MemorySearchService — 記憶検索

```
検索フロー:
1. MemoryFileService.SearchAsync() でキーワードベース検索
   優先度:
     (1) タグ完全一致
     (2) タグ部分一致
     (3) コンテンツ部分一致
2. 結果が目標数(5件)未満の場合のみグラフ拡張
3. MemoryGraphService.GetRelatedMemoriesAsync() で隣接ノード取得
4. agentRole によるフィルタリング
```

### MemoryConsolidationService — 自動記憶統合

```
トリガー: 各チャット応答後に fire-and-forget で呼び出し

バッチ方式:
- ユーザーごとに最大5メッセージをバッファリング
- バッチが5件に達するか、前回から10分経過で処理開始

判定基準（統合をスキップする場合）:
- メッセージ+応答の合計文字数 < 100
- 短い相槌のみ（ok/thanks/yes/no/了解/好的 等）

処理:
- 常に gemini プロバイダーでAIに事実抽出を依頼（ハードコード）
- 同一タグの既存記憶は上書き更新
- MemoryFileService.SaveMemoryAsync() でファイル保存
```

---

## 8. マルチエージェントシステム

### ハーネスパイプライン

3段階パイプライン（`pipelines/default.json`）:

```json
{
  "stages": [
    { "name": "Orchestrator", "role": "orchestrator", "isFinalStage": false },
    { "name": "Executor",     "role": "executor",     "retryOnQualityFail": true, "maxAttempts": 2 },
    { "name": "Reviewer",     "role": "reviewer",     "isFinalStage": true }
  ]
}
```

#### Orchestrator（計画フェーズ）
- タスクを分析してサブタスクに分解
- `OrchestratorPlan` として JSON 出力
- `pipelines/schemas/orchestrator_output.json` でスキーマ検証

#### Executor（実行フェーズ）
- Orchestratorの計画に従い実際の作業を実行
- 品質評価失敗時に最大2回リトライ
- `TaskBlackboard` に成果物を書き込み

#### Reviewer（レビューフェーズ）
- Executorの出力を4基準（Accuracy/Safety/Format/Helpfulness）で評価
- `ReviewerFeedback` として JSON 出力

### AiCollaborationService — 協調エージェント実行

```csharp
public async Task<(string Html, List<AgentStep> Steps)> CooperateAsync(
    string task, int userId, int messageId, int? chatSessionId,
    string? provider = null,
    List<string>? selectedAgentNames = null,     // 明示的エージェント指定
    Func<string, string, Task>? onStepComplete = null, // SSEコールバック
    CrewProcessType processType = CrewProcessType.Hierarchical)
```

エージェント選択優先度:
1. `selectedAgentNames` による明示的指定
2. セッションのプロジェクトに紐付いた `AgentProfile`
3. デフォルト3段階パイプライン（Orchestrator → Executor → Reviewer）

### ファイルシステムベースエージェント（SKILL.md）

```
読み込み元ディレクトリ:
- AgentSkills/System/*/   （システム標準エージェント）
- AgentSkills/User/*/     （ユーザー定義エージェント）
- test-skill/             （単一エージェント用）
- .gemini/skills/*/       （Gemini Skills）

各ディレクトリに SKILL.md が必要:
---
name: DotNetBackendArchitect
description: .NET バックエンド設計専門エージェント
---
システムプロンプト本文...

優先度: ファイルシステム側 > DB側（名前が重複する場合）
```

### Multi-AI Council（自律進化システム）

3つのAIが協議してシステムを自律的に改善:

```
Phase 1 - Propose (Gemini):
  会話バッチを分析 → 新スキル・エージェント・記憶パターンを提案

Phase 2 - Refine (DeepSeek via opencode):
  Geminiの提案を精査・改善

Phase 3 - Validate (Claude):
  安全性・有効性を検証 → 承認/却下

承認された提案は:
  - 新スキルとして DB に保存（IsAutoGenerated=true, IsApproved=false で要承認）
  - または既存スキルの自動修復（低パフォーマンススキルの改善）
```

### CrewProcessType（実行モード）

```csharp
public enum CrewProcessType
{
    Sequential,    // 順次実行（各エージェントが前段の出力を引き継ぐ）
    Hierarchical   // 階層的実行（Orchestratorがサブタスクに分解して各エージェントに割り当て）
}
```

### TaskBlackboard（共有メモリボード）

```csharp
public sealed class TaskBlackboard
{
    // サブタスクIDをキーとして成果物を格納
    void Write(string subtaskId, string agentRole, string content, ...);
    BlackboardArtifact? Read(string subtaskId);
    string BuildDepContext(List<string> deps, ...); // 依存タスクのコンテキストを結合
}
```

---

## 9. フロントエンドアーキテクチャ

### HTMX + サーバーサイドHTMLフラグメント

- サーバーはHTMLフラグメントを直接返す（JSONではなく）
- フロント側で`marked.js`がMarkdownをHTMLに変換
- Tailwind CSS + DaisyUI でUIコンポーネント

### SSEストリーミングエンドポイント

```
/api/chat                         # 通常チャット（HTML断片返却）
/api/chat/stream                  # SSEストリーミング
  データ形式: data: <テキストチャンク>
  終端: data: [DONE]

/api/chat/cooperate/stream        # マルチエージェント協調SSE
  イベント種別:
    session: セッションID通知
    step-complete: 各ステージ完了
    final: 最終応答HTML
    done: 全処理完了
```

### PWA 設定

```json
// manifest.json
{
  "name": "AI Chat App",
  "display": "standalone",
  "start_url": "/",
  "icons": [...]
}
```

```javascript
// sw.js - Service Worker
// キャッシュ戦略:
// - HTML/sw.js: no-cache
// - .js/.css: public, max-age=3600
```

### キャッシュ制御

```csharp
// Program.cs
app.UseStaticFiles(new StaticFileOptions {
    OnPrepareResponse = ctx => {
        if (path.EndsWith(".html") || path == "sw.js")
            ctx.Context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        else if (path.EndsWith(".js") || path.EndsWith(".css"))
            ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=3600";
    }
});
```

---

## 10. リアルタイム通信

### ProactiveAgentHub（SignalR）

```
エンドポイント: /hub/proactive-agent

グループ:
  project-{id} : プロジェクトグループ
  user-{id}    : ユーザーグループ（接続時に自動参加）

クライアントイベント:
  ProactiveMessage : プロアクティブ提案・通知

接続時:
  1. ユーザーグループに参加
  2. ProactiveBrainService.ProcessWelcomeInsightAsync() でウェルカム分析実行
```

### Web Push通知

```
使用技術: VAPID (Web Push Protocol)
設定: appsettings.json の WebPush セクション
登録エンドポイント: /api/notifications/subscribe
```

---

## 11. バックグラウンドサービス

### NewsBriefingScheduler
```
スケジュール: 毎朝7時（UTC）
処理: 米・中・日のニュースを自動収集・要約
結果: Users.LastBriefingContent に保存
```

### PromptEvolutionService（A/Bテスト型プロンプト進化）
```
設定: AiSettings:Evolution（デフォルト無効）
処理:
  - 過去X時間のAgentStepスコアを集計
  - チャレンジャープロンプトを生成
  - トラフィックの一部（ChallengerTraffic%）をチャレンジャーに振り向け
  - MinImprovement を超えた場合のみチャレンジャーを昇格

設定パラメータ:
  IntervalMinutes: 30    # 評価間隔
  WindowHours: 72        # 評価ウィンドウ
  MinSamples: 10         # 最小サンプル数
  TriggerThreshold: 0.75 # 昇格トリガースコア
  ChallengerTraffic: 0.3 # チャレンジャートラフィック比率
  MinImprovement: 0.02   # 最小改善率
  RegressionTolerance: 0.05 # 回帰許容範囲
```

### FileWatcherService（`ProactiveSettings:Enabled=true` 時のみ）
```
監視パス: FileWatcher:Path 設定値
スロットリング: 2秒（同一ファイルの連続変更を抑制）
通知: SignalR でファイル変更をクライアントに送信
```

### ProjectPulseService（`ProactiveSettings:Enabled=true` 時のみ）
```
間隔: 10分ごと
処理:
  1. プロジェクトごとに git の差分コミットをスキャン
  2. 前回スキャン以降の変更を検出
  3. AIによる差分分析を起動
  4. 結果を ProactiveAgentHub でブロードキャスト（哨兵フェーズ）
```

### ReminderService（`ProactiveSettings:Enabled=true` 時のみ）
```
処理: 期限が近いTodoItemをチェックしてWeb Push通知を送信
```

---

## 12. エンドポイント一覧

| モジュール | エンドポイント例 | 説明 |
|----------|--------------|------|
| Auth | `POST /api/auth/login` | ログイン（Cookie発行） |
| Auth | `POST /api/auth/register` | ユーザー登録 |
| Auth | `POST /api/auth/logout` | ログアウト |
| Chat | `POST /api/chat` | 通常チャット（HTML断片） |
| Chat | `GET /api/chat/stream` | SSEストリーミング |
| Chat | `POST /api/chat/cooperate/stream` | マルチエージェント協調SSE |
| Chat | `GET /api/sessions` | セッション一覧 |
| Memory | `GET /api/memories` | 記憶一覧 |
| Memory | `POST /api/memories` | 記憶作成 |
| Memory | `DELETE /api/memories/{id}` | 記憶削除 |
| Skill | `GET /api/skills` | スキル一覧 |
| Skill | `POST /api/skills` | スキル作成 |
| Skill | `GET /api/agents` | 利用可能エージェント一覧 |
| Harness | `GET /api/harness/pipelines` | パイプライン一覧 |
| Harness | `POST /api/harness/run` | パイプライン実行 |
| Project | `GET /api/projects` | プロジェクト一覧 |
| Project | `POST /api/projects` | プロジェクト作成 |
| CLI | `GET /api/cli/sessions` | CLI実行履歴 |
| Todo | `GET /api/todos` | Todo一覧 |
| Notes | `GET /api/notes` | ノート一覧 |
| Finance | `GET /api/finance/assets` | 金融資産一覧 |
| Crew | `GET /api/crews` | Crew一覧 |
| Briefing | `GET /api/briefing` | ニュースブリーフィング取得 |
| Notification | `POST /api/notifications/subscribe` | Push通知登録 |
| Attachment | `POST /api/attachments` | ファイルアップロード |
| Stats | `GET /api/stats` | 統計情報 |
| FileManager | `GET /api/files` | ファイル一覧（プロジェクト） |
| Inbox | `GET /api/inbox` | インボックス |

---

## 13. 認証・認可

```csharp
// Cookie認証
services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
    });

// 管理者ポリシー
services.AddAuthorization(options =>
    options.AddPolicy("AdminOnly", policy => policy.RequireClaim("IsAdmin", "true")));

// フォームPOSTはAntiforgery無効化（HTMXとの互換性のため）
app.MapPost("/api/xxx", handler).DisableAntiforgery();
```

### 管理者専用機能

コード変更要求はAiServiceレベルで管理者のみ許可:

```csharp
if (!isAdmin && IsCodeModificationRequest(prompt))
    return "拒绝执行：检测到代码修改指示...";
```

---

## 14. 設定管理

### appsettings.json 主要セクション

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=chat.db"
  },
  "MemoryDir": "memory",
  "AiSettings": {
    "DefaultProvider": "antigravity",
    "FallbackProvider": "antigravity",
    "TimeoutSeconds": 1800,
    "EvalEnabled": false,
    "Evolution": {
      "Enabled": false,
      "IntervalMinutes": 30,
      "WindowHours": 72,
      "MinSamples": 10,
      "TriggerThreshold": 0.75,
      "ChallengerTraffic": 0.3,
      "MinImprovement": 0.02,
      "RegressionTolerance": 0.05
    },
    "HeadroomCompression": {
      "Enabled": true,
      "MinChars": 800
    },
    "SystemPrompts": {
      "Default": "あなたは...",
      "TitleGenerator": "あなたはチャットタイトルの命名者です...",
      "MemoryInstruction": "\n\n[MEMORY INSTRUCTION]:..."
    }
  },
  "CliPaths": {
    "Antigravity": "/path/to/chats",
    "Claude": "/path/to/claude-history",
    "Codex": "/path/to/history.jsonl",
    "Copilot": "/path/to/logs"
  },
  "FileWatcher": {
    "Path": "/path/to/watch",
    "Enabled": false
  },
  "WebPush": {
    "VapidPublicKey": "...",
    "VapidPrivateKey": "...",
    "VapidSubject": "mailto:..."
  },
  "ProactiveSettings": {
    "Enabled": false
  }
}
```

### 環境変数

| 変数名 | 説明 | デフォルト |
|------|------|--------|
| `ADMIN_INITIAL_PASSWORD` | 初期管理者パスワード | `admin123` |

---

## 15. 起動フロー

```
1. WebApplication.CreateBuilder()
2. AddProjectServices() → 全DI登録
3. ミドルウェアパイプライン設定
   (Antiforgery → Authentication → Authorization → StaticFiles)
4. InitializeDatabaseAsync():
   a. EnsureCreated() でスキーマ生成
   b. admin ユーザーが存在しなければ作成
   c. PRAGMA table_info で列チェック → ALTER TABLE で補完
   d. PipelineLoaderService 初期化（pipelines/*.json 読み込み）
5. MemoryFileService 初期化（memory/ ディレクトリスキャン）
6. 全エンドポイント・SignalRハブのルート登録
7. HostedService 起動（バックグラウンドサービス群）
8. app.Run() → http://0.0.0.0:5000 でリッスン
```

---

## 16. 実装上の重要なパターン

### システムプロンプト構築（AiPromptService.BuildSystemPromptAsync）

```
構築順序:
1. ベースシステムプロンプト（appsettings または AgentProfile.SystemPrompt）
2. [ユーザーの既知情報・長期記憶] セクション
   → MemorySearchService.SearchAsync() でキーワード検索（最大5件）
3. [当前会话上下文] セクション
   → SessionMemoryService で短期記憶取得
4. [追加スキル指示] セクション
   → MemorySearchService.SearchSkillsAsync() でスキル検索
5. [プロジェクト文脈] セクション（プロジェクト紐付き時）
   → Project.Agents（AgentProfile群）の情報
6. [ENVIRONMENTAL POLICIES & CONSTRAINTS] セクション
   → pipelines/policies/*.md から読み込み（キャッシュ済み）
7. [MEMORY INSTRUCTION] セクション
```

### HTMLレンダリング（HTMX向け）

- `AiService.BuildStepHtml()` と `BuildCooperativeHtml()` でHTMLフラグメントを生成
- 全コンテンツは `HttpUtility.HtmlEncode()` でエスケープ済み
- `marked.js` がフロント側でMarkdown→HTMLに変換

### スキル注入ロジック

```
TriggerKeywords が空 → 常に有効（グローバルスキル）
TriggerKeywords あり → プロンプトにキーワードが含まれる場合のみ有効
BoundAgentRole あり → 該当エージェントロールの実行時のみ有効
IsAutoGenerated=true かつ IsApproved=false → スキップ（未承認AIスキル）
```

### 画像参照の自動解決

```csharp
// プロンプト内の画像ファイル名を自動的に @ファイル名 形式に変換
// 例: "test.png" → "@test.png"（ファイルが workingDirectory に存在する場合）
public string ResolveImageReferences(string prompt, string? workingDirectory)
```

### 会話履歴フォーマット

```
History:
User: ...
Assistant: ...
User: ...
Assistant: ...

現在のメッセージ:
User: {prompt}
```

### エラーハンドリング・フォールバック

```
プロバイダーエラー時: FallbackProvider に自動切替
HeadroomCompression失敗時: 非圧縮で続行
予熱プロセス死亡時: 冷起動にフォールバック
Memory統合失敗時: ログのみ（チャット応答には影響なし）
```

---

## 付録: 同等プロジェクト構築時のチェックリスト

### 必須コンポーネント

- [ ] ASP.NET Core Minimal API バックエンド
- [ ] EF Core + SQLite（マイグレーション不使用・`ALTER TABLE` 補完方式）
- [ ] Cookie認証 + BCryptパスワードハッシュ
- [ ] CLI実行エンジン（`ICliExecutor`抽象）
- [ ] 3段階マルチエージェントパイプライン
- [ ] 長期記憶システム（ファイル+DB ハイブリッド）
- [ ] スキルシステム（キーワードトリガー方式）
- [ ] SSEストリーミングエンドポイント
- [ ] SignalR ハブ（プロアクティブ通知）
- [ ] HTMX フロントエンド
- [ ] PWA（Service Worker + manifest.json）

### 推奨コンポーネント

- [ ] Headroom コンテキスト圧縮（Python スクリプト）
- [ ] プロセス予熱プール（レイテンシ削減）
- [ ] Multi-AI Council（自律進化）
- [ ] PromptEvolutionService（A/Bテスト型）
- [ ] Web Push通知
- [ ] ニュースブリーフィングスケジューラー
- [ ] 知識グラフ（記憶間関係性）

### AI CLIの準備

```bash
# 必要なCLI（PATHが通っていること）
antigravity  # または gemini CLI
claude       # Claude CLI
codex        # OpenAI Codex CLI
gh           # GitHub CLI（copilot extension含む）
opencode     # OpenCode CLI（DeepSeek向け）

# Python（Headroom圧縮用）
pip install headroom-ai
```

---

*ドキュメント生成日: 2025年*  
*対象バージョン: AiChatApp feature/hyperion-improvements ブランチ*
