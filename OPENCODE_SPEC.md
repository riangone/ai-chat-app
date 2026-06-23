# Hyperion Platform — opencode Implementation Spec

## Overview

This document is the complete implementation specification for **opencode** to execute.
The goal is to evolve the existing **AiChatApp** (ASP.NET Core 10 + HTMX) into a significantly improved
platform called **Hyperion Platform**, by integrating design concepts from opencode's Desktop UI.

**Repository**: `/home/ubuntu/ws/ai-chat-app`
**Main project**: `/home/ubuntu/ws/ai-chat-app/AiChatApp/`
**Branch**: `feature/hyperion-improvements`

Do NOT create a new project. Modify the existing AiChatApp in place.

---

## Core Objectives (Priority Order)

1. **UI Redesign** — Redesign the chat page to resemble opencode's Desktop chat interface
2. **Shadow Git Snapshot** — Add ability to snapshot and revert file changes made by agents
3. **Plan Mode** — Before executing code changes, AI presents a plan for user approval
4. **LM Studio Integration** — Add local LLM support via LM Studio's OpenAI-compatible API
5. **CLI subprocess optimization** — Keep existing CLI providers (claude/gemini/copilot/codex), optimize pre-warm pool

---

## 1. UI REDESIGN

### Target Design (opencode Desktop Chat Reference)

The new chat page must have this exact layout:

```
┌──────────────────────────────────────────────────────────────────────┐
│ [Left Sidebar - 260px]      │ [Main Chat Area]                       │
│─────────────────────────────│────────────────────────────────────────│
│ ┌─ App Header ─────────┐    │ ┌─ Chat Header ──────────────────────┐ │
│ │ ◈ Hyperion           │    │ │ Session Title      [Provider▼][Mode]│ │
│ │ Platform             │    │ └────────────────────────────────────┘ │
│ └──────────────────────┘    │                                        │
│                             │ ┌─ Messages Area (scrollable) ───────┐ │
│ ┌─ New Chat ────────────┐   │ │                                    │ │
│ │ [+ New Conversation]  │   │ │  ┌─ User Message ───────────────┐  │ │
│ └──────────────────────┘    │ │  │ Avatar │ このバグを修正して  │  │ │
│                             │ │  └────────────────────────────────┘  │ │
│ ┌─ Sessions ────────────┐   │ │                                    │ │
│ │ ▾ Today               │   │ │  ┌─ Agent Steps (collapsible) ──┐  │ │
│ │   · Session #1        │   │ │  │ ▶ [Plan]  計画確認中...     │  │ │
│ │   · Session #2        │   │ │  │ ▶ [Read]  AiService.cs      │  │ │
│ │ ▾ Yesterday           │   │ │  │ ▶ [Edit]  3 lines changed   │  │ │
│ │   · Session #3        │   │ │  │ ▼ [Bash]  dotnet build ✓   │  │ │
│ └──────────────────────┘    │ │  │   stdout: Build succeeded   │  │ │
│                             │ │  └────────────────────────────────┘  │ │
│ ┌─ Quick Tools ─────────┐   │ │                                    │ │
│ │ [Todo] [Notes] [Files]│   │ │  ┌─ Assistant Response ─────────┐  │ │
│ └──────────────────────┘    │ │  │ ◈ │ 修正完了。変更点：      │  │ │
│                             │ │  │   │ ```diff                  │  │ │
│ ┌─ System ──────────────┐   │ │  │   │ - old code               │  │ │
│ │ Provider: ● gemini    │   │ │  │   │ + new code               │  │ │
│ │ Mode: [Normal▼]       │   │ │  │   └────────────────────────────┘  │ │
│ │ [Settings] [Logout]   │   │ │                                    │ │
│ └──────────────────────┘    │ └────────────────────────────────────┘ │
│                             │                                        │
│                             │ ══════ [Plan Approval Bar] ══════════ │
│                             │ 以下の計画を実行しますか？            │
│                             │ [承認して実行]  [修正]  [キャンセル] │
│                             │                                        │
│                             │ ┌─ Input Area ───────────────────────┐ │
│                             │ │ [Attach📎] [Textarea              ]│ │
│                             │ │             [@mention] [↑ Submit] │ │
│                             │ └────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────┘
```

### UI Implementation Details

**File to modify**: `AiChatApp/wwwroot/index.html` (currently 4316 lines)

#### Chat Tab Layout Changes

The current chat tab (`id="tab-chat"`) must be redesigned to:

1. **Two-panel layout**:
   - Left sidebar: `260px` fixed width, dark background (`bg-base-200`)
   - Right main area: `flex-1`, contains header + messages + input

2. **Left Sidebar Content**:
   - App logo/name: "◈ Hyperion" header
   - `[+ New Conversation]` button (calls existing `startNewChat()`)
   - Session list grouped by date (Today / Yesterday / Older)
     - Each session item: click to load, right-click menu: rename/delete
     - Currently active session highlighted
   - Quick tool buttons: Todo, Notes, File Manager icons at bottom
   - Provider selector dropdown (gemini/claude/copilot/codex/lmstudio)
   - Mode selector: Normal | Plan Mode
   - Settings icon link + Logout button

3. **Message Bubble Design** (opencode-style):
   - **User messages**: Right-aligned bubble, avatar initials on left
   - **Agent step messages**: Collapsible step cards with icons
     - Step types: `[Plan]` `[Read]` `[Edit]` `[Bash]` `[Search]`
     - Each step: icon + title (collapsed) / icon + title + content (expanded)
     - Color coding: blue=Plan, green=Read, yellow=Edit, purple=Bash
   - **Assistant final response**: Left-aligned with Hyperion avatar (◈)
   - Markdown rendering via existing `marked.js`
   - Code blocks: syntax-highlighted, copy button on hover

4. **Plan Approval Bar** (appears when Plan Mode active):
   - Fixed bar above input area
   - Shows plan summary
   - Three buttons: 承認して実行 (green) / 修正 (yellow) / キャンセル (red)
   - Only visible when AI has submitted a plan pending approval

5. **Input Area**:
   - Textarea (auto-resize, max 200px height)
   - Attach button (existing file upload)
   - `@mention` button for agent selection
   - Submit button (↑ arrow icon)
   - Keyboard shortcut: Ctrl+Enter to submit

6. **Provider/Mode Selector** (in sidebar bottom):
   - Dropdown showing current provider with colored dot indicator
   - Green dot = online/available
   - Gray dot = unavailable

#### CSS / Styling Notes
- Keep existing Tailwind CSS + DaisyUI
- Use `data-theme` for dark/light theme support
- Sidebar uses `bg-base-200`, main area uses `bg-base-100`
- Message bubbles: `rounded-2xl`, subtle shadow
- Step cards: `border-l-4` color-coded left border
- Transition animations: `transition-all duration-200`

---

## 2. SHADOW GIT SNAPSHOT

### Concept
When an AI agent modifies files in a Project's `RootPath`, automatically create git snapshots
so users can review and revert changes.

### Backend Implementation

**New Model**: `AiChatApp/Models/FileSnapshot.cs`
```csharp
public class FileSnapshot
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public int MessageId { get; set; }
    public string ProjectPath { get; set; } = "";
    public string SnapshotCommitHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ChatSession Session { get; set; } = null!;
}
```

**New Service**: `AiChatApp/Services/ShadowGitService.cs`

```csharp
public class ShadowGitService
{
    // Shadow repo is stored at: {ProjectRootPath}/.shadow-git/
    // It is a separate git repo that tracks AI-made changes

