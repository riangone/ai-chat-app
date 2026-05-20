namespace AiChatApp.Models;

public class Attachment
{
    public int Id { get; set; }
    public int? MessageId { get; set; }
    public Message? Message { get; set; }
    public int UserId { get; set; }
    public string FileName { get; set; } = "";
    public string StoredPath { get; set; } = "";
    public long FileSize { get; set; }
    public string MimeType { get; set; } = "";
    // "image" | "text" | "binary"
    public string FileType { get; set; } = "binary";
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
