---
name: bugfix,permissions,linux,caddy
description: /home/ubuntu/ のパーミッションが 0750 であったため Caddy（caddyユーザー）がアクセスできず 403 エラーが発生していたが、chm...
type: user
userId: 1
tags: bugfix,permissions,linux,caddy
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-11T08:36:49.6476400Z
lastAccessedAt: 2026-05-11T08:36:49.6476401Z
---

/home/ubuntu/ のパーミッションが 0750 であったため Caddy（caddyユーザー）がアクセスできず 403 エラーが発生していたが、chmod o+x /home/ubuntu により修正された。