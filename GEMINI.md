# AiChatApp - Gemini Terminal Chat Wrapper (v2.0)

AiChatApp is a web-based chat interface that leverages the `gemini` CLI to provide AI responses. Version 2.0 introduces multi-user support, enhanced UI, and mobile responsiveness.

## Project Overview

- **Architecture:** ASP.NET Core 10 Minimal API.
- **AI Integration:** Wraps the `gemini` CLI via `System.Diagnostics.Process` in `AiService.cs`.
- **Database:** SQLite (managed via Entity Framework Core).
- **Authentication:** Cookie-based authentication with password hashing (BCrypt).
- **Frontend:** Responsive SPA using HTMX, Tailwind CSS, DaisyUI, and Marked.js.
- **Key Features:**
  - Mobile & Tablet responsive UI (Drawer layout).
  - User registration and login.
  - Multi-session chat management with title renaming.
  - Theme switcher with 30+ DaisyUI themes (Light, Dark, Cupcake, etc.).
  - Message actions: Copy to clipboard and Forward to input.
  - Markdown support for all messages.
  - Input field with clear button.

## Directory Structure

- `AiChatApp/Program.cs`: Main logic, Authentication, and API endpoints.
- `AiChatApp/Models/`: Data models for `User`, `ChatSession`, and `Message`.
- `AiChatApp/Data/AppDbContext.cs`: EF Core database context.
- `AiChatApp/Services/AiService.cs`: AI interaction logic.
- `AiChatApp/wwwroot/`:
  - `index.html`: Main chat interface.
  - `login.html`: Login page.
  - `register.html`: User registration page.

## Building and Running

### Prerequisites
- .NET 10 SDK
- `gemini` CLI must be installed and available in the system PATH.

### Management Scripts
For convenience, scripts are provided to manage the application lifecycle:
- `./start.sh`: Starts the app in the background (logs to `app.log`).
- `./stop.sh`: Stops the background process.
- `./restart.sh`: Restarts the app.

### Build
```bash
dotnet build
```

### Run
```bash
dotnet run --project AiChatApp
```
The application will be available at `http://localhost:5000`.

## Development Conventions

- **Security:** User passwords are hashed using `BCrypt.Net-Next`.
- **Responsiveness:** Uses DaisyUI `drawer` for mobile sidebar support.
- **Interactions:** HTMX handles all dynamic content loading and form submissions without full page reloads.
- **Markdown:** Messages are rendered on the client side using `marked.js` upon being loaded by HTMX.
