using AiChatApp.Data;
using AiChatApp.Models;
using AiChatApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using Markdig;

namespace AiChatApp.Endpoints;

public static class FinanceEndpoints
{
    public static void MapFinanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/finance").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var assets = await db.FinancialAssets
                .Where(a => a.UserId == userId)
                .OrderBy(a => a.Type)
                .ThenBy(a => a.Symbol)
                .ToListAsync();
            return Results.Json(assets);
        });

        group.MapPost("/sync", async (ClaimsPrincipal user, FinanceDataService finance) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var updatedCount = await finance.SyncPricesAsync(userId);
            return Results.Content($@"<span hx-get='/api/finance/list-html' hx-target='#asset-table-body' hx-trigger='load' class='text-[10px] opacity-50'>已更新 {updatedCount} 项资产价格</span>", "text/html");
        });

        group.MapPost("/", async (ClaimsPrincipal user, AppDbContext db, AiService ai, FinanceDataService finance, ILogger<AppDbContext> logger, [FromForm] string? name, [FromForm] string? symbol, [FromForm] string? type, [FromForm] decimal? quantity, [FromForm] decimal? averageCost, [FromForm] decimal? currentPrice, [FromForm] string? input) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

            try 
            {
                string finalName = name ?? "";
                string finalSymbol = symbol ?? "";
                string finalType = type ?? "Stock";
                decimal finalQuantity = quantity ?? 1;
                decimal finalAverageCost = averageCost ?? 0;
                decimal finalCurrentPrice = currentPrice ?? 0;

                if (!string.IsNullOrWhiteSpace(input))
                {
                    var prompt = $@"
        你是一个专业的财经数据解析助手。请将用户输入的关于股票、基金、理财或加密货币资产的自然语言描述解析为结构化的 JSON 格式。
        支持多种语言（如中文、英文、日文、德文等）。

        用户输入: ""{input}""

        解析要求:
        1. 输出必须是一个合法的 JSON 对象。
        2. 包含以下字段:
        - ""name"": (string) 资产名称 (例如: ""京王電鉄"", ""Apple"", ""腾讯控股"")
        - ""symbol"": (string) 交易代码 (例如: ""9008"", ""AAPL"", ""0700.HK"")
        - ""type"": (string) 类型，必须是 ""Stock"", ""Fund"", ""Crypto"" 之一
        - ""quantity"": (number) 持仓数量 (识别 ""株"", ""股"", ""份"", ""units"" 等单位)
        - ""averageCost"": (number) 持仓均价/成本 (识别 ""取得単価"", ""成本"", ""avg cost"", ""cost basis"" 等词汇)
        - ""currentPrice"": (number) 当前最新价格（如果提到）
        3. 如果输入中包含货币符号（如 ¥, $, 円），请提取数值部分并转换为数字。
        4. 如果某个数值字段未知或未在输入中提及，请返回 null。
        5. **只输出原始 JSON 字符串，不要包含任何解释性文字或 Markdown 代码块标签。**
        ";
                    var aiResponse = await ai.ExecuteCliDirectAsync(prompt, ai.DefaultProvider, "你是 AI 财经数据解析器。你只输出纯 JSON 字符串。");
                    try 
                    {
                        var jsonStr = aiResponse.Trim();
                        // Robust JSON extraction: find the first { and last }
                        int firstBrace = jsonStr.IndexOf('{');
                        int lastBrace = jsonStr.LastIndexOf('}');
                        if (firstBrace >= 0 && lastBrace > firstBrace)
                        {
                            jsonStr = jsonStr.Substring(firstBrace, lastBrace - firstBrace + 1);
                        }

                        var options = new System.Text.Json.JsonSerializerOptions 
                        { 
                            PropertyNameCaseInsensitive = true,
                            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
                        };
                        var parsed = System.Text.Json.JsonSerializer.Deserialize<AssetParseResult>(jsonStr, options);
                        if (parsed != null)
                        {
                            if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(parsed.Name)) finalName = parsed.Name;
                            if (string.IsNullOrWhiteSpace(symbol) && !string.IsNullOrWhiteSpace(parsed.Symbol)) finalSymbol = parsed.Symbol;
                            if (string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(parsed.Type)) finalType = parsed.Type;
                            if (quantity == null && parsed.Quantity.HasValue) finalQuantity = parsed.Quantity.Value;
                            if (averageCost == null && parsed.AverageCost.HasValue) finalAverageCost = parsed.AverageCost.Value;
                            if (currentPrice == null && parsed.CurrentPrice.HasValue) finalCurrentPrice = parsed.CurrentPrice.Value;
                        }
                    }
                    catch (Exception ex)
                    { 
                        logger.LogError(ex, "Finance Parsing Error. Response was: {AiResponse}", aiResponse);
                    }
                }

                if (string.IsNullOrWhiteSpace(finalName) && string.IsNullOrWhiteSpace(finalSymbol))
                {
                    logger.LogWarning("Could not parse asset info from input: {Input}", input);
                    return Results.Content("<tr class='bg-error/10'><td colspan='7' class='text-error text-[10px] p-2'>无法解析资产信息。请确保输入包含名称或代码。输入内容: " + WebUtility.HtmlEncode(input ?? "") + "</td></tr>", "text/html");
                }

                var asset = new FinancialAsset
                {
                    UserId = userId,
                    Name = finalName,
                    Symbol = finalSymbol,
                    Type = finalType,
                    Quantity = finalQuantity,
                    AverageCost = finalAverageCost,
                    CurrentPrice = finalCurrentPrice,
                    LastUpdatedAt = DateTime.UtcNow
                };

                // 如果解析出的价格为 0，尝试同步最新价格
                if (asset.CurrentPrice == 0)
                {
                    await finance.SyncPriceAsync(asset);
                }

                // 如果均价未设置且获取到了当前价格，自动填入当前价格作为均价（假设为现价买入）
                if (asset.AverageCost == 0 && asset.CurrentPrice > 0)
                {
                    asset.AverageCost = asset.CurrentPrice;
                }

                db.FinancialAssets.Add(asset);

                var dbUser = await db.Users.FindAsync(userId);
                if (dbUser != null) dbUser.LastBriefingContent = null;

                await db.SaveChangesAsync();
                return Results.Content(BuildAssetRowHtml(asset), "text/html");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Finance Endpoint Error for user {UserId}", userId);
                return Results.Content("<tr class='bg-error/10'><td colspan='7' class='text-error text-[10px] p-2 font-bold'>操作失败: " + WebUtility.HtmlEncode(ex.Message) + "</td></tr>", "text/html");
            }
        }).DisableAntiforgery();
        group.MapGet("/row/{id}", async (ClaimsPrincipal user, AppDbContext db, int id) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var asset = await db.FinancialAssets.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
            if (asset == null) return Results.NotFound();
            return Results.Content(BuildAssetRowHtml(asset), "text/html");
        });

        group.MapGet("/{id}/edit", async (ClaimsPrincipal user, AppDbContext db, int id) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var asset = await db.FinancialAssets.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
            if (asset == null) return Results.NotFound();
            return Results.Content(BuildAssetEditRowHtml(asset), "text/html");
        });

        group.MapPut("/{id}", async (ClaimsPrincipal user, AppDbContext db, int id, [FromForm] string name, [FromForm] string symbol, [FromForm] string type, [FromForm] decimal quantity, [FromForm] decimal averageCost, [FromForm] decimal currentPrice) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var asset = await db.FinancialAssets.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
            if (asset == null) return Results.NotFound();

            asset.Name = name;
            asset.Symbol = symbol;
            asset.Type = type;
            asset.Quantity = quantity;
            asset.AverageCost = averageCost;
            asset.CurrentPrice = currentPrice;
            asset.LastUpdatedAt = DateTime.UtcNow;

            var dbUser = await db.Users.FindAsync(userId);
            if (dbUser != null) dbUser.LastBriefingContent = null;

            await db.SaveChangesAsync();
            return Results.Content(BuildAssetRowHtml(asset), "text/html");
        }).DisableAntiforgery();

        group.MapDelete("/{id}", async (ClaimsPrincipal user, AppDbContext db, int id) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var asset = await db.FinancialAssets.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
            if (asset == null) return Results.NotFound();
            db.FinancialAssets.Remove(asset);

            var dbUser = await db.Users.FindAsync(userId);
            if (dbUser != null) dbUser.LastBriefingContent = null;

            await db.SaveChangesAsync();
            return Results.Ok();
        }).DisableAntiforgery();

        group.MapGet("/analysis", async (ClaimsPrincipal user, AppDbContext db, AiService ai) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var assets = await db.FinancialAssets.Where(a => a.UserId == userId).ToListAsync();
            
            if (!assets.Any())
            {
                return Results.Content("<div class='opacity-50 text-center py-8 text-xs'>请先添加股票或基金以进行分析。</div>", "text/html");
            }

            var assetList = string.Join("\n", assets.Select(a => $"- {a.Name} ({a.Symbol}), 类型: {a.Type}, 数量: {a.Quantity}, 均价: {a.AverageCost}"));
            
            var prompt = $@"
你是一个资深财经分析师。请对用户的投资组合进行分析并提供优化建议。
用户持仓:
{assetList}

要求:
1. 评估风险。
2. 提供优化建议。
3. 保持专业 Markdown 格式，300 字以内。
";
            var analysis = await ai.ExecuteCliDirectAsync(prompt, ai.DefaultProvider, "你是 AI 财经分析师。", outputFormat: "text");
            return Results.Content($@"<div class='prose prose-sm max-w-none text-base-content/80 p-4'>{Markdown.ToHtml(analysis)}</div>", "text/html");
        });

        group.MapGet("/recommend", async (ClaimsPrincipal user, AiService ai, [FromQuery] string? query) =>
        {
            if (string.IsNullOrWhiteSpace(query)) return Results.BadRequest("请输入投资预算、关键字或理财目标。");

            bool isBudget = decimal.TryParse(query, out decimal budget);
            string userIntent = isBudget ? $"{budget:N2} USD" : query;

            var prompt = $@"
你是一个顶级的 AI 理财顾问，专门负责日美跨市场资产配置。请根据用户的投资意图，提供一份结构化的投资建议。

用户意图: ""{userIntent}""

要求:
1. **多市场配置**: 必须同时包含日本 (JP) 和美国 (US) 市场的具体标的（如美股、日股、ETF、Reits等）。
2. **主题驱动**: 
   - 如果用户输入数字，视为总投资额，提供均衡稳健的配置。
   - 如果用户输入关键字（如 ""AI"", ""半导体"", ""高股息"", ""新能源""），请紧贴该主题选择日美两地的龙头企业或专项基金。
   - 如果用户输入自然语言目标（如 ""我想在退休前获得稳定现金流"" 或 ""激进增长组合""），请相应调整风险偏好。
3. **输出 JSON 格式**:
   - ""recommendations"": 数组，包含项: ""name"" (名称), ""symbol"" (代码), ""type"" (Stock/Fund), ""market"" (US/JP), ""allocation"" (建议仓位, 如 ""25%""), ""reason"" (推荐理由)。
   - ""strategy"": (string) 针对该组合的整体配置思路描述。
   - ""displayBudget"": (string) 一个简短的展示标题（如 ""5万美金均衡组合"" 或 ""全球半导体先驱""）。
4. **语言一致性**: 推荐理由和策略描述请使用中文。
5. **只输出原始 JSON 字符串。**
";
            var aiResponse = await ai.ExecuteCliDirectAsync(prompt, ai.DefaultProvider, "你是 AI 资深理财顾问。你只输出纯 JSON 字符串。", outputFormat: "text");
            
            try 
            {
                var jsonStr = aiResponse.Trim();
                if (jsonStr.Contains("```") || (!jsonStr.StartsWith("{") && !jsonStr.StartsWith("[")))
                {
                    var match = Regex.Match(jsonStr, @"(\{.*\}|\[.*\])", RegexOptions.Singleline);
                    if (match.Success) jsonStr = match.Value;
                }

                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                RecommendationResult? result = null;
                if (jsonStr.StartsWith("["))
                {
                    var items = System.Text.Json.JsonSerializer.Deserialize<List<RecommendationItem>>(jsonStr, options);
                    if (items != null) result = new RecommendationResult { Recommendations = items, Strategy = "建议分散投资于日美两个市场。", DisplayBudget = userIntent };
                }
                else
                {
                    result = System.Text.Json.JsonSerializer.Deserialize<RecommendationResult>(jsonStr, options);
                }

                if (result == null || result.Recommendations == null) throw new Exception("Failed to parse");

                var displayLabel = string.IsNullOrWhiteSpace(result.DisplayBudget) ? userIntent : result.DisplayBudget;

                var sb = new StringBuilder();
                sb.Append($@"
<div class='p-4 space-y-6 animate-in fade-in slide-in-from-top-2 duration-300'>
    <div class='flex items-center justify-between'>
        <div class='flex items-center gap-3'>
            <div class='p-2 bg-primary/10 text-primary rounded-xl'>
                <svg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 24 24' stroke-width='2.5' stroke='currentColor' class='w-5 h-5'><path stroke-linecap='round' stroke-linejoin='round' d='M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09zM18.259 8.715L18 9.75l-.259-1.035a3.375 3.375 0 00-2.455-2.456L14.25 6l1.036-.259a3.375 3.375 0 002.455-2.456L18 2.25l.259 1.035a3.375 3.375 0 002.456 2.456L21.75 6l-1.035.259a3.375 3.375 0 00-2.456 2.456zM16.894 20.567L16.5 21.75l-.394-1.183a2.25 2.25 0 00-1.423-1.423L13.5 18.75l1.183-.394a2.25 2.25 0 001.423-1.423l.394-1.183.394 1.183a2.25 2.25 0 001.423 1.423l1.183.394-1.183.394a2.25 2.25 0 00-1.423 1.423z' /></svg>
            </div>
            <div>
                <h4 class='text-sm font-black uppercase tracking-widest opacity-80'>{displayLabel}</h4>
                <div class='text-[10px] opacity-40 font-mono'>AI Powered Recommendation</div>
            </div>
        </div>
    </div>
    <div class='bg-primary/5 border border-primary/10 rounded-2xl p-4'>
        <h4 class='text-xs font-black uppercase tracking-widest text-primary mb-2 opacity-60'>配置策略</h4>
        <p class='text-sm leading-relaxed opacity-80'>{result.Strategy}</p>
    </div>
    <div class='grid grid-cols-1 md:grid-cols-2 gap-4'>");

                foreach (var rec in result.Recommendations)
                {
                    var valsObj = new { 
                        name = rec.Name, 
                        symbol = rec.Symbol, 
                        type = rec.Type.Contains("Fund") ? "Fund" : "Stock",
                        quantity = 1,
                        averageCost = 0
                    };
                    var jsonVals = System.Text.Json.JsonSerializer.Serialize(valsObj);
                    var encodedVals = WebUtility.HtmlEncode(jsonVals);

                    sb.Append($@"
        <div class='card bg-base-100 border border-base-content/10 shadow-sm hover:border-primary/30 transition-all'>
            <div class='card-body p-4'>
                <div class='flex justify-between items-start mb-2'>
                    <div>
                        <h4 class='font-black text-sm'>{rec.Name}</h4>
                        <div class='flex gap-1 mt-1'>
                            <span class='badge badge-ghost badge-xs font-mono'>{rec.Symbol}</span>
                            <span class='badge badge-outline badge-xs opacity-40 uppercase'>{rec.Market}</span>
                        </div>
                    </div>
                    <div class='text-right'>
                        <div class='text-primary font-black text-lg'>{rec.Allocation}</div>
                    </div>
                </div>
                <p class='text-[11px] opacity-60 leading-relaxed mb-4'>{rec.Reason}</p>
                <div class='card-actions justify-end items-center gap-2'>
                    <span class='loading loading-spinner loading-xs htmx-indicator text-primary'></span>
                    <button hx-post='/api/finance' 
                            hx-vals='{encodedVals}'
                            hx-target='#asset-table-body'
                            hx-swap='beforeend'
                            hx-indicator='previous .htmx-indicator'
                            class='btn btn-primary btn-xs rounded-lg gap-1'>
                        添加到持仓
                    </button>
                </div>
            </div>
        </div>");
                }

                sb.Append("</div></div>");
                return Results.Content(sb.ToString(), "text/html");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Recommendation Error: {ex.Message}");
                return Results.Content("<div class='alert alert-warning m-4'>推荐生成失败，请尝试更明确的投资需求。</div>", "text/html");
            }
        });

        group.MapGet("/list-html", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var assets = await db.FinancialAssets.Where(a => a.UserId == userId).ToListAsync();
            var rows = string.Join("", assets.Select(BuildAssetRowHtml));
            return Results.Content(rows, "text/html");
        });
    }

    private static string BuildAssetRowHtml(FinancialAsset asset)
    {
        var totalValue = asset.Quantity * asset.CurrentPrice;
        var profitLoss = (asset.CurrentPrice - asset.AverageCost) * asset.Quantity;
        var profitPercent = asset.AverageCost > 0 ? (asset.CurrentPrice - asset.AverageCost) / asset.AverageCost * 100 : 0;
        var profitClass = profitLoss >= 0 ? "text-success" : "text-error";
        var profitPrefix = profitLoss >= 0 ? "+" : "";

        // Calculate relative time for last updated
        var timeSpan = DateTime.UtcNow - asset.LastUpdatedAt;
        string updatedStr = timeSpan.TotalMinutes < 1 ? "刚刚" : 
                           timeSpan.TotalHours < 1 ? $"{(int)timeSpan.TotalMinutes}分钟前" : 
                           timeSpan.TotalDays < 1 ? $"{(int)timeSpan.TotalHours}小时前" : 
                           $"{(int)timeSpan.TotalDays}天前";

        return $@"
<tr id='asset-{asset.Id}' class='hover:bg-base-200/50 transition-colors group'>
    <td>
        <div class='font-bold'>{asset.Name}</div>
        <div class='flex items-center gap-1 mt-0.5'>
            <span class='text-[10px] opacity-40 font-mono'>{asset.Symbol}</span>
            <span class='text-[9px] opacity-20'>• {updatedStr}</span>
        </div>
    </td>
    <td><span class='badge badge-ghost badge-sm text-[10px]'>{asset.Type}</span></td>
    <td class='text-right font-mono text-xs'>{asset.Quantity}</td>
    <td class='text-right font-mono text-xs'>{asset.AverageCost:F2}</td>
    <td class='text-right font-mono text-xs font-bold'>{asset.CurrentPrice:F2}</td>
    <td class='text-right'>
        <div class='flex flex-col items-end'>
            <div class='font-mono text-xs font-bold'>{totalValue:F2}</div>
            <div class='font-mono text-[9px] {profitClass}'>{profitPrefix}{profitPercent:F2}%</div>
        </div>
    </td>
    <td class='text-right'>
        <div class='flex justify-end gap-1 opacity-0 group-hover:opacity-100 transition-opacity'>
            <button hx-get='/api/finance/{asset.Id}/edit' hx-target='#asset-{asset.Id}' hx-swap='outerHTML' class='btn btn-ghost btn-xs'>
                <svg xmlns='http://www.w3.org/2000/svg' class='h-3.5 w-3.5' fill='none' viewBox='0 0 24 24' stroke='currentColor'><path stroke-linecap='round' stroke-linejoin='round' stroke-width='2' d='M15.232 5.232l3.536 3.536m-2.036-5.036a2.5 2.5 0 113.536 3.536L6.5 21.036H3v-3.572L16.732 3.732z' /></svg>
            </button>
            <button hx-delete='/api/finance/{asset.Id}' hx-target='#asset-{asset.Id}' hx-swap='delete' hx-confirm='确定要删除持仓吗？' class='btn btn-ghost btn-xs text-error'>
                <svg xmlns='http://www.w3.org/2000/svg' class='h-3.5 w-3.5' fill='none' viewBox='0 0 24 24' stroke='currentColor'><path stroke-linecap='round' stroke-linejoin='round' stroke-width='2' d='M6 18L18 6M6 6l12 12' /></svg>
            </button>
        </div>
    </td>
</tr>";
    }

    private static string BuildAssetEditRowHtml(FinancialAsset asset)
    {
        return $@"
<tr id='asset-{asset.Id}' class='bg-base-200/80 animate-in fade-in duration-200'>
    <td>
        <input type='text' name='name' value='{asset.Name}' class='input input-bordered input-xs w-full mb-1 font-bold' required />
        <input type='text' name='symbol' value='{asset.Symbol}' class='input input-bordered input-xs w-full font-mono text-[10px]' required />
    </td>
    <td>
        <select name='type' class='select select-bordered select-xs w-full'>
            <option value='Stock' {(asset.Type == "Stock" ? "selected" : "")}>Stock</option>
            <option value='Fund' {(asset.Type == "Fund" ? "selected" : "")}>Fund</option>
            <option value='Crypto' {(asset.Type == "Crypto" ? "selected" : "")}>Crypto</option>
        </select>
    </td>
    <td class='text-right'>
        <input type='number' name='quantity' value='{asset.Quantity}' step='any' class='input input-bordered input-xs w-16 text-right font-mono' required />
    </td>
    <td class='text-right'>
        <input type='number' name='averageCost' value='{asset.AverageCost}' step='any' class='input input-bordered input-xs w-20 text-right font-mono' required />
    </td>
    <td class='text-right'>
        <input type='number' name='currentPrice' value='{asset.CurrentPrice}' step='any' class='input input-bordered input-xs w-20 text-right font-mono font-bold' required />
    </td>
    <td class='text-right'>-</td>
    <td class='text-right'>
        <div class='flex justify-end gap-1'>
            <button hx-put='/api/finance/{asset.Id}' hx-target='#asset-{asset.Id}' hx-swap='outerHTML' hx-include='closest tr' class='btn btn-success btn-xs'>保存</button>
            <button hx-get='/api/finance/row/{asset.Id}' hx-target='#asset-{asset.Id}' hx-swap='outerHTML' class='btn btn-ghost btn-xs'>取消</button>
        </div>
    </td>
</tr>";
    }

    private class AssetParseResult
    {
        public string Name { get; set; } = "";
        public string Symbol { get; set; } = "";
        public string Type { get; set; } = "Stock";
        public decimal? Quantity { get; set; }
        public decimal? AverageCost { get; set; }
        public decimal? CurrentPrice { get; set; }
    }

    private class RecommendationResult
    {
        public List<RecommendationItem>? Recommendations { get; set; }
        public string Strategy { get; set; } = "";
        public string DisplayBudget { get; set; } = "";
    }

    private class RecommendationItem
    {
        public string Name { get; set; } = "";
        public string Symbol { get; set; } = "";
        public string Type { get; set; } = "";
        public string Market { get; set; } = "";
        public string Allocation { get; set; } = "";
        public string Reason { get; set; } = "";
    }
}
