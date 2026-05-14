---
name: dsai sqlite wal mode fix
description: dsai项目在并发请求（尤其是opencode长时间占用写锁期间）会触发`sqlalchemy.exc.OperationalError: (sqlite3.O...
type: user
userId: 0
tags: dsai sqlite wal mode fix
relations: relevanceScore: 80
relevanceScore: 80
accessCount: 4
createdAt: 2026-05-01T13:16:51.5288123Z
lastAccessedAt: 2026-05-14T00:52:10.0747498Z
---

dsai项目在并发请求（尤其是opencode长时间占用写锁期间）会触发`sqlalchemy.exc.OperationalError: (sqlite3.OperationalError) database is locked`，导致500内部服务器错误。

修复：在`app/database.py`中通过`event.listens_for(engine.sync_engine, "connect")`监听器启用`PRAGMA journal_mode=WAL`，同时将SQLite超时时间设为30秒。注意：`create_async_engine`使用`aiosqlite`，须通过`.sync_engine`监听。

**Why:** opencode等AI调用耗时较长（可能超60秒），期间其他请求尝试写入时会遭遇写锁冲突。WAL模式允许并发读，减少锁冲突。
**How to apply:** dsai出现500"database is locked"时，检查`database.py`是否已启用WAL模式；异步SQLAlchemy使用`engine.sync_engine`注册connect事件。