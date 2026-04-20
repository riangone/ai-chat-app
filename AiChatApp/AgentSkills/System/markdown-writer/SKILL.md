---
name: markdown-writer
description: Assists users in creating, editing, and refining Markdown documentation with a focus on clarity, structure, and literary elegance. Use this skill when the user needs to write READMEs, API docs, or any technical documentation.
---

# Markdown Writer

## Overview
The Markdown Writer skill transforms technical details into evocative and structured documentation. It provides templates, best practices, and a refined writing style to ensure your documentation is both informative and a pleasure to read.

## Workflows

### 1. Initialize New Documentation
When a user wants to start a new document (e.g., "Help me start a README"), use the provided template.

1.  Read the template from `assets/template.md`.
2.  Ask the user for project-specific details if they haven't been provided.
3.  Generate the initial Markdown structure, infusing it with clear and professional language.

### 2. Refine Existing Documentation
When a user provides existing text and wants to improve it (e.g., "Polish this API doc"), apply best practices.

1.  Analyze the provided text against `references/best-practices.md`.
2.  Improve the hierarchy, clarity, and formatting.
3.  Enhance the prose using vivid and precise vocabulary.

### 3. Generate API Documentation
When a user provides code or a description of an interface, create structured API documentation.

1.  Identify endpoints, parameters, and return types.
2.  Use clear H3 headings for each endpoint.
3.  Include code blocks with syntax highlighting for examples.

## Guidelines
- **Literary Elegance**: Use varied sentence structures and precise vocabulary. Avoid jargon where a clearer, more elegant word suffices.
- **Visual Clarity**: Use white space effectively to make the document easy to scan.
- **Consistency**: Ensure terminology is consistent throughout the entire document.
