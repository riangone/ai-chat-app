# Code Fix Plan

This document lists all identified issues and the exact changes needed to fix them.

---

## Fix 1 — Security: `/api/admin/restart` open to all authenticated users

**File:** `AiChatApp/Endpoints/AuthEndpoints.cs`  
**Line:** ~151

**Problem:** The restart endpoint uses `.RequireAuthorization()` (any login), not `.RequireAuthorization("AdminOnly")`. Any authenticated user can trigger a server restart.

**Fix:** Change the authorization policy on the restart endpoint.

```csharp
// BEFORE
app.MapPost("/api/admin/restart", async (ClaimsPrincipal user) => {
    ...
}).RequireAuthorization().DisableAntiforgery();

// AFTER
app.MapPost("/api/admin/restart", async (ClaimsPrincipal user) => {
    ...
}).RequireAuthorization("AdminOnly").DisableAntiforgery();
```

---

## Fix 2 — Security: Hardcoded default admin password

**File:** `AiChatApp/Extensions/ApplicationExtensions.cs`  
**Line:** ~17

**Problem:** `admin123` is hardcoded. Anyone who reads the source can log in with the default account.

**Fix:** Read the initial admin password from an environment variable, falling back to the hardcoded value only when not set (useful for local dev). Log a warning when the fallback is used.

```csharp
// BEFORE
var admin = new User { Username = "admin", PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"), DefaultProvider = "" };

// AFTER
var adminPassword = Environment.GetEnvironmentVariable("ADMIN_INITIAL_PASSWORD") ?? "admin123";
if (adminPassword == "admin123")
    app.Logger.LogWarning("Using default admin password 'admin123'. Set ADMIN_INITIAL_PASSWORD env var in production.");
var admin = new User { Username = "admin", PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword), DefaultProvider = "" };
```

---

## Fix 3 — Config key mismatch in MemoryConsolidationService

**File:** `AiChatApp/Services/MemoryConsolidationService.cs`  
**Line:** ~39

**Problem:** The service reads `_config["MemoryProvider"]` which does not exist in `appsettings.json`, so it always falls back to `"claude"`. The CLAUDE.md and intent say it should use the configured default provider (`AiSettings:DefaultProvider`). The `AiService` is already injected; use its `DefaultProvider` property instead of a separate config read.

**Fix:** Replace the standalone config read with `_aiService.DefaultProvider`.

```csharp
// BEFORE
string provider = _config["MemoryProvider"] ?? "claude";

// AFTER
string provider = _aiService.DefaultProvider;
```

Also remove the now-unused `IConfiguration _config` field and constructor parameter if it is not used elsewhere in the file.

---

## Fix 4 — Console.WriteLine replaced with ILogger in MemoryConsolidationService

**File:** `AiChatApp/Services/MemoryConsolidationService.cs`  
**Lines:** ~41, and any other `Console.WriteLine` calls in this file

**Problem:** `Console.WriteLine` bypasses the ASP.NET Core logging infrastructure (no log levels, no structured logging, no filtering).

**Fix:** Inject `ILogger<MemoryConsolidationService>` and replace all `Console.WriteLine` calls with `_logger.LogDebug` / `_logger.LogWarning`.

```csharp
// BEFORE (constructor)
public MemoryConsolidationService(AiService aiService, IConfiguration config, MemoryFileService fileService)
{
    _aiService = aiService;
    _config = config;
    _fileService = fileService;
}

// AFTER (constructor)
public MemoryConsolidationService(AiService aiService, MemoryFileService fileService, ILogger<MemoryConsolidationService> logger)
{
    _aiService = aiService;
    _fileService = fileService;
    _logger = logger;
}

// BEFORE (logging)
Console.WriteLine($"[Memory] Raw {provider} output ({rawJson.Length} chars): {rawJson[..Math.Min(300, rawJson.Length)]}");
Console.WriteLine($"[Memory] No JSON array found. start={start}, end={end}");

// AFTER (logging)
_logger.LogDebug("[Memory] Raw {Provider} output ({Length} chars): {Preview}", provider, rawJson.Length, rawJson[..Math.Min(300, rawJson.Length)]);
_logger.LogDebug("[Memory] No JSON array found. start={Start}, end={End}", start, end);
```

---

## Fix 5 — Remove dead method `LoadAgentFromDirAsync`

**File:** `AiChatApp/Services/AiService.cs`  
**Lines:** 92–120

**Problem:** `GetAvailableAgentsAsync` now fully delegates to `SkillManagerService.GetAllSkillsAsync()`. The private `LoadAgentFromDirAsync` method is no longer called anywhere and is dead code.

**Fix:** Delete the entire `LoadAgentFromDirAsync` method (lines 92–120).

---

## Fix 6 — Fire-and-forget tasks lack error handling

**File:** `AiChatApp/Services/AiService.cs`  
**Lines:** ~218, ~228, ~311, ~328

**Problem:** Multiple `_ = Task.Run(async () => { ... })` calls have no try/catch. Any exception thrown inside (e.g., DB errors, AI CLI failures) is silently swallowed and the failure is undetectable.

