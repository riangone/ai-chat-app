---
name: invoice validation,business logic,amount calculation
description: Implemented strict mathematical validation and automatic correction for extracte...
type: user
userId: 1
tags: invoice validation,business logic,amount calculation
relations: ExtractedInvoice,ParseExtractedInvoices
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-22T05:51:53.7772448Z
lastAccessedAt: 2026-05-22T05:51:53.7772449Z
---

Implemented strict mathematical validation and automatic correction for extracted invoice amounts (Amount = Quantity * Unit Price, Subtotal = Sum of Amounts, Total = Subtotal + Tax).