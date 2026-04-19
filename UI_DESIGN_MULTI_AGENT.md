# Multi-Agent 開発UI 設計文書

## 現状UIの問題点

現在の `index.html` は「AIチャットアプリ」であり、Multi-Agent開発ツールとしての問題点は以下の通り：

| 問題 | 具体的な症状 |
|------|------------|
| アイデンティティが曖昧 | タイトルが "AI Chat Pro"、placeholderが "Message AI..." でチャット文脈 |
| エージェントが不可視 | 処理中は "Harness Agents processing..." の1行のみ。どのエージェントが動いているか分からない |
| プロバイダー選択がチャット前提 | 単一の`<select>`で全エージェントが同じプロバイダーを使う |
| 成果物管理がない | コード・テスト・設計文書が会話バブルに埋まって再利用不可 |
| プロジェクト概念がない | "New Chat" だけで、開発タスクとしての文脈がない |
| モード切替が貧弱 | チェックボックス1つで切り替え。モードの違いが視覚的に表現されていない |

---

## 新UIの設計方針

### 方針1: デュアルモードレイアウト
- **Chat Mode（既存）**: チャット型インターフェース。軽微な改善のみ。
- **Dev Mode（新規）**: Multi-Agent開発特化。3カラムレイアウト。

### 方針2: エージェントパイプラインの可視化
- 4エージェント（Orchestrator→Executor→Tester→Reviewer）を常に表示
- 各エージェントのリアルタイム状態（待機/処理中/完了/エラー）を色とアニメーションで表現

### 方針3: 成果物中心の出力
- コード・テスト・レビュー結果を専用パネルに整理
- ダウンロード・コピーボタン付き
- エージェント別タブで切り替え可能

### 方針4: プロバイダー設定のUX改善
- エージェントカードに直接プロバイダーセレクターを配置
- 各プロバイダーをアイコン・色で識別

---

## レイアウト設計

### Chat Mode（既存から改善）

```
┌─────────────────────────────────────────────────────────────┐
│ [≡] AI Dev Studio        [Chat Mode] ←→ [Dev Mode]   [👤]  │  ← Navbar
├─────────────┬───────────────────────────────────────────────┤
│   Sidebar   │                                               │
│             │          Chat Area（既存）                    │
│  [History]  │                                               │
│  [Memory]   │                                               │
│  [Skills]   │                                               │
│             │                                               │
│             ├───────────────────────────────────────────────│
│             │  [ テキスト入力エリア ]        [▶ Send]       │
└─────────────┴───────────────────────────────────────────────┘
```

### Dev Mode（新規設計）— 3カラムレイアウト

```
┌─────────────────────────────────────────────────────────────────────┐
│ [≡] AI Dev Studio        [Chat Mode] ←→ [Dev Mode]   [👤]          │
├─────────────┬──────────────────────┬────────────────────────────────┤
│             │                      │                                │
│  LEFT       │   CENTER             │   RIGHT                        │
│  PANEL      │   PANEL              │   PANEL                        │
│  (240px)    │   (flex-1)           │   (360px)                      │
│             │                      │                                │
│  プロジェクト│  エージェント         │  成果物ビューア                │
│  リスト     │  パイプライン         │                                │
│             │                      │  [Orchestrator Plan]           │
│  + New Task │  [Orchestrator] →    │  [Executor Code]               │
│             │  [Executor]    →     │  [Tester Tests]                │
│  ─────────  │  [Tester]      →     │  [Reviewer Report]             │
│  My Tasks   │  [Reviewer]          │                                │
│  > Task 1   │                      │  ┌─────────────────────────┐  │
│    Task 2   │                      │  │  コード / テスト / 文書  │  │
│    Task 3   │                      │  │  [ Copy ] [ Download ]  │  │
│             │  ─────────────────── │  └─────────────────────────┘  │
│  ─────────  │  タスク入力エリア     │                                │
│  [Memory]   │  ┌─────────────────┐ │                                │
│  [Skills]   │  │  要件を入力...   │ │                                │
│             │  └─────────────────┘ │                                │
│             │  [▶ Run Agents]       │                                │
└─────────────┴──────────────────────┴────────────────────────────────┘
```

