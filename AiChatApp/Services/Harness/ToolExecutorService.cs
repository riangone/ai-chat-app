using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AiChatApp.Services.Harness;

public class ToolExecutorService
{
    private readonly ILogger<ToolExecutorService> _logger;
    private readonly string _workingDir;

    public ToolExecutorService(ILogger<ToolExecutorService> logger)
    {
        _logger = logger;
        _workingDir = Directory.GetCurrentDirectory();
    }

    public async Task<string> ExecuteToolsAsync(string output, string? projectRoot = null)
    {
        var currentDir = projectRoot ?? _workingDir;
        var toolCallRegex = new Regex(@"<tool_call\s+name=""(?<name>[^""]+)""\s+args='(?<args>[^']+)'\s*/>", RegexOptions.IgnoreCase);
        var matches = toolCallRegex.Matches(output);

        if (matches.Count == 0) return output;

        var resultBuilder = new StringBuilder(output);
        resultBuilder.Append("\n\n[Tool Execution Results]:\n");

        foreach (Match match in matches)
        {
            var name = match.Groups["name"].Value;
            var argsJson = match.Groups["args"].Value;
            var result = await DispatchToolAsync(name, argsJson, currentDir);
            resultBuilder.Append($"- {name}: {result}\n");
        }

        return resultBuilder.ToString();
    }

    private async Task<string> DispatchToolAsync(string name, string argsJson, string workingDir)
    {
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argsJson) ?? new();

            return name.ToLower() switch
            {
                "read_file" => await ReadFileAsync(args, workingDir),
                "write_file" => await WriteFileAsync(args, workingDir),
                "list_dir" => await ListDirAsync(args, workingDir),
                "run_shell" => await RunShellAsync(args, workingDir),
                _ => $"Error: Tool '{name}' not found."
            };
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private async Task<string> ReadFileAsync(Dictionary<string, string> args, string workingDir)
    {
        if (!args.TryGetValue("path", out var path)) return "Error: Missing 'path' argument.";
        var fullPath = Path.GetFullPath(Path.Combine(workingDir, path));
        if (!fullPath.StartsWith(workingDir)) return "Error: Path outside working directory.";
        if (!File.Exists(fullPath)) return "Error: File not found.";
        return await File.ReadAllTextAsync(fullPath);
    }

    private async Task<string> WriteFileAsync(Dictionary<string, string> args, string workingDir)
    {
        if (!args.TryGetValue("path", out var path)) return "Error: Missing 'path' argument.";
        if (!args.TryGetValue("content", out var content)) return "Error: Missing 'content' argument.";
        var fullPath = Path.GetFullPath(Path.Combine(workingDir, path));
        if (!fullPath.StartsWith(workingDir)) return "Error: Path outside working directory.";
        
        var dir = Path.GetDirectoryName(fullPath);
        if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        
        await File.WriteAllTextAsync(fullPath, content);
        return $"Successfully wrote to {path}";
    }

    private Task<string> ListDirAsync(Dictionary<string, string> args, string workingDir)
    {
        var path = args.GetValueOrDefault("path", ".");
        var fullPath = Path.GetFullPath(Path.Combine(workingDir, path));
        if (!fullPath.StartsWith(workingDir)) return Task.FromResult("Error: Path outside working directory.");
        if (!Directory.Exists(fullPath)) return Task.FromResult("Error: Directory not found.");

        var entries = Directory.GetFileSystemEntries(fullPath);
        return Task.FromResult(string.Join("\n", entries.Select(e => Path.GetRelativePath(workingDir, e))));
    }

    private async Task<string> RunShellAsync(Dictionary<string, string> args, string workingDir)
    {
        if (!args.TryGetValue("command", out var command)) return "Error: Missing 'command' argument.";

        var processInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDir
        };

        using var process = System.Diagnostics.Process.Start(processInfo);
        if (process == null) return "Error: Could not start process.";

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0) return $"Error (Code {process.ExitCode}): {error}";
        return output;
    }
}
