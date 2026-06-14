using System.Text.RegularExpressions;
using AiChatApp.Data;
using AiChatApp.Models.Harness;
using Microsoft.EntityFrameworkCore;

namespace AiChatApp.Services.Harness;

/// <summary>
/// Loop 2: プロンプトテンプレート進化（champion/challenger A/B）。
/// EvalService の評点を適応度として、低調なテンプレートの変体を LLM に生成させ、
/// 実トラフィックで比較し昇格/ロールバックする。ベースラインファイルは決して変更しない。
/// docs/SELF_EVOLUTION_DESIGN.md §3 参照。
/// </summary>
public class PromptEvolutionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PipelineLoaderService _pipelineLoader;
    private readonly IConfiguration _config;
    private readonly ILogger<PromptEvolutionService> _logger;

    private bool Enabled => _config.GetValue<bool>("AiSettings:Evolution:Enabled");
    private int IntervalMinutes => _config.GetValue<int?>("AiSettings:Evolution:IntervalMinutes") ?? 30;
    private int WindowHours => _config.GetValue<int?>("AiSettings:Evolution:WindowHours") ?? 72;
    private int MinSamples => _config.GetValue<int?>("AiSettings:Evolution:MinSamples") ?? 10;
    private double TriggerThreshold => _config.GetValue<double?>("AiSettings:Evolution:TriggerThreshold") ?? 0.75;
    private double MinImprovement => _config.GetValue<double?>("AiSettings:Evolution:MinImprovement") ?? 0.02;
    private double RegressionTolerance => _config.GetValue<double?>("AiSettings:Evolution:RegressionTolerance") ?? 0.05;

    public PromptEvolutionService(
        IServiceScopeFactory scopeFactory,
        PipelineLoaderService pipelineLoader,
        IConfiguration config,
        ILogger<PromptEvolutionService> logger)
    {
        _scopeFactory = scopeFactory;
        _pipelineLoader = pipelineLoader;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enabled)
        {
            _logger.LogInformation("PromptEvolutionService is disabled (AiSettings:Evolution:Enabled=false)");
            return;
        }

        _logger.LogInformation("PromptEvolutionService started (interval: {Minutes}min)", IntervalMinutes);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(IntervalMinutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await RunEvolutionTickAsync(stoppingToken); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger.LogError(ex, "Prompt evolution tick failed"); }
        }
    }

    private async Task RunEvolutionTickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var aiService = scope.ServiceProvider.GetRequiredService<AiService>();

        // 対象テンプレート = ロード済みパイプラインで使用中のもの + 既存変体があるもの
        var templateRoles = new Dictionary<string, string>(); // templatePath -> stage name (= AgentStep.Role)
        foreach (var pipeline in _pipelineLoader.GetAllPipelines())
            foreach (var stage in pipeline.Stages)
                if (!string.IsNullOrEmpty(stage.SystemPromptTemplate))
                    templateRoles.TryAdd(stage.SystemPromptTemplate, stage.Name);

        var variantTemplates = await db.PromptVariants.Select(v => v.TemplatePath).Distinct().ToListAsync(ct);
        var allTemplates = templateRoles.Keys.Union(variantTemplates).ToList();

        var since = DateTime.UtcNow.AddHours(-WindowHours);

        foreach (var templatePath in allTemplates)
        {
            ct.ThrowIfCancellationRequested();
            var role = templateRoles.GetValueOrDefault(templatePath);

            var variants = await db.PromptVariants.Where(v => v.TemplatePath == templatePath).ToListAsync(ct);
            var championVariant = variants.FirstOrDefault(v => v.Status == "champion");
            var challenger = variants.FirstOrDefault(v => v.Status == "challenger");

            // a) 適応度の更新
            foreach (var v in variants.Where(v => v.Status is "champion" or "challenger"))
            {
                var (avg, trials) = await GetVariantFitnessAsync(db, v.Id, since, ct);
                v.AvgScore = avg;
                v.TrialCount = trials;
            }
            var (baselineAvg, baselineTrials) = await GetBaselineFitnessAsync(db, role, since, ct);
            var (baselineHistAvg, _) = await GetBaselineFitnessAsync(db, role, DateTime.MinValue, ct);

            double? championAvg = championVariant?.AvgScore ?? baselineAvg;
            int championTrials = championVariant?.TrialCount ?? baselineTrials;

            // d) 回帰ガード: AI昇格済みチャンピオンがベースライン実績を下回ったら即ロールバック
            if (championVariant != null && championVariant.AvgScore.HasValue && baselineHistAvg.HasValue
                && championVariant.TrialCount >= MinSamples
                && championVariant.AvgScore.Value < baselineHistAvg.Value - RegressionTolerance)
            {
                championVariant.Status = "rolled_back";
                _logger.LogInformation(
                    "Prompt evolution REGRESSION ROLLBACK: {Template} variant {Id} avg {Avg:F3} < baseline historical {Base:F3} - {Tol} (trials: {Trials}); serving reverts to baseline",
                    templatePath, championVariant.Id, championVariant.AvgScore, baselineHistAvg, RegressionTolerance, championVariant.TrialCount);
                championVariant = null;
                championAvg = baselineAvg;
                championTrials = baselineTrials;
            }

            // c) 選択: challenger が十分試行されたら昇格かロールバック
            if (challenger != null && challenger.TrialCount >= MinSamples && challenger.AvgScore.HasValue && championAvg.HasValue)
            {
                if (challenger.AvgScore.Value >= championAvg.Value + MinImprovement)
                {
                    challenger.Status = "champion";
                    challenger.PromotedAt = DateTime.UtcNow;
                    if (championVariant != null) championVariant.Status = "retired";
                    _logger.LogInformation(
                        "Prompt evolution PROMOTED: {Template} challenger {Id} avg {ChAvg:F3} (trials: {ChTrials}) beat champion avg {ChampAvg:F3} (trials: {ChampTrials})",
                        templatePath, challenger.Id, challenger.AvgScore, challenger.TrialCount, championAvg, championTrials);
                }
                else
                {
                    challenger.Status = "rolled_back";
                    _logger.LogInformation(
                        "Prompt evolution ROLLED BACK: {Template} challenger {Id} avg {ChAvg:F3} (trials: {ChTrials}) did not beat champion avg {ChampAvg:F3} + {Min}",
                        templatePath, challenger.Id, challenger.AvgScore, challenger.TrialCount, championAvg, MinImprovement);
                }
                challenger = null;
            }

            await db.SaveChangesAsync(ct);

            // b) 変異: challenger 不在かつチャンピオンが低調なら新変体を生成
            if (challenger == null && role != null
                && championTrials >= MinSamples && championAvg.HasValue && championAvg.Value < TriggerThreshold)
            {
                try
                {
                    await MutateTemplateAsync(db, aiService, templatePath, role, championVariant, championAvg.Value, since, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Prompt mutation failed for {Template}; skipping this tick", templatePath);
                }
            }
        }
    }

    private static async Task<(double? avg, int trials)> GetVariantFitnessAsync(AppDbContext db, int variantId, DateTime since, CancellationToken ct)
    {
        var query = db.Evaluations.Where(e => e.AgentStep!.PromptVariantId == variantId && e.AgentStep.CreatedAt >= since);
        var trials = await query.Select(e => e.AgentStepId).Distinct().CountAsync(ct);
        if (trials == 0) return (null, 0);
        return (await query.AverageAsync(e => (double)e.Score, ct), trials);
    }

    private static async Task<(double? avg, int trials)> GetBaselineFitnessAsync(AppDbContext db, string? role, DateTime since, CancellationToken ct)
    {
        if (role == null) return (null, 0);
        var query = db.Evaluations.Where(e => e.AgentStep!.Role == role && e.AgentStep.PromptVariantId == null && e.AgentStep.CreatedAt >= since);
        var trials = await query.Select(e => e.AgentStepId).Distinct().CountAsync(ct);
        if (trials == 0) return (null, 0);
        return (await query.AverageAsync(e => (double)e.Score, ct), trials);
    }

    private async Task MutateTemplateAsync(AppDbContext db, AiService aiService, string templatePath, string role,
        PromptVariant? championVariant, double championAvg, DateTime since, CancellationToken ct)
    {
        // 現行チャンピオンのテンプレート本文（変体 or ベースライン）
        string currentTemplate;
        if (championVariant != null)
        {
            var championFile = Path.Combine(_pipelineLoader.VariantsDir, championVariant.FileName);
            if (!File.Exists(championFile)) { _logger.LogWarning("Champion variant file missing: {File}", championFile); return; }
            currentTemplate = await File.ReadAllTextAsync(championFile, ct);
        }
        else
        {
            currentTemplate = await _pipelineLoader.GetPromptTemplateAsync(templatePath);
        }

        // 直近の最低評点ステップ3件を失敗例として収集
        var stepsQuery = championVariant != null
            ? db.AgentSteps.Where(s => s.PromptVariantId == championVariant.Id && s.CreatedAt >= since)
            : db.AgentSteps.Where(s => s.Role == role && s.PromptVariantId == null && s.CreatedAt >= since);
        var worstSteps = await stepsQuery
            .Where(s => s.Evaluations.Any())
            .Select(s => new
            {
                s.Input,
                s.Output,
                AvgScore = s.Evaluations.Average(e => (double)e.Score),
                Reasonings = s.Evaluations.Where(e => e.Reasoning != null).Select(e => e.Criteria + ": " + e.Reasoning).ToList()
            })
            .OrderBy(s => s.AvgScore)
            .Take(3)
            .ToListAsync(ct);
        if (!worstSteps.Any()) return;

        var failureExamples = string.Join("\n\n", worstSteps.Select((s, i) =>
            $"### Failure example {i + 1} (avg score {s.AvgScore:F2})\n" +
            $"Input:\n{Truncate(s.Input, 1500)}\n\nOutput:\n{Truncate(s.Output, 1500)}\n\n" +
            $"Evaluator feedback:\n{Truncate(string.Join("\n", s.Reasonings), 1000)}"));

        var mutationPrompt = $@"You are a prompt engineer improving an underperforming system prompt template used by an AI agent pipeline stage (role: {role}). Its average evaluation score is {championAvg:F2} (criteria: Accuracy, Safety, Format, Helpfulness).

## Current template
{currentTemplate}

## Recent low-scoring executions
{failureExamples}

## Your task
Rewrite the template to address the observed failure patterns while preserving its intent and any {{{{...}}}} placeholders exactly as they appear. Respond in EXACTLY this format, with no markdown code fences:

HYPOTHESIS: <one sentence: what you changed and why it should score higher>
---TEMPLATE---
<the full rewritten template>";

        var response = await aiService.ExecuteCliDirectAsync(mutationPrompt, aiService.DefaultProvider, outputFormat: "text");

        var (hypothesis, newTemplate) = ParseMutationResponse(response);
        if (string.IsNullOrWhiteSpace(newTemplate))
        {
            _logger.LogWarning("Prompt mutation for {Template} produced an empty template; skipping", templatePath);
            return;
        }
        // プレースホルダー保全の検証 — 欠けていたら変体を破棄
        var placeholders = Regex.Matches(currentTemplate, @"\{\{[^}]+\}\}").Select(m => m.Value).Distinct().ToList();
        var missing = placeholders.Where(p => !newTemplate.Contains(p)).ToList();
        if (missing.Any())
        {
            _logger.LogWarning("Prompt mutation for {Template} dropped placeholders ({Missing}); skipping", templatePath, string.Join(", ", missing));
            return;
        }

        var variant = new PromptVariant
        {
            TemplatePath = templatePath,
            Status = "challenger",
            ParentVariantId = championVariant?.Id,
            Hypothesis = Truncate(hypothesis, 1000),
        };
        db.PromptVariants.Add(variant);
        await db.SaveChangesAsync(ct); // Id を確定させてからファイル名を決める

        var stem = Path.GetFileNameWithoutExtension(templatePath);
        variant.FileName = $"{stem}.v{variant.Id}.md";
        Directory.CreateDirectory(_pipelineLoader.VariantsDir);
        await File.WriteAllTextAsync(Path.Combine(_pipelineLoader.VariantsDir, variant.FileName), newTemplate, ct);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Prompt evolution MUTATED: {Template} (champion avg {Avg:F3}) -> new challenger {Id} ({File}). Hypothesis: {Hypothesis}",
            templatePath, championAvg, variant.Id, variant.FileName, variant.Hypothesis);
    }

    private static (string hypothesis, string template) ParseMutationResponse(string response)
    {
        var marker = "---TEMPLATE---";
        var idx = response.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return ("", "");

        var head = response[..idx];
        var hypMatch = Regex.Match(head, @"HYPOTHESIS:\s*(.+)", RegexOptions.None, TimeSpan.FromSeconds(1));
        var hypothesis = hypMatch.Success ? hypMatch.Groups[1].Value.Trim() : "";

        var template = response[(idx + marker.Length)..].Trim();
        // 念のため Markdown フェンスを剥がす
        template = Regex.Replace(template, @"^```[a-z]*\s*\n?|\n?```\s*$", "", RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();
        return (hypothesis, template);
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "…";
}