---

## コンポーネント詳細設計

### A. モード切替スイッチ（Navbar）

現在のチェックボックスをNavbarのセグメントコントロールに変更。

```html
<!-- Navbar中央に配置 -->
<div class="flex-1 flex justify-center">
    <div class="join bg-base-200 rounded-full p-1">
        <button id="btn-chat-mode"
                class="join-item btn btn-sm rounded-full btn-primary font-bold px-5"
                onclick="switchMode('chat')">
            <svg ...chat icon...></svg>
            Chat
        </button>
        <button id="btn-dev-mode"
                class="join-item btn btn-sm rounded-full btn-ghost font-bold px-5"
                onclick="switchMode('dev')">
            <svg ...code icon...></svg>
            Dev Mode
        </button>
    </div>
</div>
```

**モード切替の動作：**
- Chat Mode: 既存の `.drawer` レイアウトを表示、`#dev-layout` を `hidden`
- Dev Mode: 既存の `.drawer` を `hidden`、`#dev-layout` を表示

```javascript
function switchMode(mode) {
    const chatLayout = document.getElementById('chat-layout');
    const devLayout = document.getElementById('dev-layout');
    const btnChat = document.getElementById('btn-chat-mode');
    const btnDev = document.getElementById('btn-dev-mode');

    if (mode === 'dev') {
        chatLayout.classList.add('hidden');
        devLayout.classList.remove('hidden');
        btnChat.classList.replace('btn-primary', 'btn-ghost');
        btnDev.classList.replace('btn-ghost', 'btn-primary');
        localStorage.setItem('mode', 'dev');
    } else {
        chatLayout.classList.remove('hidden');
        devLayout.classList.add('hidden');
        btnChat.classList.replace('btn-ghost', 'btn-primary');
        btnDev.classList.replace('btn-primary', 'btn-ghost');
        localStorage.setItem('mode', 'chat');
    }
}
// 起動時に復元
switchMode(localStorage.getItem('mode') || 'chat');
```

---

### B. Dev Mode レイアウト HTML

