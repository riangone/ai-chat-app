# UI改善実装仕様書

## 概要

このドキュメントはマルチエージェント関連UIの改善を実装するための詳細仕様書です。
コードを変更する前に全体を通読し、変更の依存関係を把握してください。

**対象ファイル:**
- `AiChatApp/wwwroot/index.html`
- `AiChatApp/Services/ProjectApiController.cs`
- `AiChatApp/Services/ProjectService.cs`
- `AiChatApp/Models/AgentProfile.cs`（変更なし、参照のみ）
- `AiChatApp/Services/AiService.cs`

**変更の優先順（依存関係順）:**
1. [Fix-1] SSEデータのJSON化（バグ修正）
2. [Feature-1] エージェント削除API
3. [Feature-2] パイプライン進捗UI
4. [Feature-3] Cooperativeバブルのフッター統一
5. [Feature-4] モード選択UI刷新
6. [Feature-5] プロジェクト選択の永続化＋コンテキストバナー
7. [Feature-6] エージェントモーダルUX改善

---

## [Fix-1] SSEの`step-complete`イベントをJSONに変更

### 問題

`AiService.cs` の `CooperateAsync` メソッドで、`step-complete` イベントのデータを
`{role}|{stepHtml}` というパイプ区切り文字列で送信している。
`BuildStepHtml` が生成するHTML内に `|` が含まれうるため、クライアント側のパースが壊れる。

### 変更1: `AiChatApp/Services/AiService.cs`

**変更箇所:** `CooperateAsync` メソッド内の `onStepComplete` コールバック呼び出し側ではなく、
呼び出し元の `Program.cs` にある `SendEvent` の `step-complete` 送信部分。

実際には `onStepComplete` コールバックの引数として渡される `stepHtml` をそのまま送信しているのは
`Program.cs` の `/api/chat/cooperate/stream` エンドポイント内。

**変更箇所:** `AiChatApp/Program.cs` の行302付近

```csharp
// 変更前
var (html, _) = await ai.CooperateAsync(content, userId, aMsg.Id, session.Id, provider,
    onStepComplete: async (role, stepHtml) =>
    {
        await SendEvent("step-complete", $"{role}|{stepHtml}");
    });
```

```csharp
// 変更後
var (html, _) = await ai.CooperateAsync(content, userId, aMsg.Id, session.Id, provider,
    onStepComplete: async (role, stepHtml) =>
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new { role, html = stepHtml });
        await SendEvent("step-complete", payload);
    });
```

**理由:** JSON文字列はシリアライザが `"` や改行を自動エスケープするため、
区切り文字の衝突が発生しない。

### 変更2: `AiChatApp/wwwroot/index.html`

**変更箇所:** `handleSSEEvent` 関数内の `step-complete` 処理（行533〜540付近）

```javascript
// 変更前
} else if (event === 'step-complete') {
    const [role, stepHtml] = data.split('|', 2);
    if (progressEl) {
        const innerBubble = progressEl.querySelector('.chat-bubble');
        const cleanStepHtml = stepHtml.replace(/\\n/g, '\n');
        innerBubble.insertAdjacentHTML('beforeend', `<div class="step-result mt-2">${cleanStepHtml}</div>`);
        innerBubble.querySelector('.step-status').textContent = `Step complete: ${role}`;
        scrollToBottom();
    }
}
```

```javascript
// 変更後
} else if (event === 'step-complete') {
    const parsed = JSON.parse(data);
    const role = parsed.role;
    const stepHtml = parsed.html;
    if (progressEl) {
        updatePipelineStep(progressEl, role, stepHtml);
        scrollToBottom();
    }
}
```

`updatePipelineStep` は [Feature-2] で定義する新関数。

---

## [Feature-1] エージェント削除API

### 変更1: `AiChatApp/Services/ProjectService.cs`

`DeleteAgentAsync` メソッドを追加する。既存の `DeleteProjectAsync` の直後に追記。

```csharp
// 追加するメソッド
public async Task DeleteAgentAsync(int agentId, int userId)
{
    var agent = await _db.AgentProfiles
        .Include(a => a.Project)
        .FirstOrDefaultAsync(a => a.Id == agentId && a.Project!.UserId == userId);
    if (agent != null)
    {
        _db.AgentProfiles.Remove(agent);
        await _db.SaveChangesAsync();
    }
}
```

### 変更2: `AiChatApp/Services/ProjectApiController.cs`

`MapProjectEndpoints` メソッド内に、既存の `group.MapPost("/{id}/agents", ...)` の直後に
エージェント削除エンドポイントを追加する。

```csharp
// 追加するエンドポイント
group.MapDelete("/{projectId}/agents/{agentId}", async (
    int projectId, int agentId, ProjectService projectService, ClaimsPrincipal user) =>
{
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var project = await projectService.GetProjectAsync(projectId, userId);
    if (project == null) return Results.NotFound();
    await projectService.DeleteAgentAsync(agentId, userId);
    return Results.Ok();
});
```

---

## [Feature-2] パイプライン進捗UI

### 概念

Cooperativeモードで処理中、チャットバブル内にパイプラインの各ステップを
可視化するカードを表示する。各カードは以下の3状態を持つ:

