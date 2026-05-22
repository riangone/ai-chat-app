---
name: 后端, C#, 文件名生成, 占位符
description: 扩展了 DynamicEntityController.cs 中的 BuildPdfFilename 方法，支持通过 {FieldName} 占位符（例如 {I...
type: user
userId: 1
tags: 后端, C#, 文件名生成, 占位符
relations: DynamicEntityController,InvoiceNo,filenameTemplate
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-22T03:12:36.4089902Z
lastAccessedAt: 2026-05-22T03:12:36.4089904Z
---

扩展了 DynamicEntityController.cs 中的 BuildPdfFilename 方法，支持通过 {FieldName} 占位符（例如 {InvoiceNo}）动态生成 PDF 文件名，并自动处理无效字符。