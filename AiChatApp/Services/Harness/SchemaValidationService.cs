using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using AiChatApp.Models.Harness;

namespace AiChatApp.Services.Harness;

public class SchemaValidationService
{
    private readonly ILogger<SchemaValidationService> _logger;
    private readonly Dictionary<string, JsonSchema> _schemaCache;
    private readonly string _schemasBasePath;

    public SchemaValidationService(ILogger<SchemaValidationService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _schemaCache = new Dictionary<string, JsonSchema>();
        
        // pipelines/schemas/ ディレクトリパスを設定
        var basePath = AppContext.BaseDirectory;
        _schemasBasePath = Path.Combine(basePath, "..", "..", "pipelines", "schemas");
    }

    /// <summary>
    /// JSON Schema に基づいて JSON コンテンツを検証します
    /// </summary>
    /// <param name="schemaPath">スキーマ相対パス（例："executor_input.json"）</param>
    /// <param name="jsonContent">検証対象の JSON 文字列</param>
    /// <returns>検証結果</returns>
    public ValidationResult Validate(string schemaPath, string jsonContent)
    {
        try
        {
            // スキーマをロード
            var schema = LoadSchema(schemaPath);

            // JSON を JsonElement に変換して検証
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(jsonContent);

            // スキーマで検証
            var evaluationResults = schema.Evaluate(jsonElement);

            if (evaluationResults.IsValid)
            {
                _logger.LogInformation("JSON Schema 検証成功: {SchemaPath}", schemaPath);
                return new ValidationResult(true, new List<string>());
            }

            // エラーを抽出
            var errors = ExtractErrors(evaluationResults);
            _logger.LogWarning("JSON Schema 検証失敗: {SchemaPath}. エラー数: {ErrorCount}", schemaPath, errors.Count);
            
            return new ValidationResult(false, errors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JSON Schema 検証中にエラーが発生しました: {SchemaPath}", schemaPath);
            return new ValidationResult(false, new List<string> { $"検証処理エラー: {ex.Message}" });
        }
    }

    /// <summary>
    /// 検証エラーから補正用プロンプトを生成します
    /// </summary>
    /// <param name="errors">検証エラーリスト</param>
    /// <param name="originalOutput">オリジナル出力</param>
    /// <returns>補正用プロンプト</returns>
    public string GenerateCorrectivePrompt(List<string> errors, string originalOutput)
    {
        var errorSummary = string.Join("\n", errors.Select(e => $"  - {e}"));
        
        var prompt = $"""
Validation errors occurred with the JSON output:

{errorSummary}

Original output:
```json
{originalOutput}
```

Please analyze the errors and provide a corrected JSON response that:
1. Fixes all validation errors
2. Maintains the original intent and data
3. Follows the required schema structure

Respond ONLY with the corrected JSON, no explanations or markdown formatting.
""";

        return prompt;
    }

    /// <summary>
    /// スキーマファイルをロードします（キャッシュを使用）
    /// </summary>
    /// <param name="schemaPath">スキーマ相対パス</param>
    /// <returns>JsonSchema オブジェクト</returns>
    private JsonSchema LoadSchema(string schemaPath)
    {
        // キャッシュを確認
        if (_schemaCache.TryGetValue(schemaPath, out var cachedSchema))
        {
            _logger.LogDebug("キャッシュからスキーマをロード: {SchemaPath}", schemaPath);
            return cachedSchema;
        }

        // ファイルをロード
        var fullPath = Path.Combine(_schemasBasePath, schemaPath);
        
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"スキーマファイルが見つかりません: {fullPath}");
        }

        var schemaContent = File.ReadAllText(fullPath, System.Text.Encoding.UTF8);
        var schema = JsonSchema.FromText(schemaContent);

        // キャッシュに保存
        _schemaCache[schemaPath] = schema;
        _logger.LogDebug("スキーマをロードしてキャッシュに保存: {SchemaPath}", schemaPath);

        return schema;
    }

    /// <summary>
    /// 検証結果からエラーメッセージを抽出します
    /// </summary>
    private List<string> ExtractErrors(EvaluationResults results)
    {
        var errors = new List<string>();

        if (results.Details != null)
        {
            ExtractErrorsRecursive(results.Details, errors, "");
        }

        // 詳細情報がない場合は汎用エラーメッセージを返す
        if (errors.Count == 0)
        {
            errors.Add("JSON Schema 検証に失敗しました");
        }

        return errors;
    }

    /// <summary>
    /// エラー詳細を再帰的に抽出します
    /// </summary>
    private void ExtractErrorsRecursive(IEnumerable<EvaluationResults> details, List<string> errors, string path)
    {
        foreach (var detail in details)
        {
            var currentPath = string.IsNullOrEmpty(path) 
                ? detail.InstanceLocation?.ToString() ?? "root"
                : $"{path}.{detail.InstanceLocation?.ToString() ?? ""}";

            if (!detail.IsValid)
            {
                var errorMessage = $"[{currentPath}] {detail.SchemaLocation?.ToString() ?? "検証失敗"}";
                if (!errors.Contains(errorMessage))
                {
                    errors.Add(errorMessage);
                }
            }

            // ネストされた詳細を処理
            if (detail.Details != null)
            {
                ExtractErrorsRecursive(detail.Details, errors, currentPath);
            }
        }
    }
}