    public async Task<string?> CreateSnapshotAsync(string projectPath, string message)
    // 1. Check if .shadow-git/ exists in projectPath, if not: git init
    // 2. Run: git -C projectPath add -A
    // 3. Run: git -C projectPath commit -m "{message} [hyperion-snapshot]"
    // 4. Return the commit hash

    public async Task<List<SnapshotInfo>> ListSnapshotsAsync(string projectPath)
    // Run: git -C projectPath log --oneline --grep="[hyperion-snapshot]"
    // Parse output into SnapshotInfo list

    public async Task<string> GetSnapshotDiffAsync(string projectPath, string commitHash)
    // Run: git -C projectPath show {commitHash} --stat
    // Return diff summary

    public async Task<bool> RevertToSnapshotAsync(string projectPath, string commitHash)
    // Run: git -C projectPath reset --hard {commitHash}
    // Return success/failure
}
```

**Integration point**: In `AiService.cs`, after `CooperateAsync` or `GetResponseAsync` completes,
if the session has a `ProjectId`, call `ShadowGitService.CreateSnapshotAsync`.

**New Endpoints**: Add to `AiChatApp/Endpoints/` a new file `SnapshotEndpoints.cs`:
- `GET /api/projects/{projectId}/snapshots` — list snapshots
- `GET /api/projects/{projectId}/snapshots/{hash}/diff` — show diff
- `POST /api/projects/{projectId}/snapshots/{hash}/revert` — revert to snapshot

**DB**: Add `FileSnapshots` table to `AppDbContext.cs` and `InitializeDatabaseAsync`.

### Frontend Integration
- In session detail, if a project is linked, show "Snapshots" button
- Clicking opens a modal listing snapshots with timestamps
- Each snapshot: "View Diff" and "Revert" buttons
- Revert shows confirmation dialog before executing

---

## 3. PLAN MODE

### Concept
When Plan Mode is enabled, before the AI executes any file changes, it must:
1. Analyze the request
2. Present a structured plan (what files to modify, what changes)
3. Wait for user approval
4. Only then execute the changes

### Backend Implementation

**New field on `ChatSession`**: `PlanModeEnabled bool` (add to model + ALTER TABLE)

**API changes in `AiService.cs`**:

Add method `GetPlanAsync(string userMessage, string systemPrompt, ...) -> string`:
- Prompt template: instructs AI to ONLY output a plan in JSON format, NOT execute
- Plan JSON schema:
```json
{
  "title": "Fix null reference in AiService.cs",
  "steps": [
    {"action": "Read", "target": "Services/AiService.cs", "reason": "Understand current structure"},
    {"action": "Edit", "target": "Services/AiService.cs", "lines": "45-52", "change": "Add null check"},
    {"action": "Bash", "command": "dotnet build AiChatApp", "reason": "Verify fix compiles"}
  ],
  "estimated_changes": "~10 lines",
  "risk": "low"
}
```

**New endpoint**: `POST /api/chat/plan`
- Takes: `{sessionId, message, provider}`
- Returns: SSE stream with plan JSON
- Does NOT execute anything

**Modified endpoint**: `POST /api/chat/cooperate`
- New parameter: `approvedPlan` (optional JSON string)
- If plan mode is on and no `approvedPlan`: call `/api/chat/plan` instead
- If `approvedPlan` provided: inject plan as context, execute normally

### Frontend Implementation

1. **Mode toggle in sidebar**: `[Normal] [Plan Mode]` toggle
   - Saves preference to `localStorage`
   - Sends `planMode: true` with each chat request

2. **Plan display**:
   - When plan received, render as collapsible step list in a special "Plan" bubble
   - Each step shows action icon + target + reason
   - Show risk badge (low/medium/high)

3. **Plan Approval Bar** (appears after plan is displayed):
   - 承認して実行: sends same message again with `approvedPlan` attached
   - 修正: opens textarea to edit plan JSON (advanced)
   - キャンセル: dismisses plan, clears pending state

---

## 4. LM STUDIO INTEGRATION

### Concept
LM Studio runs a local OpenAI-compatible server at `http://localhost:1234/v1`.
No new SDK needed — use `System.Net.Http.HttpClient` with OpenAI REST API format.