- `pending`: 未実行（グレー、番号表示）
- `active`: 実行中（プライマリカラー、スピナー）
- `done`: 完了（サクセスカラー、チェックマーク＋経過時間）

デフォルト（プロジェクトなし）は `Orchestrator`, `Executor`, `Reviewer` の3ステップ。
プロジェクトにカスタムエージェントがある場合は、そのエージェント名のリストを
`session` SSEイベントで受け取ってステップを動的生成する。

### 変更1: `AiChatApp/Program.cs`

`/api/chat/cooperate/stream` エンドポイントの `SendEvent("session", ...)` 送信部分を変更。
セッションIDだけでなく、エージェントリストも送信する。

```csharp
// 変更前（行299付近）
await SendEvent("session", session.Id.ToString());
```

```csharp
// 変更後
// セッションと同時にエージェントリストをJSONで送る
List<string> agentRoles;
if (chatSessionId.HasValue)
{
    var sessionWithProject = await db.ChatSessions
        .Include(s => s.Project)
            .ThenInclude(p => p!.Agents)
        .FirstOrDefaultAsync(s => s.Id == session.Id);
    
    var customAgents = sessionWithProject?.Project?.Agents?
        .Where(a => a.IsActive)
        .OrderBy(a => a.Id)
        .Select(a => a.RoleName)
        .ToList();
    
    agentRoles = (customAgents != null && customAgents.Any())
        ? customAgents
        : new List<string> { "Orchestrator", "Executor", "Reviewer" };
}
else
{
    agentRoles = new List<string> { "Orchestrator", "Executor", "Reviewer" };
}

var sessionPayload = System.Text.Json.JsonSerializer.Serialize(new
{
    sessionId = session.Id,
    agents = agentRoles
});
await SendEvent("session", sessionPayload);
```

**注意:** この変更後、クライアント側の `session` イベント処理も変更が必要（後述）。

### 変更2: `AiChatApp/wwwroot/index.html` — CSS追加

`<style>` ブロック内（`.step-result pre` の直後）に以下を追加する。

```css
/* Pipeline progress styles */
.pipeline-step {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 6px 10px;
    border-radius: 8px;
    font-size: 12px;
    transition: all 0.3s ease;
    margin-bottom: 4px;
}
.pipeline-step.pending {
    background: rgba(0,0,0,0.05);
    opacity: 0.5;
}
.pipeline-step.active {
    background: hsl(var(--p) / 0.15);
    border: 1px solid hsl(var(--p) / 0.3);
}
.pipeline-step.done {
    background: hsl(var(--su) / 0.1);
    border: 1px solid hsl(var(--su) / 0.2);
}
.pipeline-step .step-icon {
    width: 18px;
    height: 18px;
    border-radius: 50%;
    display: flex;
    align-items: center;
    justify-content: center;
    font-size: 10px;
    font-weight: bold;
    flex-shrink: 0;
}
.pipeline-step.pending .step-icon { background: rgba(0,0,0,0.1); }
.pipeline-step.active .step-icon { background: hsl(var(--p)); color: hsl(var(--pc)); }
.pipeline-step.done .step-icon { background: hsl(var(--su)); color: hsl(var(--suc, white)); }
.step-detail-toggle {
    cursor: pointer;
    margin-left: auto;
    opacity: 0.5;
    font-size: 10px;
}
.step-detail-toggle:hover { opacity: 1; }
```

### 変更3: `AiChatApp/wwwroot/index.html` — `renderProgressBubble` 関数の置き換え

**変更前（行589〜598付近）:**
```javascript
function renderProgressBubble(id) {
    return `<div id="${id}" class="chat chat-start group message-bubble-container">
        <div class="chat-bubble shadow-sm bg-base-200 text-base-content border border-base-300 leading-relaxed p-3 md:p-4 rounded-[18px] rounded-bl-none">
            <div class="flex items-center gap-2 mb-2">
                <span class="loading loading-dots loading-xs"></span>
                <span class="step-status text-xs opacity-60">Harness Agents processing...</span>
            </div>
        </div>
    </div>`;
}
```

**変更後:**
```javascript
function renderProgressBubble(id, agentRoles) {
    const steps = (agentRoles || ['Orchestrator', 'Executor', 'Reviewer']);
    const stepsHtml = steps.map((role, i) => `
        <div class="pipeline-step pending" id="${id}-step-${role.replace(/\s+/g, '-')}">
            <div class="step-icon">${i + 1}</div>
            <span class="font-medium">${role}</span>
            <span class="step-duration text-[10px] opacity-0 ml-1"></span>
            <span class="loading loading-spinner loading-xs ml-auto hidden step-spinner"></span>
        </div>
    `).join('');

    return `<div id="${id}" class="chat chat-start group message-bubble-container">
        <div class="chat-bubble shadow-sm bg-base-200 text-base-content border border-base-300 leading-relaxed p-3 md:p-4 rounded-[18px] rounded-bl-none">
            <div class="text-[10px] font-bold opacity-40 uppercase tracking-widest mb-3">
                <span class="loading loading-dots loading-xs mr-1"></span>
                Multi-Agent Processing
            </div>
            <div class="pipeline-steps space-y-1" id="${id}-pipeline">
                ${stepsHtml}
            </div>
            <div id="${id}-step-details" class="mt-3 space-y-2"></div>
        </div>
    </div>`;
}
```

