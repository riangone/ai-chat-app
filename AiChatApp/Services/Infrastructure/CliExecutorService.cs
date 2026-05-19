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
    Task<CliResult> ExecuteAsync(string prompt, string provider, string? systemPrompt = null, string? userPrompt = null, string? workingDirectory = null, bool agentMode = false);
    IAsyncEnumerable<StreamChunk> ExecuteStreamAsync(string prompt, string provider, string? systemPrompt = null, string? userPrompt = null, string? workingDirectory = null, bool agentMode = false);
}

public class CliExecutorService : ICliExecutor, IDisposable
{
    private readonly ILogger<CliExecutorService> _logger;
    private readonly IConfiguration _config;
    private readonly ConcurrentDictionary<string, ProcessHandle> _persistentProcesses = new();
    private readonly SemaphoreSlim _poolLock = new(1, 1);

    private class ProcessHandle : IDisposable
    {
        public Process Process { get; }
        public StreamWriter Input { get; }
        public SemaphoreSlim Lock { get; } = new(1, 1);
        public DateTime LastUsed { get; set; } = DateTime.UtcNow;
        public StringBuilder OutputBuilder { get; } = new();
        public StringBuilder ErrorBuilder { get; } = new();
        public bool IsPersistent { get; set; }

        public ProcessHandle(Process process, bool persistent)
        {
            Process = process;
            Input = process.StandardInput;
            IsPersistent = persistent;
            process.OutputDataReceived += (s, e) => { if (e.Data != null) lock(OutputBuilder) OutputBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock(ErrorBuilder) ErrorBuilder.AppendLine(e.Data); };
        }

        public void Dispose()
        {
            try { Process.Kill(true); } catch { }
            Process.Dispose();
        }
    }

