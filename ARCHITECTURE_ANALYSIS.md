# アーキテクチャ分析：Hermes Agent / Harness Engineering 思想の実装状況

本文書は、`harness-ui.md` および `memory-design.md` に記載された設計思想に対して、現在のコード実装がどの程度実現できているかを評価する。

---

## 評価サマリー

| 設計思想 | 実装完成度 | 評価 |
|----------|-----------|------|
| Harness: マルチエージェント協調 | 40% | 骨格はあるが形式的 |
| Harness: エージェント可視化 | 60% | UIはあるが静的 |
| Hermes: 長期記憶 (RAG) | 35% | 構造はあるが検索が粗い |
| Hermes: スキルシステム | 50% | 存在するが設計に欠陥 |
| Hermes: 記憶固化 | 5% | メソッドが存在するだけ |

---

## Part 1：Harness Engineering 思想の実装分析

### 1-1. マルチエージェント協調（Orchestrator / Executor / Reviewer）

**設計意図** (`harness-ui.md`より):
- Orchestratorがタスクを分解し、Workerに配布
- Workerが専門領域に集中して出力
- Reviewerが品質を検証し、必要なら差し戻す
- 各エージェントが独立した役割と人格を持つ

**現在の実装** (`AiService.cs:69-109`):
```csharp
// 3ステップが直列に並ぶだけ
string plan      = await CallAgentAsync("Orchestrator", ...);
string execution = await CallAgentAsync("Executor", ...);
string finalResult = await CallAgentAsync("Reviewer", ...);
```

**達成できている点:**
- 3役割の分離と連鎖呼び出し構造は存在する
- `CallAgentAsync` でシステムプロンプトを動的に差し替える機構がある
- 中間産物（plan, execution）が次のエージェントへの入力として渡される

**達成できていない点:**

| 設計意図 | 問題 |
|----------|------|
| タスクの動的分解 | Orchestratorの出力は自由テキスト。構造化されたサブタスクリスト（JSON等）に分解されず、Executorは「規划内容」をそのまま受け取るだけ |
| 差し戻し・ループ | Reviewerが「不十分」と判断した場合のフィードバックループが存在しない。常に1パスで終了 |
| 役割の拡張性 | 役割は `CooperateAsync` 内にハードコード。`harness-ui.md` が示した「5エージェント対応」には手動でのコード修正が必要 |
| エージェント間の共有コンテキスト | `LongTermMemory` を「shared blackboard」として使うという設計意図が未実現。各エージェント呼び出しは独立したプロセスで、DB上の共有状態を持たない |
| プロバイダーの柔軟性 | `CooperateAsync` は常に `gemini` を使う。呼び出し元の `provider` パラメータを無視（`Program.cs:149`では渡しているが`AiService.cs:69`のシグネチャが受け取らない） |

---

### 1-2. エージェント作業の可視化

**設計意図** (`harness-ui.md`より):
- 「AI A が思考中...」「AI B が AI A のコードを検証中...」等のリアルタイム状態表示
- 異なるエージェントの中間産物を複数カード/ウィンドウで並列展示

**現在の実装** (`AiService.cs:84-109`):
- Step1（規划）・Step2（執行）をCollapse折りたたみパネルで表示
- 最終出力（Reviewer）を下部に表示

**達成できている点:**
- 中間産物（規划・執行）が折りたたみUIで確認できる
- DaisyUIのCollapseコンポーネントを適切に活用している

**達成できていない点:**

| 設計意図 | 問題 |
|----------|------|
| リアルタイム状態表示 | 全3ステップが完了するまでHTTPレスポンスが返らない。ユーザーには長い沈黙のみ |
| 進行状況インジケーター | ローディング表示すら存在しない |
| 並列エージェント表示 | 現在は直列処理。複数エージェントを同時に表示する機構がない |

---

## Part 2：Hermes Agent 思想の実装分析

### 2-1. 長期記憶と RAG（Retrieval Augmented Generation）

**設計意図** (`memory-design.md`より):
- ベクトル/スキルストアによる意味的検索
- 会話前にユーザー入力に関連するコンテキストを検索して注入
- SQLite-Vec または Semantic Kernel の活用

**現在の実装** (`AiService.cs:27-32`):
```csharp
var relevantMemories = memories
    .Where(m => prompt.Contains(m.Tags, StringComparison.OrdinalIgnoreCase))
    .ToList();
```

**達成できている点:**
- `LongTermMemory` モデル（Content + Tags）は設計通りに実装されている
- プロンプトへの動的注入パイプライン（`BuildEnhancedPrompt`）が機能している
- UIに「Save to Memory」ボタン（メッセージアクション）と記憶管理パネルがある

**達成できていない点:**

