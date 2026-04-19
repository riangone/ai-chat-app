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

データベース (`chat.db`) はアプリ起動時に `EnsureCreated()` で自動生成される。スキーマ変更時はDBを削除して再起動すれば再生成される（EF Core マイグレーションは使用しない）。

## Architecture

**バックエンド**: ASP.NET Core 10 Minimal API。エンドポイントは `Program.cs`（チャット・認証・メモリ・スキル）と `Services/ProjectApiController.cs`（プロジェクト管理、`MapProjectEndpoints()` 拡張メソッド）に分割。

**データ層**: EF Core + SQLite（`Data/AppDbContext.cs`）。モデル構成：
- `User`, `ChatSession`, `Message`, `AgentStep` は `Models/Message.cs` に同居
- `LongTermMemory`, `Skill`, `AgentProfile`, `Project` は個別ファイル
- `OnModelCreating` は空。スキルのシード（`CodeOptimizer`, `CreativeWriter`, `Translator`）はユーザー登録時（`/api/auth/register`）にユーザーごとに生成される

**AI統合** (`Services/AiService.cs`):
- `GetResponseAsync` / `GetResponseStreamAsync` — 通常チャット（SSEストリーミング対応）
- `CooperateAsync` — マルチエージェントパイプライン。`Project.Agents` が設定されていればカスタムエージェント列を順次実行、なければデフォルトの Orchestrator → Executor（最大2回リトライ） → Reviewer の3段階を実行
- `ExecuteCliAsync` / `SetupProcessInfo` — プロバイダー別にサブプロセス実行。`gemini --yolo` / `copilot --yolo --silent` / `claude --dangerously-skip-permissions` / `codex exec --dangerously-bypass-approvals-and-sandbox`

**フロントエンド** (`wwwroot/`): HTMX + Tailwind CSS + DaisyUI。サーバーはHTMLフラグメントを返す（JSON APIは一部プロジェクト系エンドポイントのみ）。Markdownレンダリングは `marked.js` がフロント側で実行。

## Key Patterns

- **メモリ注入**: `BuildSystemPromptAsync` → `MemorySearchService.SearchAsync` でユーザーメッセージのキーワードと `LongTermMemory.Tags` を照合し、一致したメモリをシステムプロンプトに埋め込む。
- **スキル注入**: `MemorySearchService.SearchSkillsAsync` でプロンプトキーワードと `Skill.TriggerKeywords` を照合。`agentRole` 指定時は `Skill.BoundAgentRole` でさらに絞り込む。
- **自動記憶統合**: 各チャット応答後に `MemoryConsolidationService.TryConsolidateAsync` を fire-and-forget で呼び出し、会話からAIが事実を抽出して `LongTermMemory` に自動保存。同一タグの既存記憶は上書き更新。
- **プロジェクト文脈**: `ChatSession.ProjectId` でプロジェクトを紐付け。AI呼び出し時に `Project.RootPath` を作業ディレクトリとして渡し、`Project.Agents`（`AgentProfile`）をシステムプロンプトに注入。
- **SSEストリーミング**: `/api/chat/stream`（通常）と `/api/chat/cooperate/stream`（マルチエージェント）の2エンドポイント。Cooperativeはカスタムイベント名（`session`, `step-complete`, `final`, `done`）でSSEを送信。
- **認証**: Cookie認証 + BCrypt。フォームPOSTエンドポイントには `.DisableAntiforgery()` を付与。
- **HTMLレンダリング**: `RenderMessage` ローカル関数（`Program.cs`末尾）と `BuildStepHtml` / `BuildCooperativeHtml`（`AiService.cs`）がHTMLフラグメントを生成。コンテンツは `HtmlEncode` 済み。

## AI Provider

実行環境に `gemini`、`copilot`（`gh extension`）、`claude`、`codex` のCLIが PATH 上に存在している必要がある。デフォルトプロバイダーは `gemini`。`AgentProfile.PreferredProvider` でエージェントごとに異なるプロバイダーを指定可能。
