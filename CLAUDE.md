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

データベース (`chat.db`) はアプリ起動時に `EnsureCreated()` で自動生成される。EF Core マイグレーションは使用しない。

## Architecture

**バックエンド**: ASP.NET Core 10 Minimal API。エンドポイントは `Program.cs`（チャット・認証・メモリ・スキル）と `Services/ProjectApiController.cs`（プロジェクト管理、`MapProjectEndpoints()` 拡張メソッド）に分割。

**データ層**: EF Core + SQLite（`Data/AppDbContext.cs`）。モデル構成：
- `User`, `ChatSession`, `Message`, `AgentStep` は `Models/Message.cs` に同居
- `LongTermMemory`, `Skill`, `AgentProfile`, `Project` は個別ファイル
- `OnModelCreating` は空。スキルのシード（`CodeOptimizer`, `CreativeWriter`, `Translator`）はユーザー登録時（`/api/auth/register`）にユーザーごとに生成される

**AI統合** (`Services/AiService.cs`):
- `GetResponseAsync` / `GetResponseStreamAsync` — 通常チャット（SSEストリーミング対応）
- `CooperateAsync` — マルチエージェントパイプライン。優先順位：(1) `selectedAgentNames` 引数 → (2) セッションのプロジェクトカスタムエージェント → (3) デフォルト3段階（Orchestrator → Executor 最大2回リトライ → Reviewer）。各ステップ完了時に `onStepComplete` コールバックでSSEに中間結果を送信する
- `ExecuteCliAsync` / `SetupProcessInfo` — プロバイダー別にサブプロセス実行。`gemini --yolo` / `copilot --yolo --silent` / `claude --dangerously-skip-permissions` / `codex exec --dangerously-bypass-approvals-and-sandbox`
- `GetAvailableAgentsAsync` — エージェント定義を(1)ファイルシステム、(2)DBから収集

**フロントエンド** (`wwwroot/`): HTMX + Tailwind CSS + DaisyUI。サーバーはHTMLフラグメントを返す（JSON APIは一部プロジェクト系エンドポイントのみ）。Markdownレンダリングは `marked.js` がフロント側で実行。

## Key Patterns

- **メモリ注入**: `BuildSystemPromptAsync` → `MemorySearchService.SearchAsync` でユーザーメッセージのキーワードと `LongTermMemory.Tags` を照合し、一致したメモリをシステムプロンプトに埋め込む。検索は3段優先度（タグ完全一致→タグ部分一致→コンテンツ部分一致）。
- **スキル注入**: `MemorySearchService.SearchSkillsAsync` でプロンプトキーワードと `Skill.TriggerKeywords` を照合。`agentRole` 指定時は `Skill.BoundAgentRole` でさらに絞り込む。`TriggerKeywords` が空のスキルは常に有効（グローバルスキル）。
- **自動記憶統合**: 各チャット応答後に `MemoryConsolidationService.TryConsolidateAsync` を fire-and-forget で呼び出し、会話からAIが事実を抽出して `LongTermMemory` に自動保存。同一タグの既存記憶は上書き更新。この処理は常に "gemini" プロバイダーを使用（ハードコード）。
- **プロジェクト文脈**: `ChatSession.ProjectId` でプロジェクトを紐付け。AI呼び出し時に `Project.RootPath` を作業ディレクトリとして渡し、`Project.Agents`（`AgentProfile`）をシステムプロンプトに注入。
- **SSEストリーミング**: 3エンドポイント存在：
  - `/api/chat` — 通常・協調の両モード対応。`mode=cooperative` か `selectedAgents` 指定で協調モード。レスポンスはHTMLフラグメント（非SSE）。
  - `/api/chat/stream` — 通常チャット専用SSE。`data: [DONE]` で終端。
  - `/api/chat/cooperate/stream` — 協調マルチエージェント専用SSE。カスタムイベント（`session`, `step-complete`, `final`, `done`）を送信。`session` イベントはセッションIDとエージェント名リストをJSON送信。
- **認証**: Cookie認証 + BCrypt。フォームPOSTエンドポイントには `.DisableAntiforgery()` を付与。
- **HTMLレンダリング**: `RenderMessage` ローカル関数（`Program.cs` 末尾）と `BuildStepHtml` / `BuildCooperativeHtml`（`AiService.cs`）がHTMLフラグメントを生成。コンテンツは `HtmlEncode` 済み。

## ファイルシステムベースのエージェント（SKILL.md）

`GetAvailableAgentsAsync` は以下のディレクトリからエージェント定義を読み込む：
- `test-skill/` — 単一エージェントディレクトリ
- `.gemini/skills/` — 複数エージェントの親ディレクトリ（サブディレクトリを列挙）

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

実行環境に `gemini`、`copilot`（`gh extension`）、`claude`、`codex` のCLIが PATH 上に存在している必要がある。デフォルトプロバイダーは `gemini`。`AgentProfile.PreferredProvider` でエージェントごとに異なるプロバイダーを指定可能。
