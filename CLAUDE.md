# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# ビルド
dotnet build AiChatApp

# 実行 (http://localhost:5000)
dotnet run --project AiChatApp

# EF Core マイグレーション追加
dotnet ef migrations add <MigrationName> --project AiChatApp

# EF Core データベース更新
dotnet ef database update --project AiChatApp
```

データベース (`chat.db`) はアプリ起動時に `EnsureCreated()` で自動生成される。マイグレーションは不要だが、スキーマ変更時は `chat.db` を削除して再起動すれば再生成される。

## Architecture

**バックエンド**: ASP.NET Core 10 Minimal API。すべてのエンドポイントは `Program.cs` に直接定義（コントローラーなし）。

**データ層**: EF Core + SQLite。`AppDbContext` は `Data/AppDbContext.cs`、モデルは `Models/` 配下（`User`, `ChatSession`, `Message` は `Message.cs` に同居、`LongTermMemory` と `Skill` は個別ファイル）。

**AI統合** (`Services/AiService.cs`):
- `GetResponseAsync` — 通常チャット。ユーザーのメモリ・スキルを組み合わせてシステムプロンプトを構築し、CLI呼び出しへ渡す。
- `CooperateAsync` — 3段階マルチエージェント（Orchestrator → Executor → Reviewer）。各ステップで `CallAgentAsync` を逐次呼び出し、最終結果をHTMLフラグメントとして返す。
- `ExecuteCliAsync` — 実際のAI呼び出し。`gemini -p "..." --yolo` / `copilot -p "..." --yolo` / `claude -p "..." --dangerously-skip-permissions` をサブプロセスで実行する。

**フロントエンド** (`wwwroot/`): HTMX + Tailwind CSS + DaisyUI。`index.html` が全UIを担い、サーバーはHTMLフラグメントを返す（JSON APIではない）。`hx-target` でDOMを部分更新するSPA的動作。

## Key Patterns

- **メモリ注入**: `BuildEnhancedPrompt` でユーザーメッセージのキーワードと `LongTermMemory.Tags` を大文字小文字無視で照合し、一致したメモリをシステムプロンプトに埋め込む。
- **スキル注入**: 同様にプロンプト中にスキル名が含まれる場合、有効なスキルの説明をシステムプロンプトに追加する。
- **認証**: Cookie認証 + BCrypt。`/api/auth/*` は `.DisableAntiforgery()` を明示的に付与（フォームPOST対応）。
- **スキルのシードデータ**: `AppDbContext.OnModelCreating` で3つの初期スキル (`CodeOptimizer`, `CreativeWriter`, `Translator`) を `HasData` でシードしている。スキーマ変更時はDBを再作成しないとシードが反映されない点に注意。
- **HTMLレンダリング**: `RenderMessage` ローカル関数（`Program.cs`末尾）がメッセージHTMLフラグメントを生成。コンテンツは `HtmlEncode` 済みだが、フロントの `marked.js` でMarkdownレンダリングされる。

## AI Provider

実行環境に `gemini`、`copilot`（`gh extension`）、`claude` のCLIが PATH 上に存在している必要がある。デフォルトプロバイダーは `gemini`。
