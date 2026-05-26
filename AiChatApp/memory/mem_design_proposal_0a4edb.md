---
name: design-proposal, invoice-templates, database-schema
description: 针对不同客户请求书模板样式的对应方案：建议在 GoseiClient 表中增加 InvoiceTemplate 和 TemplateVars 字段，以实现动态模...
type: user
userId: 1
tags: design-proposal, invoice-templates, database-schema
relations: GoseiClient,InvoiceTemplate,TemplateVars
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-25T04:11:05.7009045Z
lastAccessedAt: 2026-05-25T04:11:05.7009046Z
---

针对不同客户请求书模板样式的对应方案：建议在 GoseiClient 表中增加 InvoiceTemplate 和 TemplateVars 字段，以实现动态模板选择和变量注入。