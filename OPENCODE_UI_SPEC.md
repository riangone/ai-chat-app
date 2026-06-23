# Chat UI Redesign Spec — Hyperion Platform

## Context

You are working on `AiChatApp` — an ASP.NET Core 10 web app with HTMX + Tailwind + DaisyUI frontend.
Working directory: `/home/ubuntu/ws/ai-chat-app/AiChatApp`

Redesign the Chat page UI inspired by opencode Desktop. Modify existing files only.

Target files:
- `wwwroot/index.html` (4316 lines)
- `wwwroot/components/sidebar.html` (44 lines)
- `wwwroot/components/footer.html` (52 lines)
- `Endpoints/ChatEndpoints.cs` (for JSON sessions endpoint)

Run `dotnet build AiChatApp` after each major change to verify no errors.

---

## 1. JSON Sessions Endpoint

**File**: `Endpoints/ChatEndpoints.cs`

The existing `/api/chat/list` returns HTML fragments. We need a JSON endpoint for the sidebar.

Find where `group.MapGet("/chat/list"` is and **add a new endpoint after it**:

```csharp
group.MapGet("/chat/sessions", async (int? limit, AppDbContext db, ClaimsPrincipal user) => {
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var take = limit ?? 20;
    var sessions = await db.ChatSessions
        .AsNoTracking()
        .Where(s => s.UserId == userId)
        .OrderByDescending(s => s.UpdatedAt)
        .Take(take)
        .Select(s => new { s.Id, s.Title, provider = s.PreferredProvider, updatedAt = s.UpdatedAt })
        .ToListAsync();
    return Results.Ok(sessions);
}).RequireAuthorization();
```

---

## 2. Sidebar — Session List Section

**File**: `wwwroot/components/sidebar.html`

The sidebar file ends with `</aside>`. The last significant section before `</aside>` is:
```html
<div class="p-4 mt-auto border-t border-base-content/5">
```

**INSERT** the following block BEFORE that `<div class="p-4 mt-auto border-t ...">` line:

```html
<!-- Recent Sessions -->
<div class="border-t border-base-content/5 pt-1">
    <button class="w-full flex items-center justify-between px-4 py-2 text-left select-none hover:bg-base-200/30 transition-colors" onclick="toggleSidebarSessions()">
        <span class="text-[9px] font-black uppercase tracking-[0.2em] opacity-30">Sessions</span>
        <svg id="sidebar-sessions-chevron" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="3" stroke="currentColor" class="w-2.5 h-2.5 opacity-20 transition-transform duration-200">
            <path stroke-linecap="round" stroke-linejoin="round" d="M19.5 8.25l-7.5 7.5-7.5-7.5" />
        </svg>
    </button>
    <div id="sidebar-sessions-panel" class="hidden overflow-y-auto max-h-44 custom-scrollbar px-2 pb-2 space-y-0.5">
        <div id="sidebar-sessions-inner" class="flex items-center justify-center py-3">
            <span class="loading loading-spinner loading-xs opacity-20"></span>
        </div>
    </div>
</div>
```

---

## 3. Footer — Add LM Studio + Plan Mode Toggle

**File**: `wwwroot/components/footer.html`

### 3a. Add LM Studio provider option

Find the provider select dropdown. It currently ends with:
```html
<option value="opencode">OpenCode</option>
```

Add after that line:
```html
<option value="lmstudio">LM Studio</option>
```

### 3b. Add Plan Mode toggle

Find this block in footer.html:
```html
                    <div class="flex items-center gap-1.5 ml-2">
                        <input type="checkbox" id="stream-toggle" class="checkbox checkbox-xs checkbox-primary" checked onchange="saveStreamPreference(this.checked)" />
                        <label for="stream-toggle" class="text-[10px] font-bold opacity-40 cursor-pointer uppercase">Stream</label>
                    </div>
```

