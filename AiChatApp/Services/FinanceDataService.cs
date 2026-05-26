using AiChatApp.Data;
using AiChatApp.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiChatApp.Services;

public class FinanceDataService
{
    private readonly AppDbContext _db;
    private readonly AiService _ai;
    private readonly ILogger<FinanceDataService> _logger;

    public FinanceDataService(AppDbContext db, AiService ai, ILogger<FinanceDataService> logger)
    {
        _db = db;
        _ai = ai;
        _logger = logger;
    }

    public async Task<bool> SyncPriceAsync(FinancialAsset asset)
    {
        var prompt = $@"
你是一个实时的金融市场数据分析专家。请使用你的搜索工具获取该资产最新的真实市场交易价格。

资产名称: {asset.Name}
资产代码: {asset.Symbol}
资产类型: {asset.Type}

要求:
1. 必须使用搜索工具查找当前（或最近一个交易日）的真实市场价格。
2. 识别货币单位，但只输出一个纯数字（以该资产主要交易市场的货币为准，如美股为 USD，A股为 CNY）。
3. 只输出数字，不要有任何 Markdown、符号或解释。

示例输出:
185.92
";

        try
        {
            var aiResponse = await _ai.ExecuteCliDirectAsync(prompt, _ai.DefaultProvider, "你是 AI 金融数据获取器。你必须通过搜索提供最新的真实价格。你只输出纯数字。", outputFormat: "text");
            var cleanResponse = aiResponse.Trim();
            
            // Handle cases where AI might return "$185.92" instead of "185.92"
            cleanResponse = Regex.Replace(cleanResponse, @"[^\d.]", "");

            if (decimal.TryParse(cleanResponse, out decimal price))
            {
                asset.CurrentPrice = price;
                asset.LastUpdatedAt = DateTime.UtcNow;
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing price for asset {Symbol}", asset.Symbol);
        }
        return false;
    }

    public async Task<int> SyncPricesAsync(int userId)
    {
        var assets = await _db.FinancialAssets
            .Where(a => a.UserId == userId)
            .ToListAsync();

        if (!assets.Any()) return 0;

        int updatedCount = 0;
        var assetList = string.Join("\n", assets.Select(a => $"- {a.Name} ({a.Symbol}), 类型: {a.Type}"));

        var prompt = $@"
你是一个实时的金融市场数据抓取工具。请为以下持仓列表获取最新的真实市场交易价格。

持仓列表:
{assetList}

要求:
1. 你必须对列表中的每一项进行实时搜索，以获取最准确的价格。
2. 输出必须是一个标准的 JSON 对象，Key 是资产的代码 (Symbol)，Value 是最新的价格 (Number)。
3. 不要包含货币符号，只保留数字。
4. 只输出纯 JSON 字符串，不要有任何 Markdown 或解释。

示例输出:
{{
  ""AAPL"": 185.92,
  ""000001.SS"": 3105.20,
  ""BTC"": 65432.10
}}
";

        try
        {
            var aiResponse = await _ai.ExecuteCliDirectAsync(prompt, _ai.DefaultProvider, "你是 AI 金融数据获取器。你必须通过搜索获取所有资产的最新价格并返回 JSON 字典。", outputFormat: "text");
            var jsonStr = aiResponse.Trim();
            
            if (jsonStr.Contains("```"))
            {
                var match = Regex.Match(jsonStr, @"\{(?:[^{}]|(?<open>\{)|(?<-open>\}))+(?(open)(?!))", RegexOptions.Singleline);
                if (match.Success) jsonStr = match.Value;
            }

            var prices = JsonSerializer.Deserialize<Dictionary<string, decimal>>(jsonStr, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            if (prices != null)
            {
                foreach (var asset in assets)
                {
                    decimal? foundPrice = null;
                    if (prices.TryGetValue(asset.Symbol, out decimal p1)) foundPrice = p1;
                    else if (prices.Keys.Any(k => k.Equals(asset.Symbol, StringComparison.OrdinalIgnoreCase)))
                    {
                        var key = prices.Keys.First(k => k.Equals(asset.Symbol, StringComparison.OrdinalIgnoreCase));
                        foundPrice = prices[key];
                    }
                    
                    if (foundPrice.HasValue)
                    {
                        asset.CurrentPrice = foundPrice.Value;
                        asset.LastUpdatedAt = DateTime.UtcNow;
                        updatedCount++;
                    }
                    else
                    {
                        // Fallback: try individual sync
                        if (await SyncPriceAsync(asset)) updatedCount++;
                    }
                }

                if (updatedCount > 0)
                {
                    var user = await _db.Users.FindAsync(userId);
                    if (user != null) user.LastBriefingContent = null; 
                    await _db.SaveChangesAsync();
                }
                return updatedCount;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing financial prices for user {UserId}", userId);
            
            // Total fallback
            foreach(var asset in assets)
            {
                if (await SyncPriceAsync(asset)) updatedCount++;
            }
            if (updatedCount > 0) await _db.SaveChangesAsync();
        }

        return updatedCount;
    }
}
