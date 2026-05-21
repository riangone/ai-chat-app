# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# ビルド
dotnet build AiChatApp

# 実行 (http://localhost:5000)
dotnet run --project AiChatApp

# スキーマ変更時はDBを削除して再起動（マイグレーション不要）
rm AiChatApp/chat.db && dotnet run --project AiChatApp
```

データベース (`chat.db`) はアプリ起動時に `EnsureCreated()` で自動生成され、さらに `ApplicationExtensions.InitializeDatabaseAsync` が `ALTER TABLE` で列を追補する（マイグレーションなし）。起動時にデフォルト管理者 `admin` / `admin123` が存在しなければ作成される。

## Architecture

**バックエンド**: ASP.NET Core 10 Minimal API。`Program.cs` はサービス登録と `app.MapXxxEndpoints()` 呼び出しのみで、実ロジックはすべて `Endpoints/` 配下の静的クラスに分離されている。

**エンドポイント一覧** (各 `Endpoints/*.cs` から登録):
- `AuthEndpoints` — 認証（ログイン/登録/ログアウト）
- `ChatEndpoints` — チャット・協調マルチエージェント・SSEストリーミング
- `MemoryEndpoints` — 長期記憶 CRUD
- `SkillEndpoints` — スキル CRUD
- `CliEndpoints` — 外部CLIセッション（Gemini/Claude/Codex/Copilot）の閲覧
- `HarnessEndpoints` — パイプライン定義の管理・実行・評価
- `ProjectApiController` (`Services/ProjectApiController.cs`) — プロジェクト管理（`MapProjectEndpoints()`）
- `InputHistoryEndpoints`, `TodoEndpoints`, `NotesEndpoints`, `FileManagerEndpoints`, `StatsEndpoints` — 各機能のCRUD

**データ層**: EF Core + SQLite (`Data/AppDbContext.cs`)。モデル構成：
- `User`, `ChatSession`, `Message`, `AgentStep` は `Models/Message.cs` に同居
- `LongTermMemory`, `Skill`, `AgentProfile`, `Project`, `Note`, `TodoItem`, `InputHistory`, `SessionMemory`, `ProactiveAgentProfile`, `ProactiveSuggestion` は個別ファイル
- `OnModelCreating` は空。スキルのシード（`CodeOptimizer`, `CreativeWriter`, `Translator`）はユーザー登録時に生成

**AI統合** (`Services/AiService.cs`):
- `GetResponseAsync` / `GetResponseStreamAsync` — 通常チャット（SSEストリーミング対応）
- `CooperateAsync` — マルチエージェントパイプライン。優先順位：(1) `selectedAgentNames` → (2) セッションのプロジェクトカスタムエージェント → (3) デフォルト3段階（Orchestrator → Executor 最大2回リトライ → Reviewer）。各ステップ完了時に `onStepComplete` コールバックでSSEに中間結果を送信
- `ExecuteCliAsync` / `SetupProcessInfo` — プロバイダー別サブプロセス実行: `gemini --yolo` / `copilot --yolo --silent` / `claude --dangerously-skip-permissions` / `codex exec --dangerously-bypass-approvals-and-sandbox`
- `GetAvailableAgentsAsync` — エージェント定義を(1)ファイルシステム、(2)DBから収集
- プロバイダー設定は `appsettings.json` の `AiSettings` セクション（`DefaultProvider`, `FallbackProvider`, `TimeoutSeconds`）で制御

**ハーネス** (`Services/Harness/`, `pipelines/`):
- `PipelineLoaderService` — `pipelines/*.json` を読み込み・ホットリロード。各パイプラインは `stages[]`（Orchestrator → Executor → Reviewer）を持つ
- `EvalService` — エージェントステップを Accuracy/Safety/Format/Helpfulness の4基準でAI評価し `AgentStep` に保存
- `SchemaValidationService` / `ToolExecutorService` — パイプライン出力のスキーマ検証とツール実行
- パイプライン定義は `pipelines/*.json`、プロンプトテンプレートは `pipelines/prompts/*.md`、スキーマは `pipelines/schemas/*.json`、ポリシーは `pipelines/policies/*.md`

**フロントエンド** (`wwwroot/`): HTMX + Tailwind CSS + DaisyUI。サーバーはHTMLフラグメントを返す（JSON APIは一部のみ）。Markdownレンダリングは `marked.js` がフロント側で実行。PWAマニフェスト (`manifest.json`) とService Worker (`sw.js`) あり。

**リアルタイム** (`Hubs/ProactiveAgentHub.cs`): SignalR WebSocketハブ。クライアントはプロジェクトグループ (`project-{id}`) またはユーザーグループ (`user-{id}`) に参加し、`ProactiveMessage` イベントを受信する。

**バックグラウンドサービス**:
- `FileWatcherService` — `FileWatcher:Path` 設定のディレクトリを監視し、ファイル変更時にSignalRで通知（2秒スロットリング）
- `ProjectPulseService` — 10分ごとにGitの増分コミットをスキャンし、プロジェクトへのAI主動分析を起動（哨兵フェーズ）

## Key Patterns

- **メモリ注入**: `BuildSystemPromptAsync` → `MemorySearchService.SearchAsync` でユーザーメッセージのキーワードと `LongTermMemory.Tags` を照合してシステムプロンプトに埋め込む。3段優先度（タグ完全一致→タグ部分一致→コンテンツ部分一致）
- **スキル注入**: `MemorySearchService.SearchSkillsAsync` でプロンプトキーワードと `Skill.TriggerKeywords` を照合。`TriggerKeywords` が空のスキルは常に有効（グローバルスキル）。`agentRole` 指定時は `Skill.BoundAgentRole` でさらに絞り込む
- **自動記憶統合**: 各チャット応答後に `MemoryConsolidationService.TryConsolidateAsync` を fire-and-forget で呼び出し、AIが事実を抽出して `LongTermMemory` に自動保存。同一タグの既存記憶は上書き更新。常に "gemini" プロバイダーを使用（ハードコード）
- **プロジェクト文脈**: `ChatSession.ProjectId` でプロジェクトを紐付け。`Project.RootPath` を作業ディレクトリとして渡し、`Project.Agents`（`AgentProfile`）をシステムプロンプトに注入
- **SSEストリーミング**: `/api/chat` (HTMLフラグメント)、`/api/chat/stream` (通常SSE、`data: [DONE]` 終端)、`/api/chat/cooperate/stream` (協調SSE、`session`/`step-complete`/`final`/`done` イベント)
- **認証**: Cookie認証 + BCrypt。フォームPOSTエンドポイントには `.DisableAntiforgery()` を付与
- **HTMLレンダリング**: `BuildStepHtml` / `BuildCooperativeHtml` (`AiService.cs`) と各エンドポイント内のローカル関数がHTMLフラグメントを生成。コンテンツは `HtmlEncode` 済み
- **スキーマ補正**: `ApplicationExtensions.InitializeDatabaseAsync` が起動時に `PRAGMA table_info` で列の有無を確認し、不足列を `ALTER TABLE` で追補。新列追加はここに記述する

## ファイルシステムベースのエージェント（SKILL.md）

`GetAvailableAgentsAsync` は以下のディレクトリからエージェント定義を読み込む：
- `AgentSkills/System/*/` — システム付属エージェント群（各サブディレクトリが1エージェント）
- `test-skill/` — 単一エージェントディレクトリ
- `.gemini/skills/*/` — 複数エージェントの親ディレクトリ

各ディレクトリに `SKILL.md` が存在すればエージェントとして登録される。YAMLフロントマター（`name:`, `description:`）をオプションで持てる：

```markdown
---
name: MyAgent
description: エージェントの説明
---
システムプロンプト本文
```

DBの `AgentProfile` と名前が重複する場合はファイルシステム側が優先される。

## AI Provider

実行環境に `gemini`、`copilot`（`gh extension`）、`claude`、`codex` のCLIが PATH 上に存在している必要がある。デフォルトプロバイダーは `appsettings.json` の `AiSettings:DefaultProvider` で設定（デフォルト `gemini`）。`AgentProfile.PreferredProvider` でエージェントごとに異なるプロバイダーを指定可能。

## CLI プロセス管理（CliExecutorService）

**アーキテクチャ**: 全プロバイダーはシングルショット方式（リクエストごとに新プロセス）。`gemini -p "" --yolo` / `claude -p "" --dangerously-skip-permissions` のように空の `-p ""` で起動し、実際のプロンプトは stdin に書き込んで EOF を送ることで処理が始まる。

**プロセス永続化が不可能な理由**: これらのCLIは stdin が閉じられる（EOF）まで処理を開始しない設計のため、1プロセスで複数リクエストを処理できない。PTY経由のインタラクティブモードは可能だが、応答境界の検出が困難でリスクが高い。

**プロセス予熱（Pre-warm Pool）**: `_warmPool` により起動レイテンシを削減している。
- 現在のリクエスト処理開始直後に、次のリクエスト用プロセスを非同期で `SchedulePreWarm()` する
- 次のリクエストが来たとき `ClaimWarmProcess()` で既起動プロセスを取得し、stdin にプロンプトを書くだけで済む
- **実測効果**: 冷起動 ~12-15s → 予熱済み ~11s（Node.js + CLI初期化の ~1~3s を節約）
- キー形式: `{provider}_{workingDir}_{outputFormat}`（フォーマットが異なるプールは別管理）
- **非対応プロバイダー**: `opencode` / `copilot` はプロンプトをCLI引数で渡すため予熱不可。これらは常に冷起動
- 予熱プロセスが死んでいた場合は自動的に冷起動にフォールバックするため安全
