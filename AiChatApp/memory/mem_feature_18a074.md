---
name: feature,automation,email,invoice,ai
description: 实现了 InvoiceEmailProcessorExecutor (位于 Services/BatchJob/)，用于自动化处理邮件：接收 IMAP 邮件、通...
type: user
userId: 1
tags: feature,automation,email,invoice,ai
relations: InvoiceEmailProcessorExecutor,Gemini CLI,GoseiInvoice,PDF
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-22T03:33:01.8511345Z
lastAccessedAt: 2026-05-22T03:33:01.8511347Z
---

实现了 InvoiceEmailProcessorExecutor (位于 Services/BatchJob/)，用于自动化处理邮件：接收 IMAP 邮件、通过 Gemini CLI 提取 JSON 信息、保存至数据库、生成 PDF 并发送回信。