**AFTER** that div (but still inside the parent flex container), add:
```html
                    <div class="flex items-center gap-1.5 ml-2">
                        <input type="checkbox" id="plan-mode-toggle" class="checkbox checkbox-xs checkbox-warning" onchange="onPlanModeToggle(this.checked)" />
                        <label for="plan-mode-toggle" class="text-[10px] font-bold opacity-40 cursor-pointer uppercase text-warning">Plan</label>
                    </div>
```

---

## 4. index.html — CSS Additions

**File**: `wwwroot/index.html`

Find the `<style>` block (starts around line 25). Add the following CSS **inside** the style block, at the end before `</style>`:

```css
        /* Agent Steps */
        .agent-steps-wrap {
            border: 1px solid hsl(var(--bc) / 0.07);
            border-radius: 14px;
            overflow: hidden;
            margin-bottom: 0.5rem;
            background: hsl(var(--b2) / 0.5);
        }
        .agent-step-row {
            display: flex;
            align-items: center;
            gap: 0.5rem;
            padding: 0.45rem 0.75rem;
            font-size: 11px;
            cursor: pointer;
            border-bottom: 1px solid hsl(var(--bc) / 0.05);
            transition: background 0.12s;
            user-select: none;
        }
        .agent-step-row:last-child { border-bottom: none; }
        .agent-step-row:hover { background: hsl(var(--bc) / 0.04); }
        .agent-step-badge {
            display: inline-flex;
            align-items: center;
            padding: 0.12rem 0.4rem;
            border-radius: 5px;
            font-size: 9px;
            font-weight: 900;
            letter-spacing: 0.06em;
            text-transform: uppercase;
            min-width: 52px;
            justify-content: center;
            shrink: 0;
        }
        .sbadge-orch  { background: hsl(var(--p) / 0.15); color: hsl(var(--p)); }
        .sbadge-exec  { background: hsl(200 80% 50% / 0.12); color: hsl(200 80% 38%); }
        .sbadge-rev   { background: hsl(var(--a) / 0.12); color: hsl(var(--a)); }
        .sbadge-done  { background: hsl(var(--su) / 0.12); color: hsl(142 71% 35%); }
        .sbadge-err   { background: hsl(var(--er) / 0.12); color: hsl(var(--er)); }
        .agent-step-detail {
            display: none;
            padding: 0.5rem 0.75rem 0.75rem 1rem;
            font-size: 10.5px;
            font-family: ui-monospace, SFMono-Regular, monospace;
            white-space: pre-wrap;
            word-break: break-all;
            opacity: 0.65;
            background: hsl(var(--b3) / 0.4);
            border-top: 1px solid hsl(var(--bc) / 0.05);
            max-height: 200px;
            overflow-y: auto;
        }
        .agent-step-row.step-open + .agent-step-detail { display: block; }
        /* Plan Approval Bar */
        #plan-approval-bar {
            border-top: 1px solid hsl(var(--wa) / 0.25);
            background: hsl(var(--wa) / 0.06);
            padding: 0.75rem 1rem;
            animation: fadeIn 0.2s ease-out;
        }
        /* Provider badge in header */
        .provider-status-dot {
            width: 6px;
            height: 6px;
            border-radius: 50%;
            background: hsl(var(--su));
            display: inline-block;
            animation: pulseGlow 2s infinite;
        }
        @keyframes pulseGlow {
            0%, 100% { opacity: 1; box-shadow: 0 0 0 0 hsl(var(--su) / 0.4); }
            50% { opacity: 0.8; box-shadow: 0 0 0 4px hsl(var(--su) / 0); }
        }
```

---

## 5. index.html — Provider Badge in Chat Header

**File**: `wwwroot/index.html`

Find the chat slide's shell-header. It contains a `<div class="shell-badge">` with a session title span. Find the line containing `id="chat-session-title"` or the chat shell-badge.

Search for this pattern in the chat slide area (around line 3380-3430):
```html
<div class="shell-badge">
```
in the context of the chat swiper-slide (not other slides).

