using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using AiChatApp.Data;
using AiChatApp.Models;

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite("Data Source=AiChatApp/chat.db")
    .Options;

using var db = new AppDbContext(options);

// Check AgentSteps with copilot in Model field
var copilotSteps = db.AgentSteps
    .Where(s => s.Model != null && s.Model.ToLower().Contains("copilot"))
    .ToList();

Console.WriteLine($"=== AgentSteps where Model contains 'copilot' ({copilotSteps.Count} records) ===");
Console.WriteLine($"{"Id",-5} | {"Model",-25} | {"PT",-6} | {"CT",-6} | {"TT",-6} | {"Output (first 80 chars)",-80}");
Console.WriteLine(new string('-', 140));

foreach (var s in copilotSteps.Take(30)) {
    string output = s.Output?.Length > 80 ? s.Output.Substring(0, 77) + "..." : s.Output?.Replace("\n", " ").Replace("\r", "");
    Console.WriteLine($"{s.Id,-5} | {s.Model,-25} | {s.PromptTokens,-6} | {s.CompletionTokens,-6} | {s.TotalTokens,-6} | {output}");
}

// Also check steps with empty Model but from copilot sessions
var emptyModelSteps = db.AgentSteps
    .Where(s => string.IsNullOrEmpty(s.Model))
    .Join(db.Messages, step => step.MessageId, msg => msg.Id, (step, msg) => new { Step = step, Msg = msg })
    .Join(db.ChatSessions, x => x.Msg.ChatSessionId, sess => sess.Id, (x, sess) => new { x.Step, SessionProvider = sess.PreferredProvider })
    .Where(x => x.SessionProvider == "copilot")
    .Select(x => x.Step)
    .ToList();

Console.WriteLine($"\n=== AgentSteps with empty Model from copilot sessions ({emptyModelSteps.Count} records) ===");
foreach (var s in emptyModelSteps.Take(20)) {
    Console.WriteLine($"Id={s.Id}, Model='{s.Model}', PT={s.PromptTokens}, CT={s.CompletionTokens}, TT={s.TotalTokens}, Role={s.Role}");
}

// Check all unique Model values
Console.WriteLine("\n=== All unique Model values in AgentSteps ===");
var allModels = db.AgentSteps
    .Where(s => !string.IsNullOrEmpty(s.Model))
    .Select(s => s.Model)
    .Distinct()
    .ToList();
foreach (var m in allModels) {
    var count = db.AgentSteps.Count(s => s.Model == m);
    var totalTokens = db.AgentSteps.Where(s => s.Model == m).Sum(s => s.TotalTokens);
    Console.WriteLine($"Model='{m}', Count={count}, TotalTokens={totalTokens}");
}
