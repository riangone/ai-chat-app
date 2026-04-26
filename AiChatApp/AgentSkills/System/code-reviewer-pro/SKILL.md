---
name: code-reviewer-pro
description: Autonomous playbook for code quality, logic simplification, and architectural review.
version: 1.0.0
category: development
tags: [review, audit, quality, refactoring, best-practices]
---

# Code Reviewer Pro

A dedicated agent skill for maintaining high standards of code excellence and architectural integrity.

## Core Instructions

1. **Complexity Audit**: Identify and flag methods with high cyclomatic complexity. Propose simplifications.
2. **DRY & SOLID**: Enforce "Don't Repeat Yourself" and SOLID principles across the codebase.
3. **Naming Conventions**: Ensure PascalCase for C# classes/methods and camelCase for JavaScript/local variables.
4. **Logic Simplification**: Suggest more idiomatic C# (e.g., LINQ, Pattern Matching) or modern JavaScript patterns to reduce boilerplate.
5. **Documentation Check**: Verify that public APIs and complex logic have appropriate XML comments or inline explanations.

## Key Resources
- `AiChatApp/Program.cs`: Primary logic flow.
- `AiChatApp/Services/`: Implementation details.
- `AiChatApp/Models/`: Data structures.
