# AGENTS.md

## Commands

```bash
# Build
dotnet build AiChatApp

# Run (http://localhost:5000)
dotnet run --project AiChatApp

# Reset database on schema change
rm AiChatApp/chat.db && dotnet run --project AiChatApp
```

## Architecture

- **Backend**: ASP.NET Core 10 Minimal API
- **Database**: SQLite via EF Core with `EnsureCreated()` - no migrations
- **Frontend**: HTMX + Tailwind CSS + DaisyUI (server returns HTML fragments)
- **Auth**: Cookie-based + BCrypt; use `.DisableAntiforgery()` on form POSTs
- **AI Providers**: Requires `gemini`, `copilot`, `claude`, `codex` CLIs in PATH

## Key Patterns

- **Memory injection**: Keywords matched against `LongTermMemory.Tags`, injected into system prompt
- **Skill injection**: Keywords matched against `Skill.TriggerKeywords`; empty = global
- **Auto memory consolidation**: Fire-and-forget after each response using hardcoded "gemini" provider
- **Project context**: Use `ChatSession.ProjectId` to link sessions to `Project`
- **SSE streaming**: 3 endpoints - `/api/chat/stream`, `/api/chat/cooperate/stream`, `/api/chat`

## Agent Discovery

Agents loaded from:
- `test-skill/` (single agent)
- `.gemini/skills/` (multiple agents)

Each directory needs `SKILL.md` with optional YAML frontmatter (`name:`, `description:`). Filesystem agents override DB `AgentProfile` on name conflict.

## Common Gotchas

- `chat.db` is created auto on first run - delete to reset
- HTML content is already `HtmlEncode`d - don't double-encode
- Multi-agent `CooperateAsync` uses SSE callbacks for step results
- Memory consolidation always uses "gemini" provider (hardcoded)