```html
<!-- Dev Mode Layout（chat-layoutの兄弟要素として配置） -->
<div id="dev-layout" class="hidden flex h-screen overflow-hidden bg-base-200 gap-0">

    <!-- ─── Left Panel: Project List ─── -->
    <div class="w-60 bg-base-100 border-r border-base-300 flex flex-col p-3 gap-3 flex-none">
        <button id="dev-new-task-btn"
                class="btn btn-primary btn-sm w-full font-bold shadow-md">
            + New Task
        </button>

        <div class="divider text-[10px] opacity-40 uppercase tracking-widest my-0">Tasks</div>

        <div id="dev-task-list" class="flex-1 overflow-y-auto flex flex-col gap-1">
            <!-- タスクリストアイテムはJSで動的生成 -->
            <!-- 例:
            <button class="btn btn-ghost btn-sm justify-start text-left font-normal gap-2 w-full"
                    onclick="loadTask(1)">
                <span class="badge badge-success badge-xs"></span>
                電卓クラスを作成
            </button>
            -->
        </div>

        <div class="divider text-[10px] opacity-40 uppercase tracking-widest my-0">Context</div>

        <button class="btn btn-ghost btn-sm justify-start gap-2 text-xs opacity-70"
                onclick="switchTab(null, 'memory-container')">
            <svg ...brain icon...></svg> Memory
        </button>
        <button class="btn btn-ghost btn-sm justify-start gap-2 text-xs opacity-70"
                onclick="switchTab(null, 'skills-container')">
            <svg ...puzzle icon...></svg> Skills
        </button>
    </div>

    <!-- ─── Center Panel: Agent Pipeline ─── -->
    <div class="flex-1 flex flex-col min-w-0 bg-base-50">

        <!-- Pipeline Header -->
        <div class="flex-none px-6 pt-5 pb-3 border-b border-base-300 bg-base-100">
            <h2 class="text-sm font-bold opacity-50 uppercase tracking-widest mb-3">Agent Pipeline</h2>

            <!-- エージェントカード行 -->
            <div class="flex gap-3 items-stretch">

                <!-- Orchestrator Card -->
                <div class="agent-card flex-1 rounded-xl border-2 border-info/30 bg-info/5 p-3
                            flex flex-col gap-2 transition-all"
                     data-role="Orchestrator" id="agent-card-Orchestrator">
                    <div class="flex items-center justify-between">
                        <span class="badge badge-info badge-sm font-bold">Orchestrator</span>
                        <span class="agent-status text-[10px] opacity-50">待機中</span>
                    </div>
                    <p class="text-[10px] opacity-50 leading-relaxed">タスク分解・計画立案</p>
                    <select name="roleProvider_Orchestrator"
                            class="select select-bordered select-xs w-full bg-base-100">
                        <option value="gemini" selected>🔷 Gemini</option>
                        <option value="claude">🟠 Claude</option>
                        <option value="gh-copilot">🐙 Copilot</option>
                        <option value="codex">🟢 Codex</option>
                    </select>
                </div>

                <!-- Arrow -->
                <div class="flex items-center opacity-30 text-lg font-light flex-none">→</div>

                <!-- Executor Card -->
                <div class="agent-card flex-1 rounded-xl border-2 border-success/30 bg-success/5 p-3
                            flex flex-col gap-2 transition-all"
                     data-role="Executor" id="agent-card-Executor">
                    <div class="flex items-center justify-between">
                        <span class="badge badge-success badge-sm font-bold">Executor</span>
                        <span class="agent-status text-[10px] opacity-50">待機中</span>
                    </div>
                    <p class="text-[10px] opacity-50 leading-relaxed">コード・成果物の実装</p>
                    <select name="roleProvider_Executor"
                            class="select select-bordered select-xs w-full bg-base-100">
                        <option value="gemini">🔷 Gemini</option>
                        <option value="claude" selected>🟠 Claude</option>
                        <option value="gh-copilot">🐙 Copilot</option>
                        <option value="codex">🟢 Codex</option>
                    </select>
                </div>

                <!-- Arrow -->
                <div class="flex items-center opacity-30 text-lg font-light flex-none">→</div>

                <!-- Tester Card -->
                <div class="agent-card flex-1 rounded-xl border-2 border-warning/30 bg-warning/5 p-3
                            flex flex-col gap-2 transition-all"
                     data-role="Tester" id="agent-card-Tester">
                    <div class="flex items-center justify-between">
                        <span class="badge badge-warning badge-sm font-bold">Tester</span>
                        <span class="agent-status text-[10px] opacity-50">待機中</span>
                    </div>
                    <p class="text-[10px] opacity-50 leading-relaxed">テストコード・QA</p>
                    <select name="roleProvider_Tester"
                            class="select select-bordered select-xs w-full bg-base-100">
                        <option value="gemini">🔷 Gemini</option>
                        <option value="claude">🟠 Claude</option>
                        <option value="gh-copilot" selected>🐙 Copilot</option>
                        <option value="codex">🟢 Codex</option>
                    </select>
                </div>

                <!-- Arrow -->
                <div class="flex items-center opacity-30 text-lg font-light flex-none">→</div>

                <!-- Reviewer Card -->
                <div class="agent-card flex-1 rounded-xl border-2 border-secondary/30 bg-secondary/5 p-3
                            flex flex-col gap-2 transition-all"
                     data-role="Reviewer" id="agent-card-Reviewer">
                    <div class="flex items-center justify-between">
                        <span class="badge badge-secondary badge-sm font-bold">Reviewer</span>
                        <span class="agent-status text-[10px] opacity-50">待機中</span>
                    </div>
                    <p class="text-[10px] opacity-50 leading-relaxed">品質確認・最終レポート</p>
                    <select name="roleProvider_Reviewer"
                            class="select select-bordered select-xs w-full bg-base-100">
                        <option value="gemini">🔷 Gemini</option>
                        <option value="claude">🟠 Claude</option>
                        <option value="gh-copilot">🐙 Copilot</option>
                        <option value="codex" selected>🟢 Codex</option>
                    </select>
                </div>
            </div>
        </div>

        <!-- Pipeline Log (リアルタイムログ表示) -->
        <div id="pipeline-log" class="flex-1 overflow-y-auto p-6 space-y-3">
            <!-- 初期状態 -->
            <div id="pipeline-empty" class="flex flex-col items-center justify-center h-full
                                            text-base-content/20 space-y-3">
                <svg class="w-12 h-12 opacity-30" ...code-bracket icon...></svg>
                <p class="text-sm">タスクを入力してエージェントを起動してください</p>
            </div>
            <!-- ログエントリーはJSで挿入 -->
        </div>

        <!-- Task Input -->
        <div class="flex-none p-4 border-t border-base-300 bg-base-100">
            <form id="dev-form" onsubmit="return handleDevFormSubmit(event)">
                <input type="hidden" name="sessionId" id="dev-session-id" />
                <div class="flex gap-3 items-end">
                    <textarea name="content" id="dev-input"
                              placeholder="開発タスクを入力... 例: C#でスタック実装しユニットテストを書いて"
                              class="textarea textarea-bordered flex-1 resize-none text-sm leading-relaxed
                                     min-h-[72px] max-h-[200px] focus:textarea-primary"
                              oninput="autoExpand(this)"></textarea>
                    <button type="submit" id="dev-run-btn"
                            class="btn btn-primary px-6 h-[72px] flex flex-col gap-1 font-bold shadow-lg
                                   hover:scale-105 transition-transform">
                        <svg class="w-5 h-5" ...play icon...></svg>
                        <span class="text-xs">Run</span>
                    </button>
                </div>
            </form>
        </div>
    </div>

    <!-- ─── Right Panel: Artifact Viewer ─── -->
    <div class="w-96 bg-base-100 border-l border-base-300 flex flex-col flex-none">

        <!-- Artifact Tabs -->
        <div class="flex-none px-4 pt-4 border-b border-base-300">
            <h2 class="text-xs font-bold opacity-40 uppercase tracking-widest mb-3">Artifacts</h2>
            <div role="tablist" class="tabs tabs-boxed tabs-sm bg-base-200 p-1">
                <a role="tab" class="tab tab-active tab-xs"
                   onclick="switchArtifactTab(this, 'artifact-plan')">Plan</a>
                <a role="tab" class="tab tab-xs"
                   onclick="switchArtifactTab(this, 'artifact-code')">Code</a>
                <a role="tab" class="tab tab-xs"
                   onclick="switchArtifactTab(this, 'artifact-tests')">Tests</a>
                <a role="tab" class="tab tab-xs"
                   onclick="switchArtifactTab(this, 'artifact-review')">Review</a>
            </div>
        </div>

        <!-- Plan Artifact -->
        <div id="artifact-plan" class="artifact-tab flex-1 overflow-y-auto p-4">
            <div class="text-center opacity-20 text-xs mt-8">
                Orchestrator の計画結果がここに表示されます
            </div>
        </div>

        <!-- Code Artifact -->
        <div id="artifact-code" class="artifact-tab hidden flex-1 overflow-y-auto p-4">
            <div class="text-center opacity-20 text-xs mt-8">
                Executor の実装コードがここに表示されます
            </div>
        </div>

        <!-- Tests Artifact -->
        <div id="artifact-tests" class="artifact-tab hidden flex-1 overflow-y-auto p-4">
            <div class="text-center opacity-20 text-xs mt-8">
                Tester のテストコードがここに表示されます
            </div>
        </div>

        <!-- Review Artifact -->
        <div id="artifact-review" class="artifact-tab hidden flex-1 overflow-y-auto p-4">
            <div class="text-center opacity-20 text-xs mt-8">
                Reviewer の最終レポートがここに表示されます
            </div>
        </div>

        <!-- Artifact Footer (action buttons) -->
        <div id="artifact-footer" class="flex-none p-3 border-t border-base-300 hidden">
            <div class="flex gap-2">
                <button onclick="copyArtifact()" class="btn btn-ghost btn-xs flex-1 gap-1">
                    <svg class="w-3 h-3" ...copy icon...></svg> Copy
                </button>
                <button onclick="downloadArtifact()" class="btn btn-ghost btn-xs flex-1 gap-1">
                    <svg class="w-3 h-3" ...download icon...></svg> Download
                </button>
                <button onclick="sendToChat()" class="btn btn-ghost btn-xs flex-1 gap-1">
                    <svg class="w-3 h-3" ...chat icon...></svg> To Chat
                </button>
            </div>
        </div>
    </div>
</div>
```

