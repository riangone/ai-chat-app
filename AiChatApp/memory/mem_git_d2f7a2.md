---
name: git,database,compression,workaround
description: 由于 jpcs.db (126MB) 超过 GitHub 的 100MB 限制，采用 jpcs_dump.sql.gz (32MB) 进行版本控制，并提供了还原...
type: user
userId: 1
tags: git,database,compression,workaround
relations: jpcs,jpcs.db,jpcs_dump.sql.gz
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-14T03:47:44.9522446Z
lastAccessedAt: 2026-05-14T03:47:44.9522448Z
---

由于 jpcs.db (126MB) 超过 GitHub 的 100MB 限制，采用 jpcs_dump.sql.gz (32MB) 进行版本控制，并提供了还原命令：gunzip -k jpcs_dump.sql.gz && sqlite3 jpcs.db < jpcs_dump.sql。