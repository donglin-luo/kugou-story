using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace KugouStory.Core;

internal static class ProviderRequest
{
    internal static async Task<JsonDocument> PostAsync(HttpClient http, Uri endpoint, string key, object payload,
        string stage, int timeoutSeconds, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                if (!string.IsNullOrWhiteSpace(key)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
                request.Headers.UserAgent.ParseAdd("KugouStory/1.1");
                request.Content = JsonContent.Create(payload);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                var code = (int)response.StatusCode;
                if (attempt == 0 && (code is 408 or 429 || code >= 500))
                { await Task.Delay(900, timeout.Token); continue; }
                if (!response.IsSuccessStatusCode)
                    throw new StoryException(response.StatusCode switch
                    {
                        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"{stage}无权限，请检查 DeepSeek API Key。",
                        HttpStatusCode.PaymentRequired => "DeepSeek 余额不足，请在服务商控制台检查余额。",
                        HttpStatusCode.TooManyRequests => $"{stage}请求过于频繁，请稍后重试。",
                        HttpStatusCode.NotFound => $"{stage}接口或模型不存在，请检查设置。",
                        _ => $"{stage}暂时不可用（HTTP {code}），请稍后重试。"
                    });
                using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var buffer = new MemoryStream();
                var chunk = new byte[8192];
                int read;
                while ((read = await stream.ReadAsync(chunk, timeout.Token)) != 0)
                {
                    if (buffer.Length + read > 4_194_304) throw new StoryException($"{stage}返回内容过长，请重试。");
                    buffer.Write(chunk, 0, read);
                }
                return JsonDocument.Parse(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new StoryException($"{stage}响应超时，请检查网络后重试。"); }
        catch (HttpRequestException) { throw new StoryException($"无法连接{stage}服务，请检查网络和接口地址。"); }
        catch (JsonException) { throw new StoryException($"{stage}返回的内容无法解析，请重试。"); }
    }
}