---

### C. エージェントカードの状態アニメーション

エージェントの3つの状態に対してCSSクラスを切り替える：

```css
/* 待機状態（デフォルト） */
.agent-card { opacity: 0.7; }

/* 処理中状態 */
.agent-card.agent-active {
    opacity: 1;
    border-color: currentColor;
    box-shadow: 0 0 0 3px rgba(var(--color-primary), 0.15);
    transform: translateY(-2px);
}

/* 処理中のpulseアニメーション */
.agent-card.agent-active .agent-status::before {
    content: '';
    display: inline-block;
    width: 6px;
    height: 6px;
    border-radius: 50%;
    background: oklch(var(--p));
    animation: pulse 1s infinite;
    margin-right: 4px;
}

@keyframes pulse {
    0%, 100% { opacity: 1; transform: scale(1); }
    50% { opacity: 0.4; transform: scale(0.8); }
}

/* 完了状態 */
.agent-card.agent-done {
    opacity: 1;
    border-color: oklch(var(--su));  /* success色 */
}

/* エラー状態 */
.agent-card.agent-error {
    border-color: oklch(var(--er));
}
```

```javascript
// エージェント状態更新関数
function setAgentStatus(role, state) {
    // state: 'idle' | 'active' | 'done' | 'error'
    const card = document.getElementById(`agent-card-${role}`);
    const statusEl = card.querySelector('.agent-status');

    card.classList.remove('agent-active', 'agent-done', 'agent-error');

    const statusText = {
        idle:   '待機中',
        active: '処理中...',
        done:   '✓ 完了',
        error:  '✗ エラー'
    };

    if (state !== 'idle') card.classList.add(`agent-${state}`);
    statusEl.textContent = statusText[state];
}
```