### Backend Implementation

**New file**: `AiChatApp/Services/Infrastructure/LmStudioProvider.cs`

```csharp
public class LmStudioProvider
{
    private readonly HttpClient _http;
    private const string BaseUrl = "http://localhost:1234/v1";

    // Check if LM Studio is running
    public async Task<bool> IsAvailableAsync()
    // GET /v1/models → returns 200 if running

    // Get available models
    public async Task<List<string>> GetModelsAsync()
    // GET /v1/models → parse model list

    // Generate response (non-streaming)
    public async Task<string> GetResponseAsync(string prompt, string model = "")
    // POST /v1/chat/completions with stream: false

    // Generate response (streaming SSE)
    public async IAsyncEnumerable<string> GetResponseStreamAsync(string prompt, string model = "")
    // POST /v1/chat/completions with stream: true
    // Yield each content delta
}
```

**Integration in `CliExecutorService.cs`**:
- Add `"lmstudio"` as a recognized provider name
- Route to `LmStudioProvider` instead of CLI subprocess
- Return same `ExecuteResult` format as other providers

**Configuration in `appsettings.json`**:
```json
"LmStudio": {
  "BaseUrl": "http://localhost:1234/v1",
  "DefaultModel": "",
  "Enabled": true
}
```

**New endpoint**: `GET /api/lmstudio/status` — returns `{available: bool, models: [...]}`

### Frontend Integration
- In provider selector dropdown: add "LM Studio" option with local indicator
- If `GET /api/lmstudio/status` returns `available: false`, show option as grayed out
- When LM Studio selected and multiple models available, show model selector sub-dropdown

---

## 5. CLI SUBPROCESS OPTIMIZATION

The existing `CliExecutorService.cs` has a pre-warm pool. Keep it and add:

1. **Health check endpoint**: `GET /api/cli/health`
   - Returns pool status: `{gemini: {poolSize: 1, ready: true}, claude: {poolSize: 0, ready: false}, ...}`

