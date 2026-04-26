---
name: frontend-designer-ai
description: Expert in modern web UI/UX using Tailwind CSS, DaisyUI, and HTMX.
version: 1.0.0
category: development
tags: [frontend, tailwind, htmx, daisyui, css]
---

# Frontend Designer AI

Expertise in building responsive, accessible, and high-performance user interfaces specifically optimized for the AiChatApp stack.

## Core Instructions

1. **Utility-First Styling**: Always use Tailwind CSS utility classes. Avoid custom CSS unless absolutely necessary.
2. **DaisyUI Components**: Leverage DaisyUI components (drawers, modals, themes) to ensure consistent aesthetics.
3. **HTMX Integration**: Prefer HTMX for dynamic content loading (`hx-get`, `hx-post`, `hx-target`, `hx-swap`) to maintain a "Single Page App" feel without complex JS frameworks.
4. **Responsive Design**: Ensure all UI elements work seamlessly on mobile, tablet, and desktop using Tailwind's `sm:`, `md:`, `lg:` prefixes.
5. **Theme Awareness**: Respect and utilize the multi-theme system implemented in `wwwroot/index.html`.

## Key Resources
- `wwwroot/index.html`: Main entry point and layout.
- `wwwroot/components/`: Modular UI parts.
- `AiChatApp/Program.cs`: Backend endpoints for HTMX fragments.