### 変更4: `AiChatApp/wwwroot/index.html` — `updatePipelineStep` 新関数の追加

`scrollToBottom` 関数の直前に追加する。

```javascript
function updatePipelineStep(progressEl, role, stepHtml) {
    const progressId = progressEl.id;
    const stepId = `${progressId}-step-${role.replace(/\s+/g, '-')}`;
    const stepEl = document.getElementById(stepId);
    
    if (stepEl) {
        // 現在activeのステップをdoneにする
        progressEl.querySelectorAll('.pipeline-step.active').forEach(el => {
            el.classList.remove('active');
            el.classList.add('done');
            el.querySelector('.step-spinner')?.classList.add('hidden');
            const icon = el.querySelector('.step-icon');
            if (icon) icon.textContent = '✓';
        });
        
        // このステップをdoneにする
        stepEl.classList.remove('pending', 'active');
        stepEl.classList.add('done');
        const icon = stepEl.querySelector('.step-icon');
        if (icon) icon.textContent = '✓';
        stepEl.querySelector('.step-spinner')?.classList.add('hidden');
        
        // 次のペンディングステップをactiveにする
        const allSteps = progressEl.querySelectorAll('.pipeline-step');
        let foundDone = false;
        allSteps.forEach(el => {
            if (el === stepEl) { foundDone = true; return; }
            if (foundDone && el.classList.contains('pending')) {
                el.classList.remove('pending');
                el.classList.add('active');
                el.querySelector('.step-spinner')?.classList.remove('hidden');
                foundDone = false; // 一つだけ
            }
        });
    }
    
    // ステップ詳細を折りたたみで追加
    const detailsContainer = document.getElementById(`${progressId}-step-details`);
    if (detailsContainer) {
        const cleanHtml = stepHtml.replace(/\\n/g, '\n');
        detailsContainer.insertAdjacentHTML('beforeend', cleanHtml);
    }
}

// パイプラインの最初のステップをactiveにする
function activateFirstPipelineStep(progressEl) {
    const firstPending = progressEl.querySelector('.pipeline-step.pending');
    if (firstPending) {
        firstPending.classList.remove('pending');
        firstPending.classList.add('active');
        firstPending.querySelector('.step-spinner')?.classList.remove('hidden');
    }
}
```

### 変更5: `AiChatApp/wwwroot/index.html` — `handleSSEEvent` の `session` 処理変更

```javascript
// 変更前（行531〜532付近）
if (event === 'session') {
    document.getElementById('session-id-input').value = data;
}
```

```javascript
// 変更後
if (event === 'session') {
    const parsed = JSON.parse(data);
    document.getElementById('session-id-input').value = parsed.sessionId;
    // agentRolesをwindow変数に保存（renderProgressBubbleで参照）
    window._currentAgentRoles = parsed.agents;
    // 既に表示されているprogressバブルのパイプラインを動的に更新
    // （session イベントはバブル生成後に来るため、バブル再生成が必要）
    // → sendCooperativeMessage 側でセッション確立後に対応（後述）
}
```

### 変更6: `AiChatApp/wwwroot/index.html` — `sendCooperativeMessage` の修正

`sendCooperativeMessage` 関数内で、progressバブルを一時プレースホルダーとして生成し、
`session` イベント受信後に正式なパイプラインUIを描画するよう変更する。

```javascript
// 変更前（行486〜488付近）
const progressId = 'progress-' + Date.now();
chatBox.insertAdjacentHTML('beforeend', renderProgressBubble(progressId));
scrollToBottom();
```

```javascript
// 変更後
const progressId = 'progress-' + Date.now();
window._pendingProgressId = progressId;
window._currentAgentRoles = null;
// まず仮のローディングバブルを表示
chatBox.insertAdjacentHTML('beforeend', renderLoadingBubble(progressId));
scrollToBottom();
```

`handleSSEEvent` の `session` 処理に、パイプラインバブルへの置き換えロジックを追加:

```javascript
if (event === 'session') {
    const parsed = JSON.parse(data);
    document.getElementById('session-id-input').value = parsed.sessionId;
    window._currentAgentRoles = parsed.agents;
    
    // 仮のローディングバブルを正式なパイプラインバブルに置き換える
    const pendingId = window._pendingProgressId;
    if (pendingId) {
        const loadingEl = document.getElementById(pendingId);
        if (loadingEl) {
            loadingEl.outerHTML = renderProgressBubble(pendingId, parsed.agents);
            // 最初のステップをactiveにする
            const newProgressEl = document.getElementById(pendingId);
            if (newProgressEl) activateFirstPipelineStep(newProgressEl);
        }
    }
    scrollToBottom();
}
```

また `progressEl` の参照を毎回 `document.getElementById(progressId)` で取り直すよう変更する
（outerHTML置き換え後は古い参照が無効になるため）:

