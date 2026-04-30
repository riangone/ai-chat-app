# ページネーション設計書

## 概要

AiChatApp の全リスト表示ページで HTMX Infinite Scroll パターンによるページネーションを実装する。
ユーザー操作なしに、スクロールで自動的に次のページを読み込む UX を統一する。

---

## 対象エンドポイント一覧

| エンドポイント | ファイル | 状態 | デフォルト pageSize |
|---|---|---|---|
| `GET /api/chat/list` | `Endpoints/ChatEndpoints.cs` | ✅ 実装済み | 20 |
| `GET /api/chat/{id}/older-messages` | `Endpoints/ChatEndpoints.cs` | ✅ 実装済み（ID ベース） | 20 |
| `GET /api/todos` | `Endpoints/TodoEndpoints.cs` | ✅ 実装済み | 20 |
| `GET /api/notes` | `Endpoints/NotesEndpoints.cs` | ✅ 実装済み | 20 |
| `GET /api/input-history` | `Endpoints/InputHistoryEndpoints.cs` | ✅ 実装済み | 20 |
| `GET /api/memories` | `Endpoints/MemoryEndpoints.cs` | ✅ 実装済み | 20 |
| `GET /api/skills` | `Endpoints/SkillEndpoints.cs` | ✅ 実装済み | 20 |
| `GET /api/projects/list-html` | `Services/ProjectApiController.cs` | ✅ 実装済み | 20 |

---

## 統一パターン

### クエリパラメータ

| パラメータ | 型 | デフォルト | 説明 |
|---|---|---|---|
| `page` | `int` | `1` | 1 始まりのページ番号 |
| `pageSize` | `int` | `20` | 1 ページあたりの件数 |

### サーバー側ロジック（C#）

```csharp
var p = page ?? 1;
var ps = pageSize ?? 20;

// データベースから pageSize+1 件取得（次ページ存在チェック用）
var items = await db.Items
    .Where(i => i.UserId == userId)
    .OrderByDescending(i => i.CreatedAt)
    .Skip((p - 1) * ps)
    .Take(ps + 1)
    .ToListAsync();

var hasMore = items.Count > ps;
var itemsToReturn = items.Take(ps).ToList();

// 最終アイテムに infinite scroll 属性を付与
return string.Join("", itemsToReturn.Select((item, index) => {
    var isLast = index == itemsToReturn.Count - 1 && hasMore;
    var scrollAttr = isLast
        ? $"hx-get='/api/items?page={p + 1}&pageSize={ps}' hx-trigger='revealed' hx-swap='afterend'"
        : "";
    return RenderItem(item, scrollAttr);
}));
```

### フロントエンド側（HTMX）

特別な JS 実装は不要。HTMX の `hx-trigger='revealed'` が要素のビューポート進入を検知し、
次ページを `hx-swap='afterend'` で現在のリストの末尾に挿入する。

```html
<!-- 最終アイテムに自動付与される属性 -->
<div
  hx-get="/api/todos?page=2&pageSize=20"
  hx-trigger="revealed"
  hx-swap="afterend"
>
  <!-- item content -->
</div>
```

### 空状態

```csharp
if (!items.Any() && p == 1)
    return Results.Content("<div class='text-center py-10 opacity-40'>No items yet.</div>", "text/html");
```

---

## ID ベースページネーション（チャットメッセージ）

古いメッセージ読み込みはオフセットではなく ID ベースを使用。
スクロール中にデータが追加されてもズレが生じない。

```
GET /api/chat/{sessionId}/older-messages?beforeId={messageId}
```

レスポンスは最新 20 件のメッセージを `beforeId` より古いものから返す。

---

## ファイルシステムベースリスト（スキル・メモリ）

DB クエリではなくファイルシステムから全件取得後、メモリ上でページング。

```csharp
var all = service.GetAll();
var paged = all.Skip((p - 1) * ps).Take(ps + 1).ToList();
```

データ量が増えた場合は DB への移行を検討する。

---

## 対象外エンドポイント

| エンドポイント | 理由 |
|---|---|
| `GET /api/projects` | JSON レスポンス、JS 側で処理 |
| `GET /api/projects/{id}/pipeline-logs` | JSON レスポンス、JS 側で処理 |
| `GET /api/stats/*` | 集計結果、ページネーション不要 |
| `GET /api/files` | ディレクトリ単位の表示、構造的に不向き |
