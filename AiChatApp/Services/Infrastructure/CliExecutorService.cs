using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using AiChatApp.Models;

namespace AiChatApp.Services.Infrastructure;

public record CliResult(string Output, string Model, int PromptTokens, int CompletionTokens, int TotalTokens);

public record StreamChunk(string? Text, string? Model, int PromptTokens, int CompletionTokens, int TotalTokens);

public interface ICliExecutor
{
    Task<CliResult> ExecuteAsync(string prompt, string provider, string? systemPrompt = null, string? userPrompt = null, string? workingDirectory = null, bool agentMode = false, string? outputFormat = null);
    IAsyncEnumerable<StreamChunk> ExecuteStreamAsync(string prompt, string provider, string? systemPrompt = null, string? userPrompt = null, string? workingDirectory = null, bool agentMode = false, string? outputFormat = null);
}

public class CliExecutorService : ICliExecutor, IDisposable
{
    private readonly ILogger<CliExecutorService> _logger;
    private readonly IConfiguration _config;
    private readonly LmStudioProvider? _lmStudio;

    // Pre-warmed processes: started but stdin not yet written, ready for the next request.
    // Key = "{provider}_{workingDir}_{outputFormat}"
    private readonly ConcurrentDictionary<string, Process> _warmPool = new();

    public CliExecutorService(ILogger<CliExecutorService> logger, IConfiguration config, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _config = config;
        try { _lmStudio = serviceProvider.GetRequiredService<LmStudioProvider>(); } catch { }
    }

    private string DefaultProvider => _config["AiSettings:DefaultProvider"] ?? "antigravity";
    private string FallbackProvider => _config["AiSettings:FallbackProvider"] ?? "antigravity";
    private int TimeoutSeconds => int.TryParse(_config["AiSettings:TimeoutSeconds"], out var s) ? s : 300;

    // Only stdin-based providers can be pre-warmed; arg-based ones need the prompt at spawn time.
    private static bool SupportsPreWarm(string normalizedProvider) =>
        normalizedProvider != "opencode" && normalizedProvider != "copilot" &&
        normalizedProvider != "antigravity" && normalizedProvider != "gemini";

    private Process? ClaimWarmProcess(string key)
    {
        if (!_warmPool.TryRemove(key, out var p)) return null;
        if (!p.HasExited) return p;
        p.Dispose();
        return null;
    }