```javascript
// handleSSEEventのstep-complete処理でprogressElを毎回取得
} else if (event === 'step-complete') {
    const parsed = JSON.parse(data);
    const freshProgressEl = document.getElementById(progressId); // 毎回IDで取得
    if (freshProgressEl) {
        updatePipelineStep(freshProgressEl, parsed.role, parsed.html);
        scrollToBottom();
    }
}
```

`handleSSEEvent` のシグネチャを `progressId` を受け取るように変更する。
既存の `progressEl` 引数を `progressId` に変更し、関数内で毎回 `getElementById` する。

```javascript
// 変更前のシグネチャ
function handleSSEEvent(event, data, progressEl, progressId, chatBox) {

// 変更後のシグネチャ
function handleSSEEvent(event, data, progressId, chatBox) {
    const progressEl = document.getElementById(progressId); // 毎回取得
```

`sendCooperativeMessage` 内の `handleSSEEvent` 呼び出しも変更:
```javascript
// 変更前
handleSSEEvent(eventName, data, progressEl, progressId, chatBox);

// 変更後
handleSSEEvent(eventName, data, progressId, chatBox);
```

また、`sendCooperativeMessage` 内の `const progressEl = ...` の行は削除する。

### `renderLoadingBubble` 関数の追加（`renderProgressBubble` の直前に追加）

```javascript
function renderLoadingBubble(id) {
    return `<div id="${id}" class="chat chat-start group message-bubble-container">
        <div class="chat-bubble shadow-sm bg-base-200 text-base-content border border-base-300 leading-relaxed p-3 md:p-4 rounded-[18px] rounded-bl-none">
            <div class="flex items-center gap-2">
                <span class="loading loading-dots loading-xs"></span>
                <span class="text-xs opacity-60">Connecting agents...</span>
            </div>
        </div>
    </div>`;
}
```

---

## [Feature-3] Cooperativeバブルのフッター統一

### 問題

`final` SSEイベント受信後、`renderAiBubble(finalHtml)` でバブルを置き換えているが、
この関数にはコピー・転送・記憶保存のフッターボタンが含まれていない。

### 変更: `AiChatApp/wwwroot/index.html` — `renderAiBubble` 関数の置き換え

**変更前（行581〜587付近）:**
```javascript
function renderAiBubble(content) {
    return `<div class="chat chat-start group message-bubble-container">
        <div class="chat-bubble shadow-sm bg-base-200 text-base-content border border-base-300 markdown leading-relaxed p-3 md:p-4 rounded-[18px] rounded-bl-none">
            ${content}
        </div>
    </div>`;
}
```

**変更後:**
```javascript
function renderAiBubble(content) {
    return `<div class="chat chat-start group message-bubble-container">
        <div class="chat-bubble shadow-sm bg-base-200 text-base-content border border-base-300 markdown leading-relaxed p-3 md:p-4 rounded-[18px] rounded-bl-none">
            ${content}
        </div>
        <div class="chat-footer opacity-0 group-hover:opacity-50 transition-opacity flex gap-3 pt-2 px-1">
            <button class="hover:text-primary transition-colors" onclick="copyText(this)" title="Copy">
                <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-3.5 h-3.5"><path stroke-linecap="round" stroke-linejoin="round" d="M15.75 17.25v3.375c0 .621-.504 1.125-1.125 1.125h-9.75a1.125 1.125 0 0 1-1.125-1.125V7.875c0-.621.504-1.125 1.125-1.125H6.75a9.06 9.06 0 0 1 1.5 1.5h6.375a1.125 1.125 0 0 1 1.125 1.125v9.375Zm3 3V6.75a1.125 1.125 0 0 0-1.125-1.125h-1.5a3.375 3.375 0 0 1-3.375-3.375V2.125c0-.621-.504-1.125-1.125-1.125H9.75a1.125 1.125 0 0 0-1.125 1.125V4.5a9.06 9.06 0 0 1 1.5 1.5h6.75a1.125 1.125 0 0 1 1.125 1.125v13.125a1.125 1.125 0 0 1-1.125 1.125H15" /></svg>
            </button>
            <button class="hover:text-primary transition-colors" onclick="forwardText(this)" title="Forward">
                <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-3.5 h-3.5"><path stroke-linecap="round" stroke-linejoin="round" d="M9 15 3 9m0 0 6-6M3 9h12a6 6 0 0 1 0 12h-3" /></svg>
            </button>
            <button class="hover:text-primary transition-colors" onclick="saveToMemory(this)" title="Save to Memory">
                <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-3.5 h-3.5"><path stroke-linecap="round" stroke-linejoin="round" d="M12 18.75a6 6 0 0 0 6-6v-1.5m-6 7.5a6 6 0 0 1-6-6v-1.5m6 7.5v3.75m-3.75 0h7.5M12 15.75a3 3 0 0 1-3-3V4.5a3 3 0 1 1 6 0v8.25a3 3 0 0 1-3 3Z" /></svg>
            </button>
            <span class="ml-auto text-[10px] opacity-30 font-semibold">MULTI-AGENT</span>
        </div>
    </div>`;
}
```

