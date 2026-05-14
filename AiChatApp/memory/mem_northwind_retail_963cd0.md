---
name: northwind-retail, bug-fix, yaml-config
description: 项目 northwind-retail 在启动时曾因 product.yml 的反序列化错误失败，原因是 FilterDefinition.Options 预期...
type: user
userId: 1
tags: northwind-retail, bug-fix, yaml-config
relations: northwind-retail,product.yml,FilterDefinition
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-13T05:50:53.5059898Z
lastAccessedAt: 2026-05-13T05:50:53.5059898Z
---

项目 northwind-retail 在启动时曾因 product.yml 的反序列化错误失败，原因是 FilterDefinition.Options 预期为 List<string> 但收到了对象格式。