After the `</div>` that closes that shell-badge, add:
```html
                                <div id="chat-provider-badge" class="hidden items-center gap-1.5 px-2 py-1 rounded-lg bg-base-200 border border-base-content/8 text-[10px] font-bold opacity-70 ml-1">
                                    <span class="provider-status-dot"></span>
                                    <span id="chat-provider-name">gemini</span>
                                </div>
```

---

## 6. index.html — Plan Approval Bar HTML

**File**: `wwwroot/index.html`

Find the line `<div id="chat-panel" class="flex flex-col flex-1">`. The chat panel structure is:
```html
<div id="chat-panel" class="flex flex-col flex-1">
    <div id="chat-box" ...>
        ...
    </div>
</div>
```

**AFTER** the closing `</div>` of `chat-panel` (and its `chat-box` child), add the plan approval bar. 

Find the exact closing `</div>` after `<div id="chat-panel"` and insert after:

```html
                        <!-- Plan Approval Bar -->
                        <div id="plan-approval-bar" class="hidden">
                            <div class="flex items-center gap-3 px-4 py-3 max-w-3xl mx-auto">
                                <div class="w-7 h-7 rounded-lg bg-warning/20 text-warning flex items-center justify-center shrink-0">
                                    <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor" class="w-3.5 h-3.5"><path stroke-linecap="round" stroke-linejoin="round" d="M9 12h3.75M9 15h3.75M9 18h3.75m3 .75H18a2.25 2.25 0 002.25-2.25V6.108c0-1.135-.845-2.098-1.976-2.192a48.424 48.424 0 00-1.123-.08m-5.801 0c-.065.21-.1.433-.1.664 0 .414.336.75.75.75h4.5a.75.75 0 00.75-.75 2.25 2.25 0 00-.1-.664m-5.8 0A2.251 2.251 0 0113.5 2.25H15c1.012 0 1.867.668 2.15 1.586m-5.8 0c-.376.023-.75.05-1.124.08C9.095 4.01 8.25 4.973 8.25 6.108V8.25m0 0H4.875c-.621 0-1.125.504-1.125 1.125v11.25c0 .621.504 1.125 1.125 1.125h9.75c.621 0 1.125-.504 1.125-1.125V9.375c0-.621-.504-1.125-1.125-1.125H8.25z" /></svg>
                                </div>
                                <div class="flex-1 min-w-0">
                                    <p class="text-[10px] font-black text-warning uppercase tracking-wider">Plan Ready</p>
                                    <p id="plan-approval-summary" class="text-xs opacity-60 truncate mt-0.5"></p>
                                </div>
                                <div class="flex items-center gap-2 shrink-0">
                                    <button onclick="approvePlan()" class="btn btn-warning btn-xs rounded-xl font-bold shadow-sm border-0">
                                        <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2.5" stroke="currentColor" class="w-3 h-3"><path stroke-linecap="round" stroke-linejoin="round" d="M4.5 12.75l6 6 9-13.5" /></svg>
                                        Approve & Run
                                    </button>
                                    <button onclick="dismissPlan()" class="btn btn-ghost btn-xs rounded-xl opacity-50 hover:opacity-100">
                                        Cancel
                                    </button>
                                </div>
                            </div>
                        </div>
```

---

## 7. index.html — JavaScript Additions

**File**: `wwwroot/index.html`

Find the large `<script>` block. Add the following functions. Best place: after the `handleFormSubmit` / `sendMessageStream` functions (around line 620+), before the `addUserMessage` function.

### 7a. Sidebar Session List Functions