**追加のポイント:** `MULTI-AGENT` バッジをフッター右端に表示し、通常のAI応答と区別する。

---

## [Feature-4] モード選択UIの刷新

### 変更: `AiChatApp/wwwroot/index.html` — フォームのモード切り替え部分

**変更前（行167〜172付近）:**
```html
<div class="flex items-center gap-2">
    <label class="label cursor-pointer gap-2 py-0">
        <span class="label-text text-[10px] font-bold opacity-50 uppercase tracking-wider">Cooperative Mode</span> 
        <input type="checkbox" name="mode" value="cooperative" class="toggle toggle-primary toggle-xs" />
    </label>
</div>
```

**変更後:**
```html
<div class="flex items-center gap-1 bg-base-300/50 rounded-full p-0.5">
    <button type="button" id="mode-normal-btn"
            onclick="setMode('normal')"
            class="btn btn-xs rounded-full px-3 btn-primary mode-btn-active">
        Normal
    </button>
    <button type="button" id="mode-multi-btn"
            onclick="setMode('cooperative')"
            class="btn btn-xs rounded-full px-3 btn-ghost mode-btn">
        <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-3 h-3 mr-0.5 inline">
            <path stroke-linecap="round" stroke-linejoin="round" d="M18 18.72a9.094 9.094 0 0 0 3.741-.479 3 3 0 0 0-4.682-2.72m.94 3.198.001.031c0 .225-.012.447-.037.666A11.944 11.944 0 0 1 12 21c-2.17 0-4.207-.576-5.963-1.584A6.062 6.062 0 0 1 6 18.719m12 0a5.971 5.971 0 0 0-.941-3.197m0 0A5.995 5.995 0 0 0 12 12.75a5.995 5.995 0 0 0-5.058 2.772m0 0a3 3 0 0 0-4.681 2.72 8.986 8.986 0 0 0 3.74.477m.94-3.197a5.971 5.971 0 0 0-.94 3.197M15 6.75a3 3 0 1 1-6 0 3 3 0 0 1 6 0Zm6 3a2.25 2.25 0 1 1-4.5 0 2.25 2.25 0 0 1 4.5 0Zm-13.5 0a2.25 2.25 0 1 1-4.5 0 2.25 2.25 0 0 1 4.5 0Z" />
        </svg>
        Multi-Agent
    </button>
    <!-- hidden input for form submission -->
    <input type="hidden" name="mode" id="mode-input" value="" />
</div>
```

**CSS追加（`<style>` ブロック内）:**
```css
.mode-btn-active {
    /* btn-primary already applied via setMode */
}
/* form multi-agent active state: subtle glow */
#chat-form.mode-multiagent {
    border-color: hsl(var(--s) / 0.4);
    box-shadow: 0 0 0 1px hsl(var(--s) / 0.15);
}
```

**JavaScript追加（`<script>` ブロック内、`handleFormSubmit` の前に追加）:**
```javascript
function setMode(mode) {
    const modeInput = document.getElementById('mode-input');
    const normalBtn = document.getElementById('mode-normal-btn');
    const multiBtn = document.getElementById('mode-multi-btn');
    const form = document.getElementById('chat-form');

    if (mode === 'cooperative') {
        modeInput.value = 'cooperative';
        normalBtn.classList.remove('btn-primary');
        normalBtn.classList.add('btn-ghost');
        multiBtn.classList.remove('btn-ghost');
        multiBtn.classList.add('btn-secondary');
        form.classList.add('mode-multiagent');
    } else {
        modeInput.value = '';
        normalBtn.classList.remove('btn-ghost');
        normalBtn.classList.add('btn-primary');
        multiBtn.classList.remove('btn-secondary');
        multiBtn.classList.add('btn-ghost');
        form.classList.remove('mode-multiagent');
    }
}
```

**`handleFormSubmit` の変更:**
```javascript
// 変更前
const isCooperative = formData.get('mode') === 'cooperative';

// 変更後（変更なし、hidden inputが'cooperative'か空を送るので互換性あり）
const isCooperative = formData.get('mode') === 'cooperative';
```

---

## [Feature-5] プロジェクト選択の永続化＋コンテキストバナー

### 変更1: `AiChatApp/wwwroot/index.html` — `selectProject` 関数の修正

```javascript
// 変更前
function selectProject(id, name, path) {
    selectedProjectId = id;
    document.getElementById('current-project-name').textContent = name;
    document.getElementById('current-project-path').textContent = path;
    document.getElementById('selected-project-info').classList.remove('hidden');
    // ...
}
```

```javascript
// 変更後
function selectProject(id, name, path) {
    selectedProjectId = id;
    document.getElementById('current-project-name').textContent = name;
    document.getElementById('current-project-path').textContent = path;
    document.getElementById('selected-project-info').classList.remove('hidden');
    
    // localStorage に永続化
    localStorage.setItem('selectedProject', JSON.stringify({ id, name, path }));
    
    // 入力エリアのコンテキストバナーを更新
    updateProjectBanner(name);
    
    // プロジェクトリストのハイライト
    document.querySelectorAll('#projects-list button').forEach(btn => {
        btn.classList.remove('btn-active', 'bg-primary/20');
        if (btn.textContent.trim().includes(name)) btn.classList.add('bg-primary/20');
    });

    loadAgents(id);
}
```

