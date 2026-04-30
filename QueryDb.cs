using System;
using System.Linq;
using AiChatApp.Data;
using AiChatApp.Models;
using Microsoft.EntityFrameworkCore;

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite("Data Source=AiChatApp/chat.db")
    .Options;

using var db = new AppDbContext(options);

// Check AgentSteps with copilot in Model field
var copilotSteps = db.AgentSteps
    .Where(s => s.Model != null && s.Model.ToLower().Contains("copilot"))
    .ToList();

Console.WriteLine($"=== AgentSteps where Model contains 'copilot' ({copilotSteps.Count} records) ===");
Console.WriteLine($"{"Id",-5} | {"Model",-25} | {"PT",-6} | {"CT",-6} | {"TT",-6}");
Console.WriteLine(new string('-', 70));

foreach (var s in copilotSteps.Take(30)) {
    Console.WriteLine($"{s.Id,-5} | {s.Model,-25} | {s.PromptTokens,-6} | {s.CompletionTokens,-6} | {s.TotalTokens,-6}");
}

// Check all unique Model values and their token sums
Console.WriteLine("\n=== All unique Model values in AgentSteps ===");
var allModels = db.AgentSteps
    .Where(s => !string.IsNullOrEmpty(s.Model))
    .Select(s => s.Model)
    .Distinct()
    .ToList();

foreach (var m in allModels) {
    var count = db.AgentSteps.Count(s => s.Model == m);
    var totalTokens = db.AgentSteps.Where(s => s.Model == m).Sum(s => s.TotalTokens);
    var totalPT = db.AgentSteps.Where(s => s.Model == m).Sum(s => s.PromptTokens);
    var totalCT = db.AgentSteps.Where(s => s.Model == m).Sum(s => s.CompletionTokens);
    Console.WriteLine($"Model='{m}', Count={count}, PT={totalPT}, CT={totalCT}, TT={totalTokens}");
}

// Check steps that might be from copilot but have empty Model
var messagesWithCopilot = db.Messages
    .Join(db.ChatSessions, m => m.ChatSessionId, s => s.Id, (m, s) => new { Message = m, Session = s })
    .Where(x => x.Session.PreferredProvider == "copilot")
    .Select(x => x.Message.Id)
    .ToList();

var stepsFromCopilotSessions = db.AgentSteps
    .Where(s => messagesWithCopilot.Contains(s.MessageId))
    .ToList();

Console.WriteLine($"\n=== Steps from copilot sessions (by PreferredProvider) ({stepsFromCopilotSessions.Count} records) ===");
Console.WriteLine($"{"Id",-5} | {"Model",-25} | {"PT",-6} | {"CT",-6} | {"TT",-6} | {"Role",-15}");
foreach (var s in stepsFromCopilotSessions.Take(30)) {
    Console.WriteLine($"{s.Id,-5} | {s.Model,-25} | {s.PromptTokens,-6} | {s.CompletionTokens,-6} | {s.TotalTokens,-6} | {s.Role,-15}");
}
