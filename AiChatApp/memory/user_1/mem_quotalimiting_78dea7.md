---
name: QuotaLimiting,Tenant,DI
description: 多租户配额限制通过 ITenantQuotaValidator 与 TenantQuotaValidator 实现，并在 DI 中注册，在 RowMutatio...
type: user
userId: 1
tags: QuotaLimiting,Tenant,DI
relations: ITenantQuotaValidator,TenantQuotaValidator,RowMutationRepository.cs
relevanceScore: 80
accessCount: 0
createdAt: 2026-06-30T23:46:25.4815487Z
lastAccessedAt: 2026-06-30T23:46:25.4815488Z
boundAgentRole: 
---

多租户配额限制通过 ITenantQuotaValidator 与 TenantQuotaValidator 实现，并在 DI 中注册，在 RowMutationRepository 中执行配额验证。