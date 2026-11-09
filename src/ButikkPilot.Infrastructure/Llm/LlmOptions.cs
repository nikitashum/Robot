using Microsoft.Extensions.Options;

namespace ButikkPilot.Infrastructure.Llm;

public enum LlmProvider
{
    /// <summary>Deterministic offline assistant. Free, no network. For demos without a key and for tests.</summary>
    Stub,

    /// <summary>OpenAI (or any OpenAI-compatible) chat completions endpoint.</summary>
    OpenAI,

    /// <summary>Azure OpenAI: Endpoint = https://NAME.openai.azure.com, Model = DEPLOYMENT name.</summary>
    AzureOpenAI,
}

public sealed class LlmOptions
{
    public const string Section = "Llm";

    public LlmProvider Provider { get; set; } = LlmProvider.Stub;

    /// <summary>Secret. Supply via environment variable (Llm__ApiKey) or a secret store, never in appsettings.json.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Optional for OpenAI (defaults to api.openai.com/v1), required for Azure.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Model name (OpenAI) or deployment name (Azure).</summary>
    public string Model { get; set; } = "gpt-4o-mini";

    public string ApiVersion { get; set; } = "2024-10-21";

    /// <summary>Hard cap on generated tokens per call (cost control).</summary>
    public int MaxCompletionTokens { get; set; } = 600;

    public int AttemptTimeoutSeconds { get; set; } = 20;

    /// <summary>Max LLM calls per UTC day across all visitors. 0 = unlimited (not recommended on a public URL).</summary>
    public int DailyRequestLimit { get; set; } = 300;
}

public sealed class LlmOptionsValidator : IValidateOptions<LlmOptions>
{
    public ValidateOptionsResult Validate(string? name, LlmOptions o)
    {
        var errors = new List<string>();

        if (o.Provider != LlmProvider.Stub)
        {
            if (string.IsNullOrWhiteSpace(o.ApiKey))
            {
                errors.Add($"Llm:ApiKey is required for provider {o.Provider} (set env var Llm__ApiKey). Refusing to silently fall back to the stub.");
            }

            if (string.IsNullOrWhiteSpace(o.Model))
            {
                errors.Add("Llm:Model is required.");
            }
        }

        if (o.Provider == LlmProvider.AzureOpenAI
            && (!Uri.TryCreate(o.Endpoint, UriKind.Absolute, out var azure) || azure.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("Llm:Endpoint must be an https URL for AzureOpenAI, e.g. https://my-resource.openai.azure.com");
        }

        if (!string.IsNullOrWhiteSpace(o.Endpoint) && (!Uri.TryCreate(o.Endpoint, UriKind.Absolute, out var ep) || ep.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("Llm:Endpoint must be an absolute https URL.");
        }

        if (o.MaxCompletionTokens is < 50 or > 4000)
        {
            errors.Add("Llm:MaxCompletionTokens must be between 50 and 4000.");
        }

        if (o.AttemptTimeoutSeconds is < 3 or > 120)
        {
            errors.Add("Llm:AttemptTimeoutSeconds must be between 3 and 120.");
        }

        if (o.DailyRequestLimit < 0)
        {
            errors.Add("Llm:DailyRequestLimit must be >= 0.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
