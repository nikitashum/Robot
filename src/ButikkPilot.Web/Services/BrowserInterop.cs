using Microsoft.JSInterop;

namespace ButikkPilot.Web.Services;

/// <summary>
/// Thin, failure-tolerant wrapper over the two JS modules. Nothing here may throw into the UI: a missing
/// speech engine or a blocked storage must degrade a feature, never crash the page.
/// </summary>
public sealed class BrowserInterop : IAsyncDisposable
{
    private const string WelcomeKey = "butikkpilot_welcome_seen";

    private readonly IJSRuntime _js;
    private readonly Lazy<Task<IJSObjectReference>> _speech;
    private readonly Lazy<Task<IJSObjectReference>> _ui;

    public BrowserInterop(IJSRuntime js)
    {
        _js = js;
        _speech = new(() => js.InvokeAsync<IJSObjectReference>("import", "./js/speech.js").AsTask());
        _ui = new(() => js.InvokeAsync<IJSObjectReference>("import", "./js/ui.js").AsTask());
    }

    // ---- speech ----

    public Task<bool> IsRecognitionAvailableAsync() => SafeAsync(_speech, "isRecognitionAvailable", false);

    public Task<bool> IsSynthesisAvailableAsync() => SafeAsync(_speech, "isSynthesisAvailable", false);

    public Task<string?> ListenOnceAsync(string lang) => SafeAsync<string?>(_speech, "listenOnce", null, lang);

    public Task SpeakAsync(string text, string lang) => SafeVoidAsync(_speech, "speak", text, lang);

    public Task StopSpeechAsync() => SafeVoidAsync(_speech, "stopSpeaking");

    // ---- ui ----

    public Task ScrollToBottomAsync(Microsoft.AspNetCore.Components.ElementReference element) =>
        SafeVoidAsync(_ui, "scrollToBottom", element);

    public Task<string> GetBrowserLanguageAsync() => SafeAsync(_ui, "getBrowserLanguage", "nb-NO");

    public Task SetDocumentLanguageAsync(string code) => SafeVoidAsync(_ui, "setDocumentLanguage", code);

    public Task ReloadAsync() => SafeVoidAsync(_ui, "reload");

    // ---- session storage (welcome overlay shown once per tab) ----

    public async Task<bool> HasSeenWelcomeAsync()
    {
        try
        {
            return await _js.InvokeAsync<string?>("sessionStorage.getItem", WelcomeKey) == "1";
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
            return false;
        }
    }

    public async Task SetWelcomeSeenAsync(bool seen)
    {
        try
        {
            if (seen)
            {
                await _js.InvokeVoidAsync("sessionStorage.setItem", WelcomeKey, "1");
            }
            else
            {
                await _js.InvokeVoidAsync("sessionStorage.removeItem", WelcomeKey);
            }
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
            // Storage blocked (private mode): the overlay will simply show again.
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var module in new[] { _speech, _ui })
        {
            if (!module.IsValueCreated)
            {
                continue;
            }

            try
            {
                await (await module.Value).DisposeAsync();
            }
            catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException)
            {
                // Page is going away; nothing to clean up.
            }
        }
    }

    private static async Task<T> SafeAsync<T>(Lazy<Task<IJSObjectReference>> module, string function, T fallback, params object?[] args)
    {
        try
        {
            return await (await module.Value).InvokeAsync<T>(function, args);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            return fallback;
        }
    }

    private static async Task SafeVoidAsync(Lazy<Task<IJSObjectReference>> module, string function, params object?[] args)
    {
        try
        {
            await (await module.Value).InvokeVoidAsync(function, args);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            // Feature unavailable: ignore.
        }
    }
}
