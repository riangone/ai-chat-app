namespace AiChatApp.Models.Harness;

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; }

    public ValidationResult()
    {
        IsValid = true;
        Errors = new List<string>();
    }

    public ValidationResult(bool isValid, List<string>? errors = null)
    {
        IsValid = isValid;
        Errors = errors ?? new List<string>();
    }
}