---

### D. Pipeline Log エントリー

各エージェントの出力をログに追加する関数：

```javascript
function appendPipelineLog(role, content, durationMs) {
    const log = document.getElementById('pipeline-log');
    const empty = document.getElementById('pipeline-empty');
    if (empty) empty.remove();

    const badgeClass = {
        Orchestrator: 'badge-info',
        Executor:     'badge-success',
        Tester:       'badge-warning',
        Reviewer:     'badge-secondary'
    }[role] || 'badge-ghost';

    const entry = document.createElement('div');
    entry.className = 'bg-base-200 rounded-xl border border-base-300 overflow-hidden animate-[fadeIn_0.3s_ease-out]';
    entry.innerHTML = `
        <div class="flex items-center gap-2 px-4 py-2 border-b border-base-300/50 bg-base-100">
            <span class="badge ${badgeClass} badge-sm font-bold">${role}</span>
            <span class="flex-1 text-xs opacity-50">${new Date().toLocaleTimeString()}</span>
            ${durationMs ? `<span class="text-[10px] opacity-30">${durationMs}ms</span>` : ''}
            <button class="btn btn-ghost btn-xs"
                    onclick="this.closest('.log-entry-body').classList.toggle('hidden')">
                <svg class="w-3 h-3" viewBox="0 0 24 24" fill="none" stroke="currentColor">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 9l-7 7-7-7"/>
                </svg>
            </button>
        </div>
        <div class="log-entry-body p-4 text-sm markdown">${content}</div>
    `;
    log.appendChild(entry);
    log.scrollTop = log.scrollHeight;
}
```

---

### E. Artifact パネルへの成果物振り分け

SSE `step-complete` イベント受信時に、ロール別にアーティファクトパネルへ振り分ける：