| 設計意図 | 問題 |
|----------|------|
| 意味的検索 (RAG) | キーワード完全一致のみ。「コード最適化して」と入力しても `Tags = "code"` のメモリはヒットしない。SQLite-Vec / Semantic Kernel は未導入 |
| 記憶の粒度 | `Tags` フィールドが単一文字列。複数タグや階層化の仕組みがない |
| 記憶スコープ | メモリはユーザー単位。エージェント間の shared blackboard としての用途が未実現 |
| 記憶の優先度・鮮度 | すべての記憶が同等に扱われる。古い・低関連度の記憶がプロンプトを汚染するリスクがある |

---

### 2-2. スキルシステム

**設計意図** (`memory-design.md`より):
- 異なるエージェントに異なるスキルセットを紐づける
- ユーザーが必要なスキルをON/OFFで制御

**現在の実装** (`AiService.cs:36-42`):
```csharp
var activeSkills = skills
    .Where(s => prompt.Contains(s.Name, StringComparison.OrdinalIgnoreCase))
    .ToList();
```

**達成できている点:**
- `Skill` モデルと `IsEnabled` トグルが存在する
- UIにスキル管理パネルがある
- プロンプト中にスキル名が含まれる場合、そのDescriptionがシステムプロンプトに追加される

**達成できていない点:**

| 設計意図 | 問題 |
|----------|------|
| エージェントへのスキル紐づけ | スキルは会話全体に注入される。特定のエージェント（ExecutorのみCodeOptimizerを使う等）への割り当てが不可能 |
| ユーザースコープ | `UserId` がなく全ユーザーに影響（`IMPROVEMENTS.md` Issue 3参照） |
| スキル発火条件 | プロンプトにスキル名（"CodeOptimizer"等）が文字通り含まれないと発火しない。実用上はほぼ使われない |

---

### 2-3. 記憶固化（Memory Consolidation）

**設計意図** (`memory-design.md`より):
- 会話終了時またはN回のターン後に、AIが会話を要約して長期記憶へ自動昇格
- `save_memory` に相当するHermes的なループ処理

**現在の実装**:
```csharp
// AiService.cs:172-176 — 存在するが未使用のデッドコード
public async Task<string> ConsolidateMemoryAsync(string userMessage, string aiResponse)
{
    string extractionPrompt = $"分析以下对话，提取出关于用户的长期事实...";
    return await ExecuteCliAsync(extractionPrompt, "gemini");
}
```

**達成できている点:**
- `ConsolidateMemoryAsync` メソッドの設計は正しい方向性

**達成できていない点:**

| 設計意図 | 問題 |
|----------|------|
| 自動固化 | `ConsolidateMemoryAsync` は `/api/chat` エンドポイントから一切呼ばれていない |
| 固化結果の保存 | メソッドの戻り値が `string` のままで、DB保存ロジックがない |
| 固化トリガー | N回ターン後の自動実行、バックグラウンドサービス等が存在しない |

---

## Part 3：根本的な設計上のギャップ

現在の実装は「アイデアの実証」に留まっており、設計文書が示したアーキテクチャとの間に以下の本質的なギャップがある。

### G1: ステートレスなエージェント

HarnessもHermesも「状態を持つエージェント」を前提とする。しかし現在のAIServiceは毎回独立したCLIプロセスを起動するだけで、会話履歴もエージェントの作業状態も保持しない。各エージェント呼び出しは前のエージェントの出力をテキストとして受け取るだけで、深い文脈共有がない。

### G2: オーケストレーションの欠如

Harness設計が示した「動的タスク分解→サブタスクへのルーティング→品質ゲートによる差し戻し」という本来のオーケストレーション循環が実装されていない。`CooperateAsync` は3つのAI呼び出しを直列に並べた「シーケンシャルチェーン」に過ぎず、真のオーケストレーターではない。

### G3: 記憶の検索品質

Hermes設計が提案したベクトル検索（SQLite-Vec）やSemantic Kernelによる意味的検索が未実装。現在のキーワード完全一致では、記憶システムは実用上ほぼ機能しない。

---

## 今後の実装優先度

設計思想をより忠実に実現するための次のステップを優先度順に示す。

| 優先度 | 改善項目 | 効果 |
|--------|----------|------|
| 1 | `ConsolidateMemoryAsync` の完成と自動呼び出し | Hermesの中核機能が動き始める |
| 2 | タグの部分一致または複数タグ対応 | 記憶の実用性が大幅向上 |
| 3 | `CooperateAsync` へのSSEストリーミング | Harnessの可視化体験が実現 |
| 4 | Reviewerの差し戻しループ（最大N回）の実装 | 真のオーケストレーション |
| 5 | SQLite FTS5 または Semantic Kernel の導入 | Hermes本来のRAG品質 |