```javascript
        // ---- Sidebar Session List ----
        function toggleSidebarSessions() {
            const panel = document.getElementById('sidebar-sessions-panel');
            const chevron = document.getElementById('sidebar-sessions-chevron');
            if (!panel) return;
            const opening = panel.classList.contains('hidden');
            panel.classList.toggle('hidden', !opening);
            if (chevron) chevron.style.transform = opening ? 'rotate(180deg)' : '';
            if (opening) loadSidebarSessions();
        }

        async function loadSidebarSessions() {
            const inner = document.getElementById('sidebar-sessions-inner');
            if (!inner) return;
            try {
                const r = await fetch('/api/chat/sessions?limit=20');
                if (!r.ok) throw new Error();
                const sessions = await r.json();
                if (!sessions.length) {
                    inner.innerHTML = '<p class="text-[10px] opacity-25 text-center py-2">No sessions yet</p>';
                    return;
                }
                inner.innerHTML = sessions.map(s => {
                    const title = (s.title || 'Untitled').replace(/</g,'&lt;').replace(/>/g,'&gt;');
                    const provDot = s.provider ? `<span class="w-1.5 h-1.5 rounded-full bg-primary/50 shrink-0"></span>` : '';
                    return `<div class="flex items-center gap-1.5 px-2 py-1.5 rounded-xl cursor-pointer hover:bg-base-300/60 transition-colors group sidebar-session-item" data-session-id="${s.id}" onclick="openSidebarSession(${s.id}, this)" title="${title}">
                        ${provDot}
                        <span class="text-[11px] font-medium opacity-55 group-hover:opacity-90 truncate flex-1">${title}</span>
                    </div>`;
                }).join('');
            } catch(e) {
                inner.innerHTML = '<p class="text-[10px] opacity-25 text-center py-2">Failed to load</p>';
            }
        }

        async function openSidebarSession(sessionId, el) {
            document.querySelectorAll('.sidebar-session-item').forEach(d => d.classList.remove('bg-primary/10'));
            el?.classList.add('bg-primary/10');
            openSlide('chat');
            document.getElementById('session-id-input').value = sessionId;
            document.getElementById('delete-current-chat-btn')?.classList.remove('hidden');
            const panel = document.getElementById('chat-panel');
            if (panel) {
                panel.innerHTML = '<div class="flex-1 flex items-center justify-center opacity-20"><span class="loading loading-spinner loading-md"></span></div>';
                try {
                    const r = await fetch(`/api/chat/load/${sessionId}`);
                    if (r.ok) { panel.innerHTML = await r.text(); renderMarkdown(); scrollToBottom(); }
                } catch(e) { panel.innerHTML = ''; }
            }
        }
        // ---- End Sidebar Session List ----
```

### 7b. Provider Badge Functions

```javascript
        // ---- Provider Badge ----
        function updateChatProviderBadge(provider) {
            const badge = document.getElementById('chat-provider-badge');
            const nameEl = document.getElementById('chat-provider-name');
            if (!badge || !nameEl) return;
            const labels = { antigravity: 'Gemini', gemini: 'Gemini', claude: 'Claude', copilot: 'Copilot', codex: 'Codex', opencode: 'OpenCode', lmstudio: 'LM Studio' };
            const label = labels[provider] || provider;
            nameEl.textContent = label;
            badge.classList.toggle('hidden', !provider || provider === '');
            badge.classList.toggle('flex', !!provider && provider !== '');
        }
        // ---- End Provider Badge ----
```

### 7c. Agent Steps Functions