```javascript
function handleDevSSEEvent(event, data) {
    if (event === 'session') {
        document.getElementById('dev-session-id').value = data;

    } else if (event === 'step-complete') {
        const separatorIndex = data.indexOf('|');
        const role = data.substring(0, separatorIndex);
        const stepHtml = data.substring(separatorIndex + 1).replace(/\\n/g, '\n');

        // エージェントカード: active → done
        setAgentStatus(role, 'done');

        // パイプラインログに追加
        appendPipelineLog(role, stepHtml);

        // アーティファクトパネルに振り分け
        const artifactMap = {
            Orchestrator: 'artifact-plan',
            Executor:     'artifact-code',
            Tester:       'artifact-tests',
            Reviewer:     'artifact-review'
        };
        const targetId = artifactMap[role];
        if (targetId) {
            const panel = document.getElementById(targetId);
            panel.innerHTML = `<div class="prose prose-sm max-w-none markdown">${stepHtml}</div>`;
            renderMarkdown();

            // アーティファクトフッターを表示
            document.getElementById('artifact-footer').classList.remove('hidden');

            // 完了したタブに通知バッジ
            markArtifactTabReady(targetId);
        }

    } else if (event === 'final') {
        const finalHtml = data.replace(/\\n/g, '\n');
        // Reviewerタブをアクティブにして最終結果を表示
        switchArtifactTab(
            document.querySelector('[onclick*="artifact-review"]'),
            'artifact-review'
        );
        document.getElementById('artifact-review').innerHTML =
            `<div class="prose prose-sm max-w-none markdown">${finalHtml}</div>`;
        renderMarkdown();

    } else if (event === 'done') {
        // 全エージェント完了
        document.getElementById('dev-run-btn').disabled = false;
        document.getElementById('dev-run-btn').classList.remove('loading');
        // タスクリストに完了済みとして保存
        saveTaskToList(
            document.getElementById('dev-input').value,
            'done'
        );
    }
}
```

---

### F. Dev Mode フォーム送信処理

```javascript
async function handleDevFormSubmit(e) {
    e.preventDefault();

    const taskContent = document.getElementById('dev-input').value.trim();
    if (!taskContent) return false;

    // UIをリセット・ローディング状態に
    document.getElementById('pipeline-log').innerHTML =
        '<div id="pipeline-empty" class="flex flex-col items-center justify-center h-full text-base-content/20 space-y-3"></div>';
    ['Orchestrator', 'Executor', 'Tester', 'Reviewer'].forEach(r => setAgentStatus(r, 'idle'));
    ['artifact-plan', 'artifact-code', 'artifact-tests', 'artifact-review'].forEach(id => {
        document.getElementById(id).innerHTML =
            '<div class="text-center opacity-20 text-xs mt-8">処理中...</div>';
    });
    document.getElementById('artifact-footer').classList.add('hidden');

    const runBtn = document.getElementById('dev-run-btn');
    runBtn.disabled = true;
    runBtn.classList.add('loading');

    // フォームデータ収集（ロール別プロバイダー含む）
    const formData = new FormData();
    formData.append('content', taskContent);
    formData.append('mode', 'cooperative');
    formData.append('sessionId', document.getElementById('dev-session-id').value);

    // 各エージェントカードのselectからプロバイダーを取得
    ['Orchestrator', 'Executor', 'Tester', 'Reviewer'].forEach(role => {
        const sel = document.querySelector(`select[name="roleProvider_${role}"]`);
        if (sel) formData.append(`roleProvider_${role}`, sel.value);
    });

    // 最初のアクティブなエージェントをハイライト
    setAgentStatus('Orchestrator', 'active');

    try {
        const response = await fetch('/api/chat/cooperate/stream', {
            method: 'POST',
            body: formData
        });

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        const agentOrder = ['Orchestrator', 'Executor', 'Tester', 'Reviewer'];
        let agentIndex = 0;

        while (true) {
            const { done, value } = await reader.read();
            if (done) break;

            const text = decoder.decode(value, { stream: true });
            let eventName = '';

            for (const line of text.split('\n')) {
                if (line.startsWith('event: ')) {
                    eventName = line.slice(7).trim();
                } else if (line.startsWith('data: ')) {
                    const data = line.slice(6).trim();
                    handleDevSSEEvent(eventName, data);

                    // step-complete後に次のエージェントをアクティブ化
                    if (eventName === 'step-complete') {
                        agentIndex++;
                        if (agentIndex < agentOrder.length) {
                            setAgentStatus(agentOrder[agentIndex], 'active');
                        }
                    }
                }
            }
        }
    } catch (err) {
        console.error('Dev mode error:', err);
        ['Orchestrator', 'Executor', 'Tester', 'Reviewer'].forEach(r => {
            const card = document.getElementById(`agent-card-${r}`);
            if (card.classList.contains('agent-active')) setAgentStatus(r, 'error');
        });
        runBtn.disabled = false;
        runBtn.classList.remove('loading');
    }

    return false;
}
```

