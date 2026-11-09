using System.Net;
using System.Net.Http.Json;
using ButikkPilot.Contracts;

namespace ButikkPilot.Web.Services;

public enum ApiErrorKind
{
    Server,
    RateLimited,
    Invalid,
    Offline,
}

public sealed class ApiException : Exception
{
    public ApiException(ApiErrorKind kind, Exception? inner = null) : base($"API error: {kind}", inner) => Kind = kind;

    public ApiErrorKind Kind { get; }
}

public sealed class ButikkPilotApiClient
{
    private readonly HttpClient _http;

    public ButikkPilotApiClient(HttpClient http) => _http = http;

    /// <exception cref="ApiException">Any failure, classified so the UI can show a helpful localized message.</exception>
    public async Task<ChatResponse> SendChatAsync(ChatRequest request, CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync("api/chat", request, ContractsJsonContext.Default.ChatRequest, ct);
            ThrowIfFailed(response);
            return await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.ChatResponse, ct)
                ?? throw new ApiException(ApiErrorKind.Server);
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException(ApiErrorKind.Offline, ex);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient timeout (not a user/demo cancellation).
            throw new ApiException(ApiErrorKind.Offline);
        }
    }

    public async Task<StoreMapDto?> GetMapAsync(string language, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync($"api/store/map?lang={Uri.EscapeDataString(language)}", ContractsJsonContext.Default.StoreMapDto, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            return null; // the map is optional; the chat still works without it
        }
    }

    private static void ThrowIfFailed(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new ApiException(response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => ApiErrorKind.RateLimited,
            HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge => ApiErrorKind.Invalid,
            _ => ApiErrorKind.Server,
        });
    }
}