```javascript
// deselectProject も修正
function deselectProject() {
    selectedProjectId = null;
    document.getElementById('selected-project-info').classList.add('hidden');
    document.querySelectorAll('#projects-list button').forEach(btn => btn.classList.remove('bg-primary/20'));
    localStorage.removeItem('selectedProject');
    updateProjectBanner(null);
}
```

**`updateProjectBanner` 関数の追加（`deselectProject` の直後に追加）:**
```javascript
function updateProjectBanner(projectName) {
    const banner = document.getElementById('project-context-banner');
    if (!banner) return;
    if (projectName) {
        banner.classList.remove('hidden');
        document.getElementById('project-banner-name').textContent = projectName;
    } else {
        banner.classList.add('hidden');
    }
}
```

**ページロード時の復元（`fetchUserInfo()` の直後に追加）:**
```javascript
// プロジェクト選択を復元
(function restoreProjectSelection() {
    const saved = localStorage.getItem('selectedProject');
    if (saved) {
        try {
            const { id, name, path } = JSON.parse(saved);
            selectedProjectId = id;
            document.getElementById('current-project-name').textContent = name;
            document.getElementById('current-project-path').textContent = path;
            document.getElementById('selected-project-info').classList.remove('hidden');
            updateProjectBanner(name);
        } catch (e) {
            localStorage.removeItem('selectedProject');
        }
    }
})();
```

### 変更2: `AiChatApp/wwwroot/index.html` — コンテキストバナーのHTML追加

**フォームの `<input type="hidden" name="sessionId" ...>` の直前に挿入:**
```html
<!-- Project Context Banner -->
<div id="project-context-banner" class="hidden flex items-center justify-between px-3 py-1.5 bg-secondary/10 border-b border-secondary/20 rounded-t-[20px] -mb-1 mx-0.5">
    <div class="flex items-center gap-1.5">
        <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor" class="w-3 h-3 text-secondary opacity-70">
            <path stroke-linecap="round" stroke-linejoin="round" d="M2.25 12.75V12A2.25 2.25 0 0 1 4.5 9.75h15A2.25 2.25 0 0 1 21.75 12v.75m-19.5 0A2.25 2.25 0 0 0 4.5 15h15a2.25 2.25 0 0 0 2.25-2.25m-19.5 0v.25A2.25 2.25 0 0 0 4.5 17.5h15a2.25 2.25 0 0 0 2.25-2.25v-.25" />
        </svg>
        <span class="text-[11px] font-semibold text-secondary opacity-80" id="project-banner-name"></span>
    </div>
    <button type="button" onclick="deselectProject()" class="text-[10px] opacity-40 hover:opacity-70 transition-opacity">✕ clear</button>
</div>
```

---

## [Feature-6] エージェントモーダルUX改善

### 変更: `AiChatApp/wwwroot/index.html` — `agent_modal` の内容を刷新

**変更前の `<dialog id="agent_modal">` 全体を以下に置き換え:**

```html
<dialog id="agent_modal" class="modal">
    <div class="modal-box w-11/12 max-w-2xl">
        <h3 class="font-bold text-lg">Project Agent Roles</h3>
        <p class="text-sm opacity-60 mt-1">
            Agents execute in the order listed. In Multi-Agent mode, these replace the default Orchestrator → Executor → Reviewer pipeline.
        </p>

        <!-- パイプライン可視化 -->
        <div id="agent-pipeline-vis" class="flex items-center gap-1 mt-3 mb-2 overflow-x-auto pb-1 min-h-[28px]">
            <!-- JS で動的生成 -->
        </div>
        
        <!-- エージェントリスト -->
        <div id="agent-list" class="mt-2 space-y-2 max-h-52 overflow-y-auto"></div>

        <!-- 新規追加フォーム -->
        <div class="mt-4 p-4 bg-base-200 rounded-lg space-y-3">
            <div class="text-[10px] font-bold opacity-40 uppercase tracking-widest">Add New Role</div>
            <div class="grid grid-cols-2 gap-3">
                <div class="form-control">
                    <label class="label pb-1"><span class="label-text text-xs font-semibold">Role Name</span></label>
                    <input type="text" id="new-agent-role" placeholder="e.g. Backend Dev" class="input input-sm input-bordered w-full" />
                </div>
                <div class="form-control">
                    <label class="label pb-1"><span class="label-text text-xs font-semibold">AI Provider</span></label>
                    <select id="new-agent-provider" class="select select-sm select-bordered w-full">
                        <option value="">Global Default</option>
                        <option value="gemini">Gemini 2.0</option>
                        <option value="claude">Claude Code</option>
                        <option value="gh-copilot">GitHub Copilot</option>
                        <option value="codex">OpenAI Codex</option>
                    </select>
                </div>
            </div>
            <div class="form-control">
                <label class="label pb-1"><span class="label-text text-xs font-semibold">System Prompt</span></label>
                <textarea id="new-agent-prompt" placeholder="You are an expert in..." class="textarea textarea-sm textarea-bordered w-full h-28"></textarea>
            </div>
            <div class="grid grid-cols-4 gap-2">
                <div class="form-control col-span-1">
                    <label class="label pb-1"><span class="label-text text-xs font-semibold">Color</span></label>
                    <select id="new-agent-color" class="select select-sm select-bordered w-full">
                        <option value="primary">Blue</option>
                        <option value="secondary">Pink</option>
                        <option value="accent">Teal</option>
                        <option value="info">Cyan</option>
                        <option value="success">Green</option>
                        <option value="warning">Yellow</option>
                        <option value="error">Red</option>
                    </select>
                </div>
                <div class="col-span-3 flex items-end">
                    <button type="button" onclick="addNewAgent()" class="btn btn-sm btn-primary w-full">
                        + Add Role
                    </button>
                </div>
            </div>
        </div>

        <div class="modal-action">
            <button type="button" class="btn" onclick="agent_modal.close()">Done</button>
        </div>
    </div>
</dialog>
```