---

### G. アーティファクトタブ制御

```javascript
function switchArtifactTab(el, targetId) {
    document.querySelectorAll('.artifact-tab').forEach(t => t.classList.add('hidden'));
    document.getElementById(targetId).classList.remove('hidden');

    if (el) {
        document.querySelectorAll('[onclick*="switchArtifactTab"]').forEach(t => {
            t.classList.remove('tab-active');
        });
        el.classList.add('tab-active');
    }
}

function markArtifactTabReady(artifactId) {
    const tabMap = {
        'artifact-plan':   'Plan',
        'artifact-code':   'Code',
        'artifact-tests':  'Tests',
        'artifact-review': 'Review'
    };
    // タブボタンに緑のドットを追加
    const tabName = tabMap[artifactId];
    document.querySelectorAll(`[onclick*="${artifactId}"]`).forEach(tab => {
        if (!tab.querySelector('.ready-dot')) {
            tab.insertAdjacentHTML('beforeend',
                '<span class="ready-dot w-1.5 h-1.5 bg-success rounded-full ml-1 inline-block"></span>');
        }
    });
}

function copyArtifact() {
    const activeTab = document.querySelector('.artifact-tab:not(.hidden)');
    navigator.clipboard.writeText(activeTab?.innerText || '');
}

function downloadArtifact() {
    const activeTab = document.querySelector('.artifact-tab:not(.hidden)');
    const content = activeTab?.innerText || '';
    const tabId = activeTab?.id || 'artifact';

    const extMap = {
        'artifact-code':  '.md',
        'artifact-tests': '.md',
        'artifact-plan':  '.md',
        'artifact-review':'.md'
    };

    const blob = new Blob([content], { type: 'text/markdown' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = `${tabId}${extMap[tabId] || '.txt'}`;
    a.click();
}

function sendToChat() {
    const activeTab = document.querySelector('.artifact-tab:not(.hidden)');
    const content = activeTab?.innerText || '';
    // Chat Modeに切り替えてテキストを引用挿入
    switchMode('chat');
    const input = document.getElementById('chat-input');
    input.value = '```\n' + content + '\n```\n\nこのコードについて...';
    autoExpand(input);
    input.focus();
}
```

---

### H. タスクリスト管理（左パネル）

```javascript
// タスクをローカルストレージに保存・表示
const TASKS_KEY = 'dev_tasks';

function saveTaskToList(content, status = 'done') {
    const tasks = JSON.parse(localStorage.getItem(TASKS_KEY) || '[]');
    tasks.unshift({
        id: Date.now(),
        content: content.length > 30 ? content.slice(0, 30) + '...' : content,
        status,
        createdAt: new Date().toISOString()
    });
    // 最大20件
    if (tasks.length > 20) tasks.pop();
    localStorage.setItem(TASKS_KEY, JSON.stringify(tasks));
    renderTaskList();
}

function renderTaskList() {
    const tasks = JSON.parse(localStorage.getItem(TASKS_KEY) || '[]');
    const list = document.getElementById('dev-task-list');
    const badgeMap = { done: 'badge-success', error: 'badge-error', pending: 'badge-warning' };

    list.innerHTML = tasks.map(t => `
        <button class="btn btn-ghost btn-xs justify-start text-left font-normal gap-2 w-full overflow-hidden"
                onclick="loadTask(${t.id})">
            <span class="badge ${badgeMap[t.status] || 'badge-ghost'} badge-xs flex-none"></span>
            <span class="truncate text-[11px]">${escapeHtml(t.content)}</span>
        </button>
    `).join('');
}

// 起動時にタスクリストを描画
renderTaskList();
```

