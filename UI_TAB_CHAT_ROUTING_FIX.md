# UI Tab And Chat Routing Fix

## Scope

This change fixes the main UI routing and loading issues in the chat shell:

- Tab panels now load their own list data reliably.
- Empty tab states render explicit placeholders instead of blank panels.
- History item selection always routes into the main Chat panel.
- CLI item selection always routes into the main Chat panel.
- Closing a tab no longer throws when the tab node is missing.
- Chat session creation and streaming now preserve `projectId` context.

## Files

- `AiChatApp/wwwroot/index.html`
- `AiChatApp/wwwroot/translations.js`
- `AiChatApp/Program.cs`

## Behavioral Changes

- Replaced fragile custom HTMX event dependency for tab list loading with explicit client-side panel fetches.
- Added `loadChatSession()` and `loadCliSession()` helpers to force chat content into `#chat-panel`.
- Reworked server-rendered History and CLI list buttons to call explicit client-side loaders.
- Added session-id synchronization after chat/CLI content injection.
- Added localized loading, error, and empty-state strings.

## Validation

- `node --check AiChatApp/wwwroot/translations.js`
- `dotnet build AiChatApp/AiChatApp.csproj`
- Application restart performed to ensure updated server-rendered HTML was live.

## Notes

- Local SQLite data file changes in `AiChatApp/chat.db` are intentionally excluded from commit/push.
