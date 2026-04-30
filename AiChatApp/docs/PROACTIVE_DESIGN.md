# AI Proactive Brain Design & Logic

## 1. Overview
The Proactive Brain system is a core feature of AiChatApp that enables AI agents (primarily **Hyperion**) to observe user activities and provide autonomous, context-aware suggestions. Unlike standard chatbots, this system can initiate conversations based on data changes.

## 2. Technical Architecture

### 2.1 Backend Components
- **`ProactiveBrainService` (Singleton)**: The orchestrator that manages background tasks and AI orchestration.
- **`ProactiveAgentHub` (SignalR)**: Real-time communication bridge between server and client.
- **`AiService`**: Executes specific agent profiles (Summarizer, Hyperion) to generate insights.
- **`AppDbContext`**: Handles persistence of proactive messages into standard chat sessions.

### 2.2 Trigger Logic
Proactive analysis is currently triggered by:
- **Todo Changes**:
    - `POST /api/todos`: Triggers initial task analysis and decomposition suggestions.
    - `PUT /api/todos/{id}/toggle`: Triggers completion celebration and "next step" planning.
- **Note Changes**:
    - `POST /api/notes`: Triggers summary generation and long-term memory proposals.

## 3. The "Hyperion" Logic Flow

When a trigger (e.g., New Todo) occurs:
1. **Backgrounding**: The endpoint fires a non-blocking `Task.Run` call.
2. **Context Compression**: The `Summarizer` agent shrinks the task/note content to a one-sentence core goal.
3. **Insight Generation**: The `HyperionBrain` agent receives the summary and generates a professional engineering tip (strict < 60 words, Markdown formatted).
4. **Session Persistence**:
    - Looks for a session titled **"Hyperion 任务洞察"**.
    - Creates it if it doesn't exist.
    - Adds the insight as a `Message` (`IsAi = true`).
5. **Real-time Push**: Broadcasts a `ProactiveMessage` via SignalR containing the content and action buttons.

## 4. Frontend Interaction

### 4.1 Proactive UI Components
- **Floating Cards**: Displayed in the bottom-right. Features backdrop blur and Markdown rendering via `marked.js`.
- **Insight Center (Sidebar)**: A dedicated history panel in the sidebar that persists insights in `localStorage` for quick access.

### 4.2 Interaction Actions
- **Open Session**: Navigates the user directly to the persistent "Hyperion Insights" chat.
- **Use in Chat**: Injects the AI's suggestion into the current chat input for further discussion.
- **Dismiss**: Clears the notification while keeping it in history.

## 5. Configuration & Future Services

The system is designed to be extensible. Currently disabled services in `ServiceExtensions.cs` include:
- `FileWatcherService`: For proactive coding assistance on file save.
- `ProjectPulseService`: For periodic project status reports.
- `WelcomeInsight`: For "catch-up" summaries upon user login.

---
*Last Updated: April 30, 2026*