```javascript
        // ---- Agent Steps ----
        window._agentSteps = {};

        function getStepBadgeClass(role) {
            const r = (role || '').toLowerCase();
            if (r.includes('orchestrator') || r.includes('plan')) return 'sbadge-orch';
            if (r.includes('executor') || r.includes('exec')) return 'sbadge-exec';
            if (r.includes('reviewer') || r.includes('review')) return 'sbadge-rev';
            if (r.includes('error') || r.includes('fail')) return 'sbadge-err';
            return 'sbadge-done';
        }

        function renderAgentSteps(steps) {
            if (!steps || !steps.length) return '';
            const rows = steps.map(step => {
                const role = step.agentRole || step.role || 'Agent';
                const bc = getStepBadgeClass(role);
                const summary = (step.content || step.summary || '').replace(/</g,'&lt;').replace(/>/g,'&gt;').slice(0,100);
                const detail = (step.content || '').replace(/</g,'&lt;').replace(/>/g,'&gt;');
                const statusIcon = step.isCompleted ? '✓' : '·';
                return `<div class="agent-step-row" onclick="this.classList.toggle('step-open')">
                    <span class="agent-step-badge ${bc}">${role.slice(0,10)}</span>
                    <span class="flex-1 opacity-55 truncate text-[10px]">${summary}</span>
                    <span class="text-[9px] opacity-30">${statusIcon}</span>
                </div><div class="agent-step-detail">${detail}</div>`;
            }).join('');
            return `<div class="agent-steps-wrap">${rows}</div>`;
        }

        function updateBubbleAgentSteps(bubbleId, steps) {
            const el = document.getElementById(bubbleId);
            if (!el) return;
            const existing = el.querySelector('.agent-steps-wrap');
            if (existing) existing.remove();
            const stepsHtml = renderAgentSteps(steps);
            if (stepsHtml) {
                const bubble = el.querySelector('.chat-bubble');
                if (bubble) bubble.insertAdjacentHTML('beforebegin', stepsHtml);
            }
        }
        // ---- End Agent Steps ----
```

### 7d. Plan Mode Functions

```javascript
        // ---- Plan Mode ----
        let _pendingPlan = null;
        let _pendingPlanPrompt = '';

        function onPlanModeToggle(enabled) {
            const modeInput = document.getElementById('mode-input');
            if (modeInput) modeInput.value = enabled ? 'plan' : '';
            if (!enabled) dismissPlan();
        }

        function showPlanApprovalBar(summary) {
            const bar = document.getElementById('plan-approval-bar');
            const sumEl = document.getElementById('plan-approval-summary');
            if (!bar) return;
            if (sumEl) sumEl.textContent = (summary || '').slice(0, 120);
            bar.classList.remove('hidden');
        }

        function dismissPlan() {
            document.getElementById('plan-approval-bar')?.classList.add('hidden');
            _pendingPlan = null;
            _pendingPlanPrompt = '';
        }

        async function approvePlan() {
            if (!_pendingPlan) return;
            const prompt = _pendingPlanPrompt;
            dismissPlan();
            // Re-send with execute mode
            const fd = new FormData();
            fd.append('sessionId', document.getElementById('session-id-input')?.value || '');
            fd.append('content', `[APPROVED PLAN - Execute now]\n\n${prompt}`);
            fd.append('provider', document.querySelector('select[name="provider"]')?.value || '');
            // Turn off plan mode for execution
            const modeInput = document.getElementById('mode-input');
            if (modeInput) modeInput.value = '';
            document.getElementById('chat-input').value = '';
            const isStream = document.getElementById('stream-toggle')?.checked !== false;
            try {
                setRequestState(true);
                if (isStream) {
                    await sendMessageStream(fd);
                } else {
                    await sendMessageNormal(fd);
                }
            } finally {
                setRequestState(false);
            }
        }

        function checkForPlanInResponse(text) {
            if (!document.getElementById('plan-mode-toggle')?.checked) return;
            if (!text) return;
            const lower = text.toLowerCase();
            const isPlan = lower.includes('## plan') || lower.includes('# plan') ||
                           lower.includes('step 1:') || lower.includes('step 1.') ||
                           (lower.includes('plan:') && lower.includes('step'));
            if (isPlan) {
                _pendingPlan = text;
                _pendingPlanPrompt = document.getElementById('chat-input')?.value || text.slice(0, 200);
                const firstLine = text.split('\n').find(l => l.trim()) || 'Plan generated';
                showPlanApprovalBar(firstLine.replace(/^#+\s*/, ''));
            }
        }
        // ---- End Plan Mode ----
```

---

## 8. Wire up existing JS functions

**File**: `wwwroot/index.html`

### 8a. Wire provider badge to changeSessionProvider

Find `async function changeSessionProvider(provider)` (around line 255). At the end of the function body (before the closing `}`), add:
```javascript
            updateChatProviderBadge(provider);
```

Also find `async function changeDefaultProvider(provider)` (around line 266). At the end of the function body, add:
```javascript
            updateChatProviderBadge(provider);
```

### 8b. Wire plan detection to sendMessageStream

Find the `sendMessageStream` function. At the end of the streaming loop, after all chunks are received and the response is complete, add a call to `checkForPlanInResponse(acc)`.

The current code after the while loop is:
```javascript
            } catch(err) { b.innerHTML=`<span class="text-error">${i18n('request-error')}</span>`; }
        }
```

**Insert before the `catch`** (after `while` loop closing brace):
```javascript
                // Check if plan mode response
                checkForPlanInResponse(acc);
```

### 8c. Wire sidebar session list refresh after new chats

Find the `handleFormSubmit` function. It contains:
```javascript
                loadPanelData('history-panel'); 
```

After that line, add:
```javascript
                if (document.getElementById('sidebar-sessions-panel') && !document.getElementById('sidebar-sessions-panel').classList.contains('hidden')) {
                    loadSidebarSessions();
                }
```

### 8d. Wire agent steps to cooperative stream

Find `if (fd.get('mode') === 'cooperative') agentName = 'Multi-Agent';` around line 604.

After this line, add:
```javascript
            const currentCoopBubbleId = id;
            window._agentSteps[id] = [];
```

This sets up per-bubble step tracking. In `sendMessageStream`, after processing each SSE line, if the data contains step info (look for JSON with `agentRole` or `role` fields), parse and add to steps. However, the current `/api/chat/stream` endpoint (not cooperative) does NOT send step events. Skip this for regular stream; cooperative stream uses a different endpoint (`/api/chat/cooperate/stream`) which is handled elsewhere in the code.

For now, just ensure the agent steps rendering functions are available in global scope. The cooperative stream handling (if present) can call `updateBubbleAgentSteps(bubbleId, steps)` when step-complete events arrive.

---

## 9. Wire loadSidebarSessions on DOMContentLoaded (optional, auto-expand)

**File**: `wwwroot/index.html`

Find the `window.addEventListener('DOMContentLoaded', () => {` block (around line 2240). Find the line that calls `initTheme(); updateUI(); ...`. After all existing init calls in that block, add:

```javascript
            // Auto-load sidebar sessions list (pre-open)
            // Uncomment to auto-expand session list on load:
            // toggleSidebarSessions();
```

(Keep this commented for now so it doesn't auto-expand — user can click to expand.)

---

## 10. Build and test

```bash
cd /home/ubuntu/ws/ai-chat-app
dotnet build AiChatApp
```

Fix any compilation errors. The JS changes are in HTML files so no build step needed for them.

## Priority order

1. Backend: Add `/api/chat/sessions` JSON endpoint in `ChatEndpoints.cs` ← compile check
2. Footer: Add lmstudio option + Plan Mode toggle
3. Sidebar: Add session list section
4. index.html CSS: Add all agent steps + plan bar + provider badge styles
5. index.html HTML: Add plan approval bar + provider badge in chat header
6. index.html JS: Add all 4 function groups (sidebar sessions, provider badge, agent steps, plan mode)
7. Wire up existing functions (changeSessionProvider, sendMessageStream, handleFormSubmit)

## Constraints

- Do NOT break existing functions: `handleFormSubmit`, `addUserMessage`, `renderAiBubbleId`, `scrollToBottom`, `renderMarkdown`, `openSlide`, `loadPanelData`
- Do NOT remove any existing IDs, classes, or data-i18n attributes
- The sidebar and footer HTML files are loaded via HTMX — all JS functions they call must be defined in index.html
- `dotnet build` must pass with 0 errors
