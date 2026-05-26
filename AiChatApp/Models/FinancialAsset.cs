using System.ComponentModel.DataAnnotations;

namespace AiChatApp.Models;

public class FinancialAsset
{
    public int Id { get; set; }
    public int UserId { get; set; }
    
    [Required]
    public string Name { get; set; } = string.Empty;
    
    [Required]
    public string Symbol { get; set; } = string.Empty; // e.g. "AAPL", "000001"
    
    public string Type { get; set; } = "Stock"; // "Stock", "Fund", "Crypto"
    
    public decimal Quantity { get; set; }
    public decimal AverageCost { get; set; }
    
    public decimal CurrentPrice { get; set; }
    public decimal? ChangePercent { get; set; }
    
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
}