    public CliExecutorService(ILogger<CliExecutorService> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    private string DefaultProvider => _config["AiSettings:DefaultProvider"] ?? "gemini";
    private string FallbackProvider => _config["AiSettings:FallbackProvider"] ?? "gemini";
    private int TimeoutSeconds => int.TryParse(_config["AiSettings:TimeoutSeconds"], out var s) ? s : 300;

    public async Task<CliResult> ExecuteAsync(string prompt, string provider, string? systemPrompt = null, string? userPrompt = null, string? workingDirectory = null, bool agentMode = false)
    {
        bool usePersistence = _config.GetValue<bool>("AiSettings:CliPersistenceEnabled", false); // Disabled by default due to CLI constraints
        
        // Both gemini and claude buffer stdin until EOF or require complex TUI/JSON-RPC protocols in persistent mode.
        // Single-shot is the only reliable way to interact with them via standard pipes.
        bool supportsPersistence = false; 

        CliResult result;
        try
        {
            if (usePersistence && supportsPersistence)
            {
                result = await ExecutePersistentAsync(prompt, provider, systemPrompt, userPrompt, workingDirectory, agentMode);
            }
            else
            {
                result = await ExecuteSingleShotAsync(prompt, provider, systemPrompt, userPrompt, workingDirectory, agentMode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Execution failed for {Provider}", provider);
            if (provider != FallbackProvider)
            {
                _logger.LogInformation("Falling back to {Fallback}", FallbackProvider);
                return await ExecuteAsync(prompt, FallbackProvider, systemPrompt, userPrompt, workingDirectory, agentMode);
            }
            throw;
        }

        if (result.Output.StartsWith("[Error") && provider != FallbackProvider)
        {
            _logger.LogWarning("CLI returned error for {Provider}, falling back.", provider);
            return await ExecuteAsync(prompt, FallbackProvider, systemPrompt, userPrompt, workingDirectory, agentMode);
        }

        return result;
    }

    public async IAsyncEnumerable<StreamChunk> ExecuteStreamAsync(string prompt, string provider, string? systemPrompt = null, string? userPrompt = null, string? workingDirectory = null, bool agentMode = false)
    {
        var targetProvider = provider ?? DefaultProvider;
        var useJsonStreaming = targetProvider == "gemini" || targetProvider == "claude" || targetProvider == "copilot" || targetProvider == "codex" || targetProvider == "opencode";
        
        var processInfo = SetupProcessInfo(targetProvider, workingDirectory, useJsonStreaming ? "stream-json" : null, agentMode: agentMode, persistent: false);

        string inputToStdin = string.IsNullOrEmpty(systemPrompt)
            ? prompt
            : $"{systemPrompt}\n\n{prompt}";

        if (targetProvider == "opencode")
        {
            processInfo.ArgumentList.Add(inputToStdin);
            inputToStdin = string.Empty;
        }
        else if (targetProvider == "copilot")
        {
            processInfo.ArgumentList.Add("-p");
            processInfo.ArgumentList.Add(inputToStdin);
            inputToStdin = string.Empty;
        }

        using var process = new Process { StartInfo = processInfo };
        if (!process.Start()) yield break;

        if (!string.IsNullOrEmpty(inputToStdin))
        {
            await process.StandardInput.WriteAsync(inputToStdin);
            process.StandardInput.Close();
        }

        using var cts = new CancellationTokenSource();
        _ = Task.Delay(TimeSpan.FromSeconds(TimeoutSeconds), cts.Token).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
            }
        }, TaskScheduler.Default);

        string? extractedModel = null;
        int pt = 0, ct = 0, tt = 0;

        if (useJsonStreaming)
        {
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string? chunk = null;
                bool shouldYield = false;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (TryParseJsonElement(root, ref chunk, ref extractedModel, ref pt, ref ct, ref tt, isIncremental: false))
                    {
                        // Text extraction — special handling for deltas in streaming
                        if (root.TryGetProperty("type", out var typeProp))
                        {
                            var type = typeProp.GetString();
                            if (type == "content_block_delta" || type == "text_delta")
                            {
                                if (root.TryGetProperty("delta", out var deltaProp) &&
                                    deltaProp.TryGetProperty("text", out var textProp))
                                    chunk = textProp.GetString() ?? "";
                            }
                        }
                        
                        shouldYield = true;
                    }
                }
                catch (JsonException) { }
                
                if (shouldYield)
                {
                    yield return new StreamChunk(chunk, extractedModel, pt, ct, tt);
                }
            }
        }
        else
        {
            var buffer = new char[64];
            int read;
            while ((read = await process.StandardOutput.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                yield return new StreamChunk(new string(buffer, 0, read), null, 0, 0, 0);
            }
        }

        try { await process.WaitForExitAsync(); } catch { }
        cts.Cancel();
    }

    private async Task<CliResult> ExecutePersistentAsync(string prompt, string provider, string? systemPrompt, string? userPrompt, string? workingDirectory, bool agentMode)
    {
        var key = $"{provider}_{workingDirectory ?? "default"}";
        ProcessHandle? handle;
        if (!_persistentProcesses.TryGetValue(key, out handle) || handle.Process.HasExited)
        {
            await _poolLock.WaitAsync();
            try
            {
                if (!_persistentProcesses.TryGetValue(key, out handle) || handle.Process.HasExited)
                {
                    var psi = SetupProcessInfo(provider, workingDirectory, agentMode: agentMode, persistent: true);
                    var process = new Process { StartInfo = psi };
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    handle = new ProcessHandle(process, true);
                    _persistentProcesses[key] = handle;
                }
            }
            finally { _poolLock.Release(); }
        }

        await handle.Lock.WaitAsync();
        try
        {
            handle.LastUsed = DateTime.UtcNow;
            lock (handle.OutputBuilder) handle.OutputBuilder.Clear();
            lock (handle.ErrorBuilder) handle.ErrorBuilder.Clear();

            string input = string.IsNullOrEmpty(systemPrompt) ? prompt : $"System: {systemPrompt}\n\nUser: {prompt}";
            await handle.Input.WriteLineAsync(input);
            await handle.Input.FlushAsync();

            return await PollForResponseAsync(handle, provider, systemPrompt, userPrompt);
        }
        finally { handle.Lock.Release(); }
    }

    private async Task<CliResult> PollForResponseAsync(ProcessHandle handle, string provider, string? systemPrompt, string? userPrompt)
    {
        var start = DateTime.UtcNow;
        while ((DateTime.UtcNow - start).TotalSeconds < TimeoutSeconds)
        {
            string currentOutput;
            lock (handle.OutputBuilder) currentOutput = handle.OutputBuilder.ToString();

            if (!string.IsNullOrEmpty(currentOutput))
            {
                var json = JsonUtils.ExtractJson(currentOutput);
                if (json != "{}" && json.EndsWith("}"))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(json);
                        string stderr;
                        lock (handle.ErrorBuilder) stderr = handle.ErrorBuilder.ToString();
                        return ParseCliOutput(currentOutput, stderr, provider, systemPrompt, userPrompt);
                    }
                    catch { }
                }
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"Persistent CLI ({provider}) response timeout.");
    }

    private async Task<CliResult> ExecuteSingleShotAsync(string prompt, string provider, string? systemPrompt, string? userPrompt, string? workingDirectory, bool agentMode)
    {
        var processInfo = SetupProcessInfo(provider, workingDirectory, agentMode: agentMode, persistent: false);
        string inputToStdin = string.IsNullOrEmpty(systemPrompt) ? prompt : $"System: {systemPrompt}\n\nUser: {prompt}";

        using var process = new Process { StartInfo = processInfo };
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (provider == "opencode")
        {
            // opencode doesn't read from stdin easily in some versions, pass as arg
            // but for consistency we use the same SetupProcessInfo logic as ExecuteStreamAsync
        }
        else if (provider == "copilot")
        {
            // similar for copilot
        }
        else
        {
            using (var sw = process.StandardInput) await sw.WriteAsync(inputToStdin);
        }

        if (!process.WaitForExit(TimeoutSeconds * 1000))
        {
            try { process.Kill(true); } catch { }
            throw new TimeoutException($"CLI process ({provider}) timed out.");
        }

        return ParseCliOutput(outputBuilder.ToString(), errorBuilder.ToString(), provider, systemPrompt, userPrompt);
    }

    private ProcessStartInfo SetupProcessInfo(string provider, string? workingDirectory, string? outputFormat = null, bool agentMode = false, bool persistent = false)
    {
        string fileName = provider.ToLower() switch
        {
            "copilot" => "copilot",
            "claudecode" => "claudecode",
            "claude" => "claude",
            "codex" => "codex",
            "opencode" => "opencode",
            "gemini" => "gemini",
            _ => DefaultProvider
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

        if (provider == "codex")
        {
            processInfo.ArgumentList.Add("exec");
            processInfo.ArgumentList.Add("--dangerously-bypass-approvals-and-sandbox");
            processInfo.ArgumentList.Add("--color");
            processInfo.ArgumentList.Add("never");
            processInfo.ArgumentList.Add("--json");
        }
        else if (provider == "opencode")
        {
            processInfo.ArgumentList.Add("run");
            processInfo.ArgumentList.Add("--format");
            processInfo.ArgumentList.Add("json");
        }
        else if (provider == "copilot")
        {
            processInfo.ArgumentList.Add("--allow-all-tools");
            processInfo.ArgumentList.Add("--output-format");
            processInfo.ArgumentList.Add(outputFormat ?? "json");
        }
        else
        {
            processInfo.ArgumentList.Add("-p");
            processInfo.ArgumentList.Add(""); 
            processInfo.ArgumentList.Add("--output-format");
            processInfo.ArgumentList.Add(outputFormat ?? "json");

            if (provider == "claude" || fileName == "claude" || provider == "claudecode" || fileName == "claudecode")
            {
                processInfo.ArgumentList.Add("--dangerously-skip-permissions");
                processInfo.ArgumentList.Add("--output-format");
                processInfo.ArgumentList.Add(outputFormat ?? "json");
                if (outputFormat == "stream-json") processInfo.ArgumentList.Add("--verbose");
            }
            else
            {
                processInfo.ArgumentList.Add("--yolo");

                if (provider == "gemini" || fileName == "gemini")
                {
                    if (provider.ToLower() != "gemini")
                    {
                        processInfo.ArgumentList.Add("-m");
                        processInfo.ArgumentList.Add(provider);
                    }
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

        var fullJsonText = JsonUtils.ExtractJson(output);
        if (!string.IsNullOrEmpty(fullJsonText) && fullJsonText != "{}")
        {
            try
            {
                using var doc = JsonDocument.Parse(fullJsonText);
                if (TryParseJsonElement(doc.RootElement, ref finalContent, ref finalModel, ref totalPt, ref totalCt, ref totalTt))
                    foundAnyJson = true;
            }
            catch (JsonException) { }
        }

        if (!foundAnyJson || (totalTt == 0 && string.IsNullOrEmpty(finalContent)))
        {
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

        // If JSON was found but content wasn't extracted, don't fall back to raw JSON output
        string? displayContent = finalContent;
        if (displayContent == null && !foundAnyJson)
            displayContent = output.Trim(); // plain text fallback (non-JSON providers)

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
                        root.TryGetProperty("usageMetadata", out usageProp);
        
        if (!hasUsage && root.TryGetProperty("message", out var msg) && msg.TryGetProperty("usage", out usageProp))
            hasUsage = true;
        
        if (!hasUsage && root.TryGetProperty("data", out var dataEl) && dataEl.TryGetProperty("outputTokens", out var otProp))
        {
            if (isIncremental) ct += otProp.GetInt32(); else ct = otProp.GetInt32();
            foundSomething = true;
        }

        if (hasUsage)
        {
            foundSomething = true;
            if (usageProp.TryGetProperty("input_tokens", out var it) || usageProp.TryGetProperty("prompt_tokens", out it) || usageProp.TryGetProperty("prompt_token_count", out it) || usageProp.TryGetProperty("promptTokenCount", out it) || usageProp.TryGetProperty("inputTokenCount", out it)) 
            { if (isIncremental) pt += it.GetInt32(); else pt = it.GetInt32(); }
            
            if (usageProp.TryGetProperty("output_tokens", out var ot) || usageProp.TryGetProperty("completion_tokens", out ot) || usageProp.TryGetProperty("completion_token_count", out ot) || usageProp.TryGetProperty("candidate_token_count", out ot) || usageProp.TryGetProperty("candidatesTokenCount", out ot) || usageProp.TryGetProperty("candidateTokenCount", out ot) || usageProp.TryGetProperty("outputTokenCount", out ot)) 
            { if (isIncremental) ct += ot.GetInt32(); else ct = ot.GetInt32(); }
            
            if (usageProp.TryGetProperty("total_tokens", out var tot) || usageProp.TryGetProperty("total_token_count", out tot) || usageProp.TryGetProperty("totalTokenCount", out tot))
            { if (isIncremental) tt += tot.GetInt32(); else tt = tot.GetInt32(); }

            if (usageProp.TryGetProperty("cache_read_input_tokens", out var crit)) { pt += crit.GetInt32(); }
            if (usageProp.TryGetProperty("cache_creation_input_tokens", out var ccit)) { pt += ccit.GetInt32(); }
        }

        if (root.TryGetProperty("stats", out var statsEl))
        {
            foundSomething = true;
            if (statsEl.TryGetProperty("input_tokens", out var sit)) { if (isIncremental) pt += sit.GetInt32(); else pt = sit.GetInt32(); }
            if (statsEl.TryGetProperty("output_tokens", out var sot)) { if (isIncremental) ct += sot.GetInt32(); else ct = sot.GetInt32(); }
            if (statsEl.TryGetProperty("total_tokens", out var stt)) { if (isIncremental) tt += stt.GetInt32(); else tt = stt.GetInt32(); }
        }

        if (root.TryGetProperty("type", out var typeProp))
        {
            foundSomething = true;
            var t = typeProp.GetString();
            if (t == "text" && root.TryGetProperty("part", out var op) && op.TryGetProperty("text", out var otText))
                content = (content ?? "") + otText.GetString();
            else if (t == "assistant.message" && root.TryGetProperty("content", out var amc))
                content = (content ?? "") + amc.GetString();
            else if (t == "message" && root.TryGetProperty("content", out var mMsg) &&
                     (!root.TryGetProperty("role", out var roleProp) || roleProp.GetString() != "user"))
                content = (content ?? "") + mMsg.GetString();
            else if (t == "assistant.message_delta" && root.TryGetProperty("data", out var dd) && dd.TryGetProperty("deltaContent", out var dc))
                content = (content ?? "") + dc.GetString();
            else if (t == "result" && root.TryGetProperty("result", out var rc))
                content = rc.GetString();
        }
        else if (root.TryGetProperty("response", out var res)) { content = (content ?? "") + res.GetString(); foundSomething = true; }
        else if (root.TryGetProperty("content", out var res3)) { content = (content ?? "") + res3.GetString(); foundSomething = true; }
        else if (root.TryGetProperty("text", out var res4)) { content = (content ?? "") + res4.GetString(); foundSomething = true; }
        else if (root.TryGetProperty("error", out var errProp))
        {
            var errMsg = errProp.ValueKind == JsonValueKind.String ? errProp.GetString() :
                         errProp.TryGetProperty("message", out var em) ? em.GetString() : errProp.ToString();
            if (!string.IsNullOrEmpty(errMsg)) { content = $"[Error: {errMsg}]"; foundSomething = true; }
        }

        return foundSomething;
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
        return text.Trim();
    }

    public void Dispose()
    {
        foreach (var handle in _persistentProcesses.Values) handle.Dispose();
        _persistentProcesses.Clear();
        _poolLock.Dispose();
    }
}
