---
name: C#, ASP.NET Core, Models, BugFix, Styling
description: 在 Models/PageDefinition.cs 中的 SectionDefinition 类添加了 Class 属性 (string?)，以修复 _Sec...
type: user
userId: 1
tags: C#, ASP.NET Core, Models, BugFix, Styling
relations: SectionDefinition,PageDefinition.cs,_SectionMarkdown.cshtml
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-15T04:18:32.1131413Z
lastAccessedAt: 2026-05-15T04:18:32.1131415Z
---

在 Models/PageDefinition.cs 中的 SectionDefinition 类添加了 Class 属性 (string?)，以修复 _SectionMarkdown.cshtml 中引用 sec.Class 导致的 CS1061 编译错误。