---

## 追加CSS（`<style>`タグ内に追加）

```css
/* Dev Mode フェードインアニメーション */
@keyframes fadeIn {
    from { opacity: 0; transform: translateY(8px); }
    to   { opacity: 1; transform: translateY(0); }
}

/* エージェントカード */
.agent-card {
    transition: all 0.3s cubic-bezier(0.2, 0.8, 0.2, 1);
}
.agent-card.agent-active {
    opacity: 1;
    transform: translateY(-2px);
    box-shadow: 0 4px 20px rgba(0, 0, 0, 0.1);
}
.agent-card.agent-done {
    opacity: 1;
}

/* パイプラインログエントリー */
#pipeline-log > div {
    animation: fadeIn 0.3s ease-out;
}

/* アーティファクトパネルのコードブロック */
#artifact-code pre, #artifact-tests pre {
    background: oklch(var(--b3));
    padding: 1rem;
    border-radius: 8px;
    overflow-x: auto;
    font-size: 0.8rem;
    line-height: 1.6;
}

/* Readyドット */
.ready-dot {
    animation: fadeIn 0.3s ease-out;
}
```

---

## 変更対象ファイルと変更箇所まとめ

| ファイル | 変更内容 | 変更方法 |
|---------|---------|---------|
| `wwwroot/index.html` | `<body>`直下に `id="chat-layout"` を既存 `.drawer` に追加 | 属性追加 |
| `wwwroot/index.html` | Navbar を全モード共通の固定Navbarに変更 | 置換 |
| `wwwroot/index.html` | `#dev-layout` ブロックを `.drawer` の兄弟として追加 | 挿入 |
| `wwwroot/index.html` | `<style>` に追加CSS を挿入 | 挿入 |
| `wwwroot/index.html` | `<script>` に `switchMode`, `setAgentStatus`, `handleDevFormSubmit`, `handleDevSSEEvent`, `appendPipelineLog`, `switchArtifactTab`, `markArtifactTabReady`, `copyArtifact`, `downloadArtifact`, `sendToChat`, `saveTaskToList`, `renderTaskList` を追加 | 挿入 |

---

## 実装後の期待される動作

1. ページ読み込み時、前回のモードを `localStorage` から復元
2. **Chat Mode**: 既存のチャットUIと完全に同じ動作
3. **Dev Mode** タスク入力 → "Run" クリック：
   - Orchestrator カードがハイライト＋pulse
   - SSEでステップ完了ごとにログに追加
   - 次のエージェントカードがハイライト
   - 各成果物が対応するタブに自動振り分け
   - 全完了後、Review タブが自動選択
   - タスクリストに「完了」として保存

---

## モバイル対応

Dev Mode は3カラムのため、モバイル（`< 1024px`）では以下のように縮退：

```html
<!-- タブ切替でパネルを切り替える（モバイル） -->
<div class="lg:hidden tabs tabs-boxed w-full">
    <a class="tab tab-active" onclick="showDevPanel('pipeline')">Pipeline</a>
    <a class="tab" onclick="showDevPanel('artifacts')">Artifacts</a>
    <a class="tab" onclick="showDevPanel('tasks')">Tasks</a>
</div>
```

```javascript
function showDevPanel(panel) {
    // lg未満のみ有効
    if (window.innerWidth >= 1024) return;
    const map = {
        pipeline:  '#dev-center-panel',
        artifacts: '#dev-right-panel',
        tasks:     '#dev-left-panel'
    };
    Object.values(map).forEach(id => document.querySelector(id)?.classList.add('hidden'));
    document.querySelector(map[panel])?.classList.remove('hidden');
}
```