    private void SchedulePreWarm(string key, string provider, string? workingDirectory, string? outputFormat, bool agentMode)
    {
        _ = Task.Run(() =>
        {
            try
            {
                var psi = SetupProcessInfo(provider, workingDirectory, outputFormat: outputFormat, agentMode: agentMode, persistent: false);
                var p = new Process { StartInfo = psi };
                if (!p.Start()) { p.Dispose(); return; }
                if (!_warmPool.TryAdd(key, p))
                {
                    // Slot already taken by a concurrent pre-warm — discard this one.
                    try { p.Kill(true); } catch { }
                    p.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Pre-warm failed for {Provider}", provider);
            }
        });
    }

    public async Task<CliResult> ExecuteAsync(string prompt, string provider, string? systemPrompt = null, string? userPrompt = null, string? workingDirectory = null, bool agentMode = false, string? outputFormat = null)
    {
        if (provider?.ToLowerInvariant() == "lmstudio" && _lmStudio != null)
        {
            var response = await _lmStudio.GetResponseAsync(prompt, systemPrompt ?? "");
            return new CliResult(response, "lmstudio", 0, 0, 0);
        }

        var effectiveProvider = provider ?? DefaultProvider;
        CliResult result;
        try
        {
            result = await ExecuteSingleShotAsync(prompt, effectiveProvider, systemPrompt, userPrompt, workingDirectory, agentMode, outputFormat);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Execution failed for {Provider}", provider);
            if (provider != FallbackProvider)
            {
                _logger.LogInformation("Falling back to {Fallback}", FallbackProvider);
                return await ExecuteAsync(prompt, FallbackProvider, systemPrompt, userPrompt, workingDirectory, agentMode, outputFormat);
            }
            throw;
        }

        if (result.Output.StartsWith("[Error") && provider != FallbackProvider)
        {
            _logger.LogWarning("CLI returned error for {Provider}, falling back.", provider);
            return await ExecuteAsync(prompt, FallbackProvider, systemPrompt, userPrompt, workingDirectory, agentMode, outputFormat);
        }

        return result;
    }

    public async IAsyncEnumerable<StreamChunk> ExecuteStreamAsync(string prompt, string provider, string? systemPrompt = null, string? userPrompt = null, string? workingDirectory = null, bool agentMode = false, string? outputFormat = null)
    {
        if (provider?.ToLowerInvariant() == "lmstudio" && _lmStudio != null)
        {
            await foreach (var chunk in _lmStudio.GetResponseStreamAsync(prompt, systemPrompt ?? ""))
                yield return new StreamChunk(chunk, "lmstudio", 0, 0, 0);
            yield break;
        }

        var targetProvider = (provider ?? DefaultProvider).ToLowerInvariant();
        var useJsonStreaming = targetProvider is "claude" or "claudecode" or "copilot" or "codex" or "opencode";

        var format = outputFormat ?? (useJsonStreaming ? "stream-json" : null);
        var processInfo = SetupProcessInfo(provider ?? DefaultProvider, workingDirectory, format, agentMode: agentMode, persistent: false);

        string inputToStdin = string.IsNullOrEmpty(systemPrompt)
            ? prompt
            : $"{systemPrompt}\n\n{prompt}";

        if (targetProvider == "opencode")
        {
            processInfo.ArgumentList.Add(inputToStdin);
            inputToStdin = string.Empty;
        }
        else if (targetProvider == "copilot" || targetProvider == "antigravity" || targetProvider == "gemini")
        {
            processInfo.ArgumentList.Add("-p");
            processInfo.ArgumentList.Add(inputToStdin);
            inputToStdin = string.Empty;
        }

        var warmKey = $"{targetProvider}_{workingDirectory ?? "default"}_{format ?? "raw"}";
        var warm = SupportsPreWarm(targetProvider) ? ClaimWarmProcess(warmKey) : null;

        Process process;
        if (warm != null)
        {
            _logger.LogDebug("Stream: using pre-warmed process for {Provider}", targetProvider);
            process = warm;
        }
        else
        {
            process = new Process { StartInfo = processInfo };
            if (!process.Start())
            {
                process.Dispose();
                yield break;
            }
        }

        // Kick off the next pre-warm immediately so it's ready for the following request.
        if (SupportsPreWarm(targetProvider))
            SchedulePreWarm(warmKey, provider ?? DefaultProvider, workingDirectory, format, agentMode);

        if (!string.IsNullOrEmpty(inputToStdin))
        {
            await process.StandardInput.WriteAsync(inputToStdin);
            process.StandardInput.Close();
        }

        using var cts = new CancellationTokenSource();
        _ = Task.Delay(TimeSpan.FromSeconds(TimeoutSeconds), cts.Token).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
                try { if (!process.HasExited) process.Kill(true); } catch { }
        }, TaskScheduler.Default);

        string? extractedModel = null;
        int pt = 0, ct = 0, tt = 0;

