# Development Guidelines
- Follow C# coding standards (PascalCase for methods, camelCase for local variables).
- Use asynchronous programming patterns (`async/await`) for I/O operations.
- Ensure all new public endpoints are added to the appropriate `Endpoints` static class.
- Maintain consistent indentation (4 spaces).
- Optimize compilation and testing performance: Prefer incremental builds (`dotnet build` without `--no-incremental` unless necessary) and target tests precisely using filters (`dotnet test --filter`) to avoid long full runs and minimize execution time.
