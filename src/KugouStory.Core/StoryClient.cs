using System.Text.Json;

namespace KugouStory.Core;

public sealed class StoryClient(HttpClient http)
{
    // Every production story goes through research and review before presentation.
    public async Task<string> CompleteJsonAsync(ApiSettings settings, string instructions, object input, CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured) throw new StoryException("请先在设置中填写 DeepSeek API Key。");
        var endpoint = settings.Endpoint();
        var payload = new Dictionary<string, object>
        {
            ["model"] = settings.Model.Trim(), ["stream"] = false,
            ["max_tokens"] = 1800, ["temperature"] = 0.2,
            ["response_format"] = new { type = "json_object" },
            ["messages"] = new[]
            {
                new { role = "system", content = instructions },
                new { role = "user", content = JsonSerializer.Serialize(input) }
            }
        };
        if (endpoint.Host.Equals("api.deepseek.com", StringComparison.OrdinalIgnoreCase)) payload["thinking"] = new { type = "disabled" };
        using var document = await ProviderRequest.PostAsync(http, endpoint, settings.ApiKey, payload, "AI", 50, cancellationToken);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0 || choices[0].ValueKind != JsonValueKind.Object ||
            !choices[0].TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(content.GetString()))
            throw new StoryException("AI 返回格式不兼容，请检查模型或重试。");
        return content.GetString()!;
    }
}
