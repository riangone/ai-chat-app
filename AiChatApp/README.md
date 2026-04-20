# AiChatApp - Intelligent Terminal Chat Wrapper (v2.0)

[中文版本 (Chinese Version)](./README_ZH.md)

AiChatApp is a modern, web-based chat interface that bridges professional AI CLI tools (like `gemini` and `gh-copilot`) with a rich, responsive user experience. It features long-term memory, cooperative multi-agent task execution, and advanced session management.

## 🚀 Key Features

- **Multi-Provider Support**: Seamlessly switch between `Gemini` and `GitHub Copilot` (via CLI wrappers).
- **Cooperative Mode**: A multi-agent workflow where an **Orchestrator** plans, an **Executor** implements, and a **Reviewer** verifies the output.
- **Long-Term Memory**: AI remembers user-defined facts and preferences. Memories are retrieved based on keyword tags during conversations.
- **Skill System**: Toggleable "Plugins" or instructions that enhance AI capabilities for specific tasks.
- **Modern UI/UX**:
  - Built with **HTMX** for smooth, SPA-like interactions without the complexity of heavy JS frameworks.
  - Styled with **Tailwind CSS** and **DaisyUI** (includes 30+ themes).
  - Responsive **Drawer layout** for mobile/tablet support.
  - Real-time **Markdown rendering** (using `marked.js`).
  - Native-feeling message actions: Copy, Forward (Quote), and Save to Memory.
- **Session Management**: Full history tracking, chat renaming, and instant session creation.
- **Secure Authentication**: Cookie-based auth with **BCrypt** password hashing.

## 🛠 Tech Stack

- **Backend**: ASP.NET Core 10 Minimal API.
- **Database**: SQLite with Entity Framework Core.
- **AI Integration**: Custom `AiService` wrapping CLI processes (`System.Diagnostics.Process`).
- **Frontend**: HTMX, Tailwind CSS, DaisyUI, Marked.js.
- **Security**: BCrypt.Net-Next for hashing, Cookie Authentication.

## 📂 Project Structure

```text
AiChatApp/
├── Data/            # EF Core DbContext
├── Models/          # Data Models (User, Message, ChatSession, Memory, Skill)
├── Services/        # AiService (AI logic & CLI interaction)
├── wwwroot/         # Frontend (HTMX + Tailwind + DaisyUI)
│   ├── index.html   # Main Chat Interface
│   ├── login.html   # Authentication Pages
│   └── register.html
├── Program.cs       # App entry point & API Endpoints
└── AiChatApp.csproj # Dependencies & Config
```

## ⚙️ Prerequisites

1. **.NET 10 SDK** installed.
2. **AI CLIs**:
   - `gemini` CLI (available in PATH).
   - `gh-copilot` (optional, for GitHub Copilot support).

## 🏃 Getting Started

### 1. Clone & Build
```bash
dotnet build
```

### 2. Configure Database
The application uses SQLite (`chat.db`). The database and schema are automatically created on first run.

### 3. Run the Application
```bash
dotnet run --project AiChatApp
```
The app will be available at `http://localhost:5000`.

## 🧠 Advanced Concepts

### Cooperative Mode (Multi-Agent)
When enabled, the `AiService` initiates a 3-step pipeline:
1. **Orchestrator**: Analyzes the request and breaks it into a plan.
2. **Executor**: Follows the plan to generate code or detailed logic.
3. **Reviewer**: Reviews the execution and provides the final polished response.

### Long-Term Memory
Users can save specific snippets of information to "Memory" with tags. When a user message contains a keyword matching a tag, the `AiService` automatically injects that memory into the AI's system prompt as "Known Facts".

### Skill System
Skills are essentially system-level instructions that can be toggled on/off. When a skill is enabled and its name is mentioned in a prompt, its specific instruction set is prioritized in the system prompt.

## 📝 License
This project is for internal/educational use. Refer to the respective CLI tool licenses for AI usage terms.