2. **Warm pool size config** in `appsettings.json`:
```json
"CliSettings": {
  "PreWarmPoolSize": 2,
  "MaxConcurrentProcesses": 4
}
```

3. **Expand pool size from 1 to 2** for gemini (most used provider)

---

## DATABASE CHANGES

Add to `AppDbContext.cs`:
```csharp
public DbSet<FileSnapshot> FileSnapshots { get; set; }
```

Add to `ApplicationExtensions.InitializeDatabaseAsync`:
```sql
ALTER TABLE ChatSessions ADD COLUMN PlanModeEnabled INTEGER NOT NULL DEFAULT 0;
CREATE TABLE IF NOT EXISTS FileSnapshots (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId INTEGER NOT NULL,
    MessageId INTEGER,
    ProjectPath TEXT NOT NULL,
    SnapshotCommitHash TEXT NOT NULL,
    CreatedAt TEXT NOT NULL
);
```

---

## FILE CHANGE SUMMARY

Files to CREATE:
- `AiChatApp/Models/FileSnapshot.cs`
- `AiChatApp/Services/ShadowGitService.cs`
- `AiChatApp/Services/Infrastructure/LmStudioProvider.cs`
- `AiChatApp/Endpoints/SnapshotEndpoints.cs`

Files to MODIFY:
- `AiChatApp/wwwroot/index.html` — Major UI redesign of chat tab
- `AiChatApp/Data/AppDbContext.cs` — Add FileSnapshots DbSet
- `AiChatApp/Extensions/ApplicationExtensions.cs` — Add DB migration for new tables/columns
- `AiChatApp/Extensions/ServiceExtensions.cs` — Register ShadowGitService, LmStudioProvider
- `AiChatApp/Program.cs` — Map SnapshotEndpoints
- `AiChatApp/Services/AiService.cs` — Integrate ShadowGitService, add GetPlanAsync
- `AiChatApp/Services/Infrastructure/CliExecutorService.cs` — Add lmstudio routing
- `AiChatApp/appsettings.json` — Add LmStudio and CliSettings sections
- `AiChatApp/Models/Message.cs` (or relevant model file) — Add PlanModeEnabled to ChatSession

---

## IMPLEMENTATION ORDER

Execute in this order to avoid breaking the app:

1. **Database changes** (AppDbContext + ApplicationExtensions) — Non-breaking additions
2. **LmStudioProvider** — New independent service, easy to test in isolation
3. **ShadowGitService** — New independent service
4. **SnapshotEndpoints** — Wire up the new service
5. **AiService changes** — Add GetPlanAsync, integrate ShadowGitService
6. **Plan Mode API** — New endpoint + ChatSession field
7. **UI redesign** — Last, biggest change; test incrementally

---

## TESTING

After implementation, verify:
```bash
# Build must pass
dotnet build AiChatApp

# Start app
dotnet run --project AiChatApp

# Verify endpoints exist
curl http://localhost:5000/api/lmstudio/status
curl http://localhost:5000/api/cli/health
curl -X POST http://localhost:5000/api/chat/plan -H "Content-Type: application/json" \
  -d '{"sessionId":1,"message":"test","provider":"gemini"}' --cookie "..."
```

App runs on `http://localhost:5000`. Default login: `admin` / `admin123`.

---

## CONSTRAINTS

- Do NOT break existing functionality (chat, memory, skills, multi-agent, briefing, todo, notes)
- Do NOT change authentication system
- Do NOT add new npm/Node.js dependencies — frontend is pure HTMX + Tailwind + DaisyUI (all local in wwwroot/lib/)
- All new .NET packages must be added via `dotnet add package`
- Keep SSE streaming pattern consistent with existing `/api/chat/stream`
- The `AiSettings:DefaultProvider` is currently `"antigravity"` (a custom gemini wrapper) — do not change this
- Database uses `EnsureCreated()` + `ALTER TABLE` pattern, NOT EF Core migrations

---

## NOTES ON EXISTING CODE

- `CliExecutorService.cs:654 lines` — handles all CLI providers with pre-warm pool
- `AiService.cs:492 lines` — main AI orchestration service
- `ChatEndpoints.cs:594 lines` — all chat HTTP endpoints including SSE streaming
- `index.html:4316 lines` — single-page app with all tabs; chat tab starts around line 800
- `AppDbContext.cs:73 lines` — minimal, all config in ApplicationExtensions
- Providers currently working: `antigravity` (gemini wrapper), `claude`, `copilot`, `codex`
- Provider name `gemini` also works as alias for antigravity

Good luck. Build it incrementally and test after each major step.
