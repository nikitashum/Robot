using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ButikkPilot.Core.Abstractions;
using ButikkPilot.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace ButikkPilot.Infrastructure.Llm;

/// <summary>Chat-completions client for OpenAI and Azure OpenAI with tool calling. Timeouts/retries/circuit breaker live in the HttpClient pipeline.</summary>
public sealed class OpenAiLlmClient : ILLMClient
{
    private static readonly JsonSerializerOptions ToolJson = new(JsonSerializerDefaults.Web);

    public const string HttpClientName = "llm";

    private readonly IHttpClientFactory _httpFactory;
    private readonly LlmOptions _options;
    private readonly ILogger<OpenAiLlmClient> _logger;

    public OpenAiLlmClient(IHttpClientFactory httpFactory, IOptions<LlmOptions> options, ILogger<OpenAiLlmClient> logger)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<LlmCompletionResult> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        using var message = BuildHttpRequest(request);

        try
        {
            // A fresh client per call lets the factory rotate handlers (DNS changes) and keeps the singleton free of captive state.
            var http = _httpFactory.CreateClient(HttpClientName);
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                // Log status + provider error code only: bodies can echo prompt content.
                var code = await TryReadErrorCodeAsync(response, ct);
                _logger.LogError("LLM provider returned {Status} ({Code})", (int)response.StatusCode, code);
                throw new LlmUnavailableException($"LLM provider returned HTTP {(int)response.StatusCode}.");
            }

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(body, cancellationToken: ct);
            return Parse(doc.RootElement);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new LlmUnavailableException("LLM request timed out.");
        }
        catch (Exception ex) when (ex is HttpRequestException or ExecutionRejectedException or JsonException or InvalidDataException)
        {
            throw new LlmUnavailableException("LLM request failed.", ex);
        }
    }

    private HttpRequestMessage BuildHttpRequest(LlmRequest request)
    {
        var azure = _options.Provider == LlmProvider.AzureOpenAI;
        var baseUrl = (_options.Endpoint ?? "https://api.openai.com/v1").TrimEnd('/');
        var url = azure
            ? $"{baseUrl}/openai/deployments/{Uri.EscapeDataString(_options.Model)}/chat/completions?api-version={Uri.EscapeDataString(_options.ApiVersion)}"
            : $"{baseUrl}/chat/completions";

        var payload = new Dictionary<string, object?>
        {
            ["messages"] = request.Messages.Select(MapMessage).ToList(),
            ["max_completion_tokens"] = _options.MaxCompletionTokens,
        };

        if (request.Tools.Count > 0)
        {
            payload["tools"] = request.Tools;
            payload["tool_choice"] = "auto";
        }

        if (!azure)
        {
            payload["model"] = _options.Model;
        }

        var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, ToolJson), Encoding.UTF8, "application/json"),
        };

        if (azure)
        {
            message.Headers.Add("api-key", _options.ApiKey);
        }
        else
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        return message;
    }

    private static object MapMessage(ChatMessage m)
    {
        if (m.Role == "tool")
        {
            return new { role = "tool", tool_call_id = m.ToolCallId, content = m.Content };
        }

        if (m.Role == "assistant" && m.ToolCalls is { Count: > 0 })
        {
            return new
            {
                role = "assistant",
                content = string.IsNullOrEmpty(m.Content) ? null : m.Content,
                tool_calls = m.ToolCalls.Select(t => new
                {
                    id = t.Id,
                    type = "function",
                    function = new { name = t.Name, arguments = t.ArgumentsJson },
                }).ToList(),
            };
        }

        return new { role = m.Role, content = m.Content };
    }

    private static LlmCompletionResult Parse(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
        {
            throw new InvalidDataException("LLM response has no choices.");
        }

        var msg = choices[0].GetProperty("message");
        var content = msg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;

        var calls = new List<ToolInvocation>();
        if (msg.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
        {
            foreach (var tc in toolCalls.EnumerateArray())
            {
                if (!tc.TryGetProperty("function", out var fn))
                {
                    continue;
                }

                var name = fn.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                calls.Add(new ToolInvocation
                {
                    Id = tc.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } idText ? idText : $"call_{Guid.NewGuid():N}",
                    Name = name,
                    ArgumentsJson = fn.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() ?? "{}" : "{}",
                });
            }
        }

        return new LlmCompletionResult { AssistantContent = content, ToolCalls = calls };
    }

    private static async Task<string> TryReadErrorCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using var s = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(s, cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
                {
                    return code.GetString() ?? "unknown";
                }

                if (err.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
                {
                    return type.GetString() ?? "unknown";
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or IOException)
        {
            // Body unreadable: the status code alone is enough.
        }

        return "unknown";
    }
}
