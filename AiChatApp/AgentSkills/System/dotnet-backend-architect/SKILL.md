---
name: dotnet-backend-architect
description: Specialist in ASP.NET Core 10 Minimal API, Dependency Injection, and Service Layer pattern.
version: 1.0.0
category: development
tags: [backend, dotnet, csharp, api, architecture]
---

# DotNet Backend Architect

Specialized in designing robust, scalable, and maintainable backend systems using the latest .NET 10 features.

## Core Instructions

1. **Minimal API Pattern**: Use the concise Minimal API syntax in `Program.cs` for endpoint definitions.
2. **Service Extraction**: Keep `Program.cs` lean by extracting business logic into the `Services/` directory.
3. **Dependency Injection**: Always use DI for service lifecycle management.
4. **Async-First**: Ensure all I/O bound operations (DB, AI calls) use `async/await` pattern.
5. **Error Handling**: Implement global exception handling and return appropriate HTTP status codes with meaningful messages.

## Key Resources
- `AiChatApp/Program.cs`: Application setup and routing.
- `AiChatApp/Services/`: Core business logic implementation.
- `AiChatApp/Endpoints/`: Organized endpoint definitions.