### 変更: `AiChatApp/wwwroot/index.html` — `loadAgents` 関数の置き換え

```javascript
// 変更前
async function loadAgents(projectId) {
    const r = await fetch(`/api/projects`);
    const projects = await r.json();
    const project = projects.find(p => p.id === projectId);
    
    if (project) {
        const list = document.getElementById('agent-list');
        list.innerHTML = project.agents.map(a => `
            <div class="flex items-center justify-between p-2 bg-base-300 rounded-lg">
                ...
            </div>
        `).join('');
    }
}
```

```javascript
// 変更後
async function loadAgents(projectId) {
    const r = await fetch(`/api/projects`);
    const projects = await r.json();
    const project = projects.find(p => p.id === projectId);
    
    if (!project) return;
    
    const list = document.getElementById('agent-list');
    if (!list) return;
    
    list.innerHTML = project.agents.length === 0
        ? `<p class="text-xs opacity-40 text-center py-4">No agents yet. Add roles below to customize the pipeline.</p>`
        : project.agents.map((a, i) => `
            <div class="flex items-start gap-2 p-3 bg-base-300 rounded-lg group" id="agent-card-${a.id}">
                <div class="flex-shrink-0 w-6 h-6 rounded-full bg-${a.color || 'ghost'}/20 border border-${a.color || 'ghost'}/30 flex items-center justify-center text-xs font-bold opacity-70">
                    ${i + 1}
                </div>
                <div class="flex-1 min-w-0">
                    <div class="flex items-center gap-2">
                        <span class="badge badge-${a.color || 'ghost'} badge-sm font-semibold">${a.roleName}</span>
                        <span class="text-[9px] font-mono opacity-40 uppercase">${a.preferredProvider || 'Global'}</span>
                    </div>
                    <p class="text-[11px] opacity-60 mt-1 leading-snug line-clamp-2">${a.systemPrompt}</p>
                </div>
                <button onclick="deleteAgent(${projectId}, ${a.id})"
                        class="btn btn-ghost btn-xs text-error opacity-0 group-hover:opacity-70 transition-opacity flex-shrink-0"
                        title="Delete agent">
                    <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-3.5 h-3.5">
                        <path stroke-linecap="round" stroke-linejoin="round" d="m14.74 9-.346 9m-4.788 0L9.26 9m9.968-3.21c.342.052.682.107 1.022.166m-1.022-.165L18.16 19.673a2.25 2.25 0 0 1-2.244 2.077H8.084a2.25 2.25 0 0 1-2.244-2.077L4.772 5.79m14.456 0a48.108 48.108 0 0 0-3.478-.397m-12 .562c.34-.059.68-.114 1.022-.165m0 0a48.11 48.11 0 0 1 3.478-.397m7.5 0v-.916c0-1.18-.91-2.164-2.09-2.201a51.964 51.964 0 0 0-3.32 0c-1.18.037-2.09 1.022-2.09 2.201v.916m7.5 0a48.667 48.667 0 0 0-7.5 0" />
                    </svg>
                </button>
            </div>
        `).join('');
    
    // パイプライン可視化を更新
    updatePipelineVisualization(project.agents);
}

function updatePipelineVisualization(agents) {
    const vis = document.getElementById('agent-pipeline-vis');
    if (!vis) return;
    
    if (agents.length === 0) {
        vis.innerHTML = `<span class="text-[10px] opacity-30">Default: Orchestrator → Executor → Reviewer</span>`;
        return;
    }
    
    vis.innerHTML = agents.map((a, i) => `
        <div class="flex items-center gap-1 flex-shrink-0">
            <span class="badge badge-${a.color || 'ghost'} badge-xs whitespace-nowrap">${a.roleName}</span>
            ${i < agents.length - 1 ? '<span class="opacity-30 text-xs">→</span>' : ''}
        </div>
    `).join('');
}

async function deleteAgent(projectId, agentId) {
    const r = await fetch(`/api/projects/${projectId}/agents/${agentId}`, { method: 'DELETE' });
    if (r.ok) {
        document.getElementById(`agent-card-${agentId}`)?.remove();
        // パイプライン可視化も再読み込み
        await loadAgents(projectId);
    }
}
```