**Fix:** Wrap each fire-and-forget body in a try/catch that logs the exception. Inject or resolve `ILogger<AiService>` if not already present.

Example pattern to apply to all four fire-and-forget sites:

```csharp
// BEFORE
_ = Task.Run(() => _evalService.EvaluateStepAsync(step.Id, task, step.Output, targetProvider));

// AFTER
_ = Task.Run(async () => {
    try { await _evalService.EvaluateStepAsync(step.Id, task, step.Output, targetProvider); }
    catch (Exception ex) { _logger.LogError(ex, "EvaluateStepAsync failed for step {StepId}", step.Id); }
});
```

Apply the same pattern to:
- `_sessionMemory.PromoteToLongTermAsync` + `_skillLearning.LearnFromInteractionAsync` blocks in `CooperateAsync` (lines ~228, ~328)
- `_evalService.EvaluateStepAsync` calls in `CooperateAsync` (lines ~218, ~311)

`AiService` needs an `ILogger<AiService>` field. Add it to the constructor:

```csharp
// Add field
private readonly ILogger<AiService> _logger;

// Add to constructor parameter list and assignment
ILogger<AiService> logger
...
_logger = logger;
```

ServiceExtensions does not need changing because ASP.NET Core DI auto-resolves `ILogger<T>`.

---

## Fix 7 — Null safety for `stageStep` in pipeline loop

**File:** `AiChatApp/Services/AiService.cs`  
**Line:** ~246

**Problem:** `AgentStep stageStep = null!;` is a null-forgiving initializer used as a workaround. If `pipeline.Stages` is empty or all stages are skipped, `steps` will be empty and `steps.Last()` on line ~325 will throw `InvalidOperationException`.

**Fix:** Guard `steps.Last()` and handle the empty pipeline case:

```csharp
// BEFORE
string finalResult = steps.Last().Output;

// AFTER
if (!steps.Any())
    return (BuildCooperativeHtml(new List<AgentStep>(), task), steps);
string finalResult = steps.Last().Output;
```

Also rename `stageStep = null!` to use a proper nullable and restructure the inner loop so the compiler can prove it is always assigned before use:

```csharp
// BEFORE
AgentStep stageStep = null!;
...
for (int attempt = 1; attempt <= stage.MaxAttempts; attempt++)
{
    stageStep = await RunAgentStepAsync(...);
    ...
    break;
}
steps.Add(stageStep);

// AFTER
AgentStep? stageStep = null;
...
for (int attempt = 1; attempt <= stage.MaxAttempts; attempt++)
{
    stageStep = await RunAgentStepAsync(...);
    ...
    break;
}
if (stageStep is null) continue;   // skip stage if somehow never ran
steps.Add(stageStep);
```

---

## Fix 8 — Duplicate stream prefix-stripping logic

**File:** `AiChatApp/Services/AiService.cs`  
**Lines:** ~549-583 (JSON streaming path) and ~587-628 (non-JSON streaming path)

**Problem:** Both the JSON-line and raw-character streaming paths contain identical "prefix buffer" logic: accumulate into `prefixBuffer`, check against `PromptPrefixes`/`SystemPromptFragments`, strip the echoed system prompt if found, yield the cleaned chunk. This logic is copy-pasted.

**Fix:** Extract a private helper method `HandlePrefixBuffer` and call it from both paths.

```csharp
/// <summary>
/// Handles the prefix-stripping buffer for streaming output.
/// Returns (chunk to yield or null, whether prefix handling is now complete).
/// </summary>
private (string? toYield, bool handled) HandlePrefixBuffer(
    StringBuilder prefixBuffer, string chunk, bool prefixHandled, int maxBuffer)
{
    if (prefixHandled)
        return (chunk, true);

    prefixBuffer.Append(chunk);
    var buf = prefixBuffer.ToString();

    bool startsWithPrefix =
        PromptPrefixes.Any(p => buf.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ||
        SystemPromptFragments.Any(f => buf.StartsWith(f, StringComparison.OrdinalIgnoreCase));

    if (!startsWithPrefix)
    {
        prefixBuffer.Clear();
        return (buf, true);
    }

    if (buf.Contains("\nUser:") || buf.Contains("\nAssistant:") ||
        buf.Contains("\nAssistant ") || buf.Contains("\n[") ||
        buf.Length >= maxBuffer)
    {
        var stripped = StripEchoedPromptPrefix(buf);
        prefixBuffer.Clear();
        return (string.IsNullOrEmpty(stripped) ? null : stripped, true);
    }

    return (null, false); // still buffering
}
```

Then replace both streaming loops to call this helper instead of repeating the logic inline.

---

## Summary of files to modify

| File | Fixes |
|------|-------|
| `AiChatApp/Endpoints/AuthEndpoints.cs` | Fix 1 |
| `AiChatApp/Extensions/ApplicationExtensions.cs` | Fix 2 |
| `AiChatApp/Services/MemoryConsolidationService.cs` | Fix 3, Fix 4 |
| `AiChatApp/Services/AiService.cs` | Fix 5, Fix 6, Fix 7, Fix 8 |
