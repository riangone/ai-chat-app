namespace AiChatApp.Services;

public class MemorySearchService {
    public async Task<List<string>> SearchAsync(string query) {
        // Mock implementation for multi-agent test
        return new List<string> { "Found memory related to: " + query };
    }
}
