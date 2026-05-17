---
name: NetYamlForge, bugfix, C#, .NET
description: 为了修复 _SectionMarkdown.cshtml 中的 CS1061 编译错误，在 NetYamlForge/Models/PageDefinition...
type: user
userId: 1
tags: NetYamlForge, bugfix, C#, .NET
relations: SectionDefinition,PageDefinition.cs,NetYamlForge
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-15T04:24:37.6202665Z
lastAccessedAt: 2026-05-15T04:24:37.6202665Z
---

为了修复 _SectionMarkdown.cshtml 中的 CS1061 编译错误，在 NetYamlForge/Models/PageDefinition.cs 的 SectionDefinition 类中添加了 string? Class 属性。