### 変更: `addNewAgent` 関数の修正

```javascript
// 変更前（行730〜757付近）
async function addNewAgent() {
    if (!selectedProjectId) return;
    // ...
    if (r.ok) {
        document.getElementById('new-agent-role').value = '';
        document.getElementById('new-agent-prompt').value = '';
        document.getElementById('new-agent-provider').value = '';
        loadAgents(selectedProjectId);
    }
}
```

```javascript
// 変更後: 追加後に全エージェントリストを再読み込みし、フォームをクリア
async function addNewAgent() {
    if (!selectedProjectId) return;
    
    const roleName = document.getElementById('new-agent-role').value.trim();
    const systemPrompt = document.getElementById('new-agent-prompt').value.trim();
    const color = document.getElementById('new-agent-color').value;
    const preferredProvider = document.getElementById('new-agent-provider').value;
    
    if (!roleName || !systemPrompt) {
        // 未入力フィールドをハイライト
        if (!roleName) document.getElementById('new-agent-role').classList.add('input-error');
        if (!systemPrompt) document.getElementById('new-agent-prompt').classList.add('textarea-error');
        return;
    }
    document.getElementById('new-agent-role').classList.remove('input-error');
    document.getElementById('new-agent-prompt').classList.remove('textarea-error');
    
    const formData = new FormData();
    formData.append('roleName', roleName);
    formData.append('systemPrompt', systemPrompt);
    formData.append('color', color);
    formData.append('preferredProvider', preferredProvider);
    
    const r = await fetch(`/api/projects/${selectedProjectId}/agents`, {
        method: 'POST',
        body: formData
    });
    
    if (r.ok) {
        document.getElementById('new-agent-role').value = '';
        document.getElementById('new-agent-prompt').value = '';
        document.getElementById('new-agent-provider').value = '';
        await loadAgents(selectedProjectId);
    }
}
```

---

## 変更の依存関係まとめ

```
[Fix-1] SSE JSON化
    ├── Program.cs: CooperateAsync の onStepComplete コールバック側の SendEvent 変更
    ├── Program.cs: session イベントのペイロードをJSON化
    └── index.html: handleSSEEvent の session / step-complete 処理変更

[Feature-1] エージェント削除API
    ├── ProjectService.cs: DeleteAgentAsync 追加
    └── ProjectApiController.cs: DELETE /{projectId}/agents/{agentId} 追加

[Feature-2] パイプライン進捗UI ← Fix-1 が完了していること
    ├── index.html: CSS追加
    ├── index.html: renderProgressBubble 置き換え（引数追加）
    ├── index.html: renderLoadingBubble 追加
    ├── index.html: updatePipelineStep 追加
    ├── index.html: activateFirstPipelineStep 追加
    ├── index.html: sendCooperativeMessage 修正
    └── index.html: handleSSEEvent シグネチャ変更 + session/step-complete処理変更

[Feature-3] バブルフッター統一 ← 独立
    └── index.html: renderAiBubble 置き換え

[Feature-4] モード選択UI刷新 ← 独立
    ├── index.html: フォームのモード切り替えHTML置き換え
    └── index.html: setMode 関数追加

[Feature-5] プロジェクト永続化 ← 独立
    ├── index.html: selectProject / deselectProject 修正
    ├── index.html: updateProjectBanner 追加
    ├── index.html: restoreProjectSelection 追加
    └── index.html: コンテキストバナーHTML追加

[Feature-6] エージェントモーダル改善 ← Feature-1 が完了していること
    ├── index.html: agent_modal HTML置き換え
    ├── index.html: loadAgents 置き換え
    ├── index.html: updatePipelineVisualization 追加
    ├── index.html: deleteAgent 追加
    └── index.html: addNewAgent 修正
```

---

## 実装時の注意事項

1. **`session` イベントの旧フォーマット互換性:** `session` イベントのペイロードを `sessionId` の文字列からJSONオブジェクトに変更するため、クライアントとサーバーを同時に変更すること。

2. **`handleSSEEvent` の引数変更:** `progressEl` → `progressId` に変更するため、呼び出し元 `sendCooperativeMessage` 内の呼び出しも同時に変更すること。引数ズレによる無言のバグが発生しやすい。

3. **`renderProgressBubble` の引数追加:** 第2引数 `agentRoles` を追加するが、`renderLoadingBubble` でバブルを仮生成した後に `session` イベントで `renderProgressBubble` を呼ぶ設計のため、直接 `renderProgressBubble` を呼ぶ箇所が `handleSSEEvent` のみになる。既存の `sendCooperativeMessage` 内の呼び出しは `renderLoadingBubble` に置き換えること。

4. **DaisyUI のカラーシステム:** `badge-${color}` / `bg-${color}` のような動的クラスはTailwindのJITでパージされる可能性がある。ただし本プロジェクトはCDN版を使用しているため問題なし。

5. **DBスキーマ変更なし:** 今回の変更はAPIエンドポイントの追加とUI変更のみ。`chat.db` の削除・再作成は不要。