        if (useJsonStreaming)
        {
            bool hasDeltaContent = false;

            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                bool isDeltaHandled = false;
                bool skipLine = false;

                var chunksToYield = new List<StreamChunk>();
                try
                {
                    var jsonObjects = JsonUtils.ExtractAllJsonObjects(line);
                    foreach (var jsonText in jsonObjects)
                    {
                        using var doc = JsonDocument.Parse(jsonText);
                        var root = doc.RootElement;

                        string? currentChunk = null;
                        bool shouldYieldCurrent = false;

                        if (root.TryGetProperty("type", out var typePropCheck))
                        {
                            var eventType = typePropCheck.GetString();

                            if (eventType == "user")
                            {
                                skipLine = true;
                            }
                            else if (eventType == "content_block_delta" || eventType == "text_delta")
                            {
                                if (root.TryGetProperty("delta", out var deltaProp) &&
                                    deltaProp.TryGetProperty("text", out var deltaTextProp))
                                {
                                    var deltaText = deltaTextProp.GetString();
                                    if (!string.IsNullOrEmpty(deltaText))
                                    {
                                        currentChunk = deltaText;
                                        shouldYieldCurrent = true;
                                        hasDeltaContent = true;
                                    }
                                }
                                isDeltaHandled = true;
                            }
                            else if (hasDeltaContent && (eventType == "result" || eventType == "assistant.message"))
                            {
                                string? ignored = null;
                                if (TryParseJsonElement(root, ref ignored, ref extractedModel, ref pt, ref ct, ref tt, isIncremental: false))
                                    shouldYieldCurrent = true;
                                isDeltaHandled = true;
                            }
                        }

                        if (!skipLine && !isDeltaHandled)
                        {
                            if (TryParseJsonElement(root, ref currentChunk, ref extractedModel, ref pt, ref ct, ref tt, isIncremental: false))
                            {
                                if (!string.IsNullOrEmpty(currentChunk)) hasDeltaContent = true;
                                shouldYieldCurrent = true;
                            }
                        }
                        
                        if (shouldYieldCurrent)
                        {
                            chunksToYield.Add(new StreamChunk(currentChunk, extractedModel, pt, ct, tt));
                        }
                    }
                }
                catch (JsonException) { }

                foreach (var c in chunksToYield) yield return c;
            }
        }
        else
        {
            var buffer = new char[64];
            int read;
            while ((read = await process.StandardOutput.ReadAsync(buffer, 0, buffer.Length)) > 0)
                yield return new StreamChunk(new string(buffer, 0, read), null, 0, 0, 0);
        }

        try { await process.WaitForExitAsync(); } catch { }
        cts.Cancel();
        process.Dispose();
    }

    private async Task<CliResult> ExecuteSingleShotAsync(string prompt, string provider, string? systemPrompt, string? userPrompt, string? workingDirectory, bool agentMode, string? outputFormat = null)
    {
        var targetProvider = provider.ToLowerInvariant();
        var effectiveFormat = outputFormat ?? "json";
        var warmKey = $"{targetProvider}_{workingDirectory ?? "default"}_{effectiveFormat}";

        var processInfo = SetupProcessInfo(provider, workingDirectory, outputFormat: outputFormat, agentMode: agentMode, persistent: false);
        string inputToStdin = string.IsNullOrEmpty(systemPrompt) ? prompt : $"System: {systemPrompt}\n\nUser: {prompt}";

        if (targetProvider == "opencode")
        {
            processInfo.ArgumentList.Add(inputToStdin);
            inputToStdin = string.Empty;
        }
        else if (targetProvider == "copilot" || targetProvider == "antigravity" || targetProvider == "gemini")
        {
            processInfo.ArgumentList.Add("-p");
            processInfo.ArgumentList.Add(inputToStdin);
            inputToStdin = string.Empty;
        }

        var warm = SupportsPreWarm(targetProvider) ? ClaimWarmProcess(warmKey) : null;
        Process process;

        if (warm != null)
        {
            _logger.LogDebug("Using pre-warmed process for {Provider}", provider);
            process = warm;
        }
        else
        {
            process = new Process { StartInfo = processInfo };
            process.Start();
        }

        // Kick off the next pre-warm immediately — overlaps with the current AI response time.
        if (SupportsPreWarm(targetProvider))
            SchedulePreWarm(warmKey, provider, workingDirectory, effectiveFormat, agentMode);

        if (!string.IsNullOrEmpty(inputToStdin))
        {
            using var sw = process.StandardInput;
            await sw.WriteAsync(inputToStdin);
            sw.Close();
        }
        else
        {
            process.StandardInput.Close();
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(TimeoutSeconds * 1000))
        {
            try { process.Kill(true); } catch { }
            process.Dispose();
            throw new TimeoutException($"CLI process ({provider}) timed out.");
        }

        var output = await stdoutTask;
        var stderr = await stderrTask;

        var result = ParseCliOutput(output, stderr, provider, systemPrompt, userPrompt);
        process.Dispose();
        return result;
    }

    private ProcessStartInfo SetupProcessInfo(string provider, string? workingDirectory, string? outputFormat = null, bool agentMode = false, bool persistent = false)
    {
        var targetProvider = provider.ToLowerInvariant();
        string fileName = targetProvider switch
        {
            "copilot" => "copilot",
            "claudecode" => "claudecode",
            "claude" => "claude",
            "codex" => "codex",
            "opencode" => "opencode",
            "antigravity" => "antigravity",
            "gemini" => "antigravity",
            _ => (DefaultProvider.ToLowerInvariant() == "gemini" || DefaultProvider.ToLowerInvariant() == "antigravity") ? "antigravity" : DefaultProvider.ToLowerInvariant()
        };

        var processInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory()
        };

        processInfo.EnvironmentVariables["GEMINI_SANDBOX"] = "false";
        processInfo.EnvironmentVariables["GEMINI_CLI_TRUST_WORKSPACE"] = "true";
        processInfo.EnvironmentVariables["ANTIGRAVITY_SANDBOX"] = "false";
        processInfo.EnvironmentVariables["ANTIGRAVITY_CLI_TRUST_WORKSPACE"] = "true";

        if (targetProvider == "codex")
        {
            processInfo.ArgumentList.Add("exec");
            processInfo.ArgumentList.Add("--dangerously-bypass-approvals-and-sandbox");
            processInfo.ArgumentList.Add("--color");
            processInfo.ArgumentList.Add("never");
            processInfo.ArgumentList.Add("--json");
        }
        else if (targetProvider == "opencode")
        {
            processInfo.ArgumentList.Add("run");
            processInfo.ArgumentList.Add("--format");
            processInfo.ArgumentList.Add("json");
        }
        else if (targetProvider == "copilot")
        {
            processInfo.ArgumentList.Add("--allow-all-tools");
            processInfo.ArgumentList.Add("--output-format");
            processInfo.ArgumentList.Add(outputFormat ?? "json");
        }
        else if (targetProvider == "antigravity" || targetProvider == "gemini" || fileName == "antigravity")
        {
            processInfo.ArgumentList.Add("--dangerously-skip-permissions");
        }
        else
        {
            processInfo.ArgumentList.Add("-p");
            processInfo.ArgumentList.Add("");
            processInfo.ArgumentList.Add("--output-format");
            processInfo.ArgumentList.Add(outputFormat ?? "json");

            if (targetProvider == "claude" || fileName == "claude" || targetProvider == "claudecode" || fileName == "claudecode")
            {
                processInfo.ArgumentList.Add("--dangerously-skip-permissions");
                processInfo.ArgumentList.Add("--output-format");
                processInfo.ArgumentList.Add(outputFormat ?? "json");
                if (outputFormat == "stream-json") processInfo.ArgumentList.Add("--verbose");
            }
            else
            {
                processInfo.ArgumentList.Add("--yolo");

                if (targetProvider != "gemini" && fileName == "gemini")
                {
                    processInfo.ArgumentList.Add("-m");
                    processInfo.ArgumentList.Add(provider);
                }
            }
        }

        return processInfo;
    }

    private CliResult ParseCliOutput(string output, string stderr, string provider, string? systemPrompt, string? userPrompt)
    {
        if (string.IsNullOrWhiteSpace(output)) return new CliResult(string.Empty, provider, 0, 0, 0);

        string? finalContent = null;
        string? finalModel = null;
        int totalPt = 0, totalCt = 0, totalTt = 0;
        bool foundAnyJson = false;

        // Strategy 1: Try parsing the whole output as a single JSON object (common case)
        try
        {
            var trimmedOutput = output.Trim();
            if (trimmedOutput.StartsWith("{") && trimmedOutput.EndsWith("}"))
            {
                using var doc = JsonDocument.Parse(trimmedOutput);
                if (TryParseJsonElement(doc.RootElement, ref finalContent, ref finalModel, ref totalPt, ref totalCt, ref totalTt, isIncremental: true))
                {
                    foundAnyJson = true;
                }
            }
        }
        catch (JsonException) { }

        // Strategy 2: Extract all JSON objects (handles mixed text/JSON output)
        if (!foundAnyJson)
        {
            var allJsonObjects = JsonUtils.ExtractAllJsonObjects(output);
            foreach (var jsonText in allJsonObjects)
            {
                try
                {
                    using var doc = JsonDocument.Parse(jsonText);
                    if (TryParseJsonElement(doc.RootElement, ref finalContent, ref finalModel, ref totalPt, ref totalCt, ref totalTt, isIncremental: true))
                    {
                        foundAnyJson = true;
                    }
                }
                catch (JsonException) { }
            }
        }

        if (!foundAnyJson)
        {
            // Fallback for non-standard formats or cases where ExtractAllJsonObjects might have missed something
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var jsonText = JsonUtils.ExtractJson(line);
                if (string.IsNullOrEmpty(jsonText) || jsonText == "{}") continue;
                try
                {
                    using var doc = JsonDocument.Parse(jsonText);
                    if (TryParseJsonElement(doc.RootElement, ref finalContent, ref finalModel, ref totalPt, ref totalCt, ref totalTt, isIncremental: true))
                        foundAnyJson = true;
                }
                catch (JsonException) { }
            }
        }

        if (totalTt == 0 && (totalPt > 0 || totalCt > 0)) totalTt = totalPt + totalCt;

        string? displayContent = finalContent;
        if (displayContent == null)
        {
            displayContent = foundAnyJson
                ? "[Error: Could not extract content from AI response]"
                : output.Trim();
        }

        var cleanedOutput = CleanResponse(displayContent ?? string.Empty, systemPrompt, userPrompt);
        return new CliResult(cleanedOutput, finalModel ?? provider, totalPt, totalCt, totalTt);
    }

    private bool TryParseJsonElement(JsonElement root, ref string? content, ref string? model, ref int pt, ref int ct, ref int tt, bool isIncremental = false)
    {
        bool foundSomething = false;

        if (root.TryGetProperty("model", out var mProp) || root.TryGetProperty("modelVersion", out mProp))
        {
            var mv = mProp.GetString();
            if (!string.IsNullOrEmpty(mv)) { model = mv; foundSomething = true; }
        }

        if (root.TryGetProperty("type", out var tProp) && tProp.GetString() == "session.tools_updated")
        {
            if (root.TryGetProperty("data", out var d) && d.TryGetProperty("model", out var m))
            {
                var mv = m.GetString();
                if (!string.IsNullOrEmpty(mv)) { model = mv; foundSomething = true; }
            }
        }

        if (root.TryGetProperty("modelUsage", out var muEl) && muEl.ValueKind == JsonValueKind.Object)
        {
            foundSomething = true;
            foreach (var prop in muEl.EnumerateObject())
            {
                if (prop.Value.TryGetProperty("input_tokens", out var mit) || prop.Value.TryGetProperty("inputTokens", out mit)) { if (isIncremental) pt += mit.GetInt32(); else pt = mit.GetInt32(); }
                if (prop.Value.TryGetProperty("output_tokens", out var mot) || prop.Value.TryGetProperty("outputTokens", out mot)) { if (isIncremental) ct += mot.GetInt32(); else ct = mot.GetInt32(); }
                if (prop.Value.TryGetProperty("total_tokens", out var mtt)) { if (isIncremental) tt += mtt.GetInt32(); else tt = mtt.GetInt32(); }
                if (prop.Value.TryGetProperty("cacheReadInputTokens", out var crit)) { pt += crit.GetInt32(); }
                if (prop.Value.TryGetProperty("cacheCreationInputTokens", out var ccit)) { pt += ccit.GetInt32(); }
            }
        }

        JsonElement usageProp;
        bool hasUsage = root.TryGetProperty("usage", out usageProp) ||
                        root.TryGetProperty("usage_metadata", out usageProp) ||
                        root.TryGetProperty("usageMetadata", out usageProp) ||
                        root.TryGetProperty("tokens", out usageProp);

        if (!hasUsage && root.TryGetProperty("part", out var partEl) && partEl.TryGetProperty("tokens", out usageProp))
            hasUsage = true;

        if (!hasUsage && root.TryGetProperty("message", out var msg) && msg.TryGetProperty("usage", out usageProp))
            hasUsage = true;

        if (!hasUsage && root.TryGetProperty("data", out var dataEl) && dataEl.TryGetProperty("outputTokens", out var otProp))
        {
            if (isIncremental) ct += otProp.GetInt32(); else ct = otProp.GetInt32();
            foundSomething = true;
        }

        if (!hasUsage && root.TryGetProperty("stats", out var sEl) && sEl.TryGetProperty("models", out var modelsEl))
        {
            foreach (var m in modelsEl.EnumerateObject())
            {
                if (m.Value.TryGetProperty("tokens", out var modelTokens))
                {
                    foundSomething = true;
                    if (modelTokens.TryGetProperty("prompt", out var p) || modelTokens.TryGetProperty("input", out p)) pt += p.GetInt32();
                    if (modelTokens.TryGetProperty("candidates", out var c) || modelTokens.TryGetProperty("output", out c)) ct += c.GetInt32();
                    if (modelTokens.TryGetProperty("total", out var t)) tt += t.GetInt32();
                }
            }
        }

        if (hasUsage)
        {
            foundSomething = true;
            if (usageProp.TryGetProperty("input_tokens", out var it) || usageProp.TryGetProperty("prompt_tokens", out it) || usageProp.TryGetProperty("prompt_token_count", out it) || usageProp.TryGetProperty("promptTokenCount", out it) || usageProp.TryGetProperty("inputTokenCount", out it) || usageProp.TryGetProperty("input", out it))
            { if (isIncremental) pt += it.GetInt32(); else pt = it.GetInt32(); }

            if (usageProp.TryGetProperty("output_tokens", out var ot) || usageProp.TryGetProperty("completion_tokens", out ot) || usageProp.TryGetProperty("completion_token_count", out ot) || usageProp.TryGetProperty("candidate_token_count", out ot) || usageProp.TryGetProperty("candidatesTokenCount", out ot) || usageProp.TryGetProperty("candidateTokenCount", out ot) || usageProp.TryGetProperty("outputTokenCount", out ot) || usageProp.TryGetProperty("output", out ot))
            { if (isIncremental) ct += ot.GetInt32(); else ct = ot.GetInt32(); }

            if (usageProp.TryGetProperty("total_tokens", out var tot) || usageProp.TryGetProperty("total_token_count", out tot) || usageProp.TryGetProperty("totalTokenCount", out tot) || usageProp.TryGetProperty("total", out tot))
            { if (isIncremental) tt += tot.GetInt32(); else tt = tot.GetInt32(); }

            if (usageProp.TryGetProperty("cache_read_input_tokens", out var crit2)) { pt += crit2.GetInt32(); }
            if (usageProp.TryGetProperty("cache_creation_input_tokens", out var ccit2)) { pt += ccit2.GetInt32(); }
        }

        if (root.TryGetProperty("stats", out var statsEl))
        {
            foundSomething = true;
            if (statsEl.TryGetProperty("input_tokens", out var sit)) { if (isIncremental) pt += sit.GetInt32(); else pt = sit.GetInt32(); }
            if (statsEl.TryGetProperty("output_tokens", out var sot)) { if (isIncremental) ct += sot.GetInt32(); else ct = sot.GetInt32(); }
            if (statsEl.TryGetProperty("total_tokens", out var stt)) { if (isIncremental) tt += stt.GetInt32(); else tt = stt.GetInt32(); }
        }

        bool foundContent = false;
        if (root.TryGetProperty("type", out var typeProp))
        {
            var t = typeProp.GetString();
            if (t == "text" && root.TryGetProperty("part", out var op) && op.TryGetProperty("text", out var otText))
                { content = (content ?? "") + otText.GetString(); foundContent = true; }
            else if (t == "assistant.message" && root.TryGetProperty("content", out var amc))
                { content = (content ?? "") + amc.GetString(); foundContent = true; }
            else if (t == "message" && root.TryGetProperty("content", out var mMsg) &&
                     (!root.TryGetProperty("role", out var roleProp) || roleProp.GetString() != "user"))
                { content = (content ?? "") + mMsg.GetString(); foundContent = true; }
            else if (t == "assistant.message_delta" && root.TryGetProperty("data", out var dd) && dd.TryGetProperty("deltaContent", out var dc))
                { content = (content ?? "") + dc.GetString(); foundContent = true; }
            else if (t == "result" && root.TryGetProperty("result", out var rc))
                { content = rc.GetString(); foundContent = true; }
            else if (t == "item.completed" && root.TryGetProperty("item", out var itemProp) &&
                     itemProp.TryGetProperty("text", out var itemText))
                { content = (content ?? "") + itemText.GetString(); foundContent = true; }
        }

        if (!foundContent)
        {
            if (root.TryGetProperty("response", out var res)) { content = (content ?? "") + res.GetString(); foundContent = true; }
            else if (root.TryGetProperty("content", out var res3) && res3.ValueKind == JsonValueKind.String) { content = (content ?? "") + res3.GetString(); foundContent = true; }
            else if (root.TryGetProperty("text", out var res4)) { content = (content ?? "") + res4.GetString(); foundContent = true; }
            else if (root.TryGetProperty("data", out var dataProp) && dataProp.TryGetProperty("content", out var dataContent)) { content = (content ?? "") + dataContent.GetString(); foundContent = true; }
            else if (root.TryGetProperty("message", out var msgProp) && msgProp.TryGetProperty("content", out var msgContent)) { content = (content ?? "") + msgContent.GetString(); foundContent = true; }

            // Gemini API native format: candidates[0].content.parts[*].text
            if (!foundContent && root.TryGetProperty("candidates", out var candidatesEl) &&
                candidatesEl.ValueKind == JsonValueKind.Array && candidatesEl.GetArrayLength() > 0)
            {
                var firstCandidate = candidatesEl[0];
                if (firstCandidate.TryGetProperty("content", out var candidateContent) &&
                    candidateContent.TryGetProperty("parts", out var partsEl) &&
                    partsEl.ValueKind == JsonValueKind.Array)
                {
                    var sb = new StringBuilder();
                    foreach (var part in partsEl.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var partText))
                        {
                            var txt = partText.GetString();
                            if (!string.IsNullOrEmpty(txt)) sb.Append(txt);
                        }
                    }
                    if (sb.Length > 0) { content = (content ?? "") + sb.ToString(); foundContent = true; }
                }
            }
        }

        if (root.TryGetProperty("error", out var errProp))
        {
            var errMsg = errProp.ValueKind == JsonValueKind.String ? errProp.GetString() :
                         errProp.TryGetProperty("message", out var em) ? em.GetString() : errProp.ToString();
            if (!string.IsNullOrEmpty(errMsg)) { content = $"[Error: {errMsg}]"; foundContent = true; }
        }

        return foundSomething || foundContent;
    }

    private string CleanResponse(string text, string? systemPrompt, string? userPrompt)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var timeout = TimeSpan.FromSeconds(1);
        text = Regex.Replace(text, @"<(thinking|thought|thought_process)>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase, timeout);
        text = Regex.Replace(text, @"<(thinking|thought|thought_process)>.*", "", RegexOptions.Singleline | RegexOptions.IgnoreCase, timeout);
        text = Regex.Replace(text, @"(^(Thought|Thinking|Reasoning):.*?\n\n)", "", RegexOptions.Singleline | RegexOptions.IgnoreCase, timeout);
        text = Regex.Replace(text, @"\n\n(Thought|Thinking|Reasoning):.*?\n\n", "\n\n", RegexOptions.Singleline | RegexOptions.IgnoreCase, timeout);
        text = Regex.Replace(text, @"(Thought|Thinking|Reasoning):.*?$", "", RegexOptions.Singleline | RegexOptions.IgnoreCase, timeout);
        
        // 过滤 antigravity cli 生成的工作总结/サマリー/Summary (bold + --- separator format)
        text = Regex.Replace(text, @"\n---\s*\n\*\*(工作总结|工作サマリー|工作概要|Summary of Work|Task Summary|Work Summary|Summary)\*\*：?[\s\S]*$", "", RegexOptions.IgnoreCase, timeout);
        // Heading format: "### 工作总结" (no --- separator)
        text = Regex.Replace(text, @"\n+#{1,4}\s*(工作总结|工作サマリー|工作概要|Summary of Work|Task Summary|Work Summary|Summary)\s*\n[\s\S]*$", "", RegexOptions.IgnoreCase, timeout);
        return text.Trim();
    }

    public void Dispose()
    {
        foreach (var p in _warmPool.Values)
        {
            try { p.Kill(true); } catch { }
            p.Dispose();
        }
        _warmPool.Clear();
    }
}
