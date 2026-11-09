namespace ButikkPilot.Contracts;

public sealed record LanguageInfo(string Code, string NativeName, string EnglishName);

/// <summary>
/// Allow-list of UI/assistant languages. The server never trusts a client-supplied language string
/// (it would otherwise flow into the LLM prompt), it only accepts values that map into this list.
/// </summary>
public static class SupportedLanguages
{
    public const string Default = "nb-NO";

    public static readonly IReadOnlyList<LanguageInfo> All =
    [
        new("nb-NO", "Norsk", "Norwegian (Bokmal)"),
        new("en-US", "English", "English"),
        new("pl-PL", "Polski", "Polish"),
    ];

    public static LanguageInfo Get(string? code) => All.First(l => l.Code == Normalize(code));

    /// <summary>Maps any input ("nb", "NB-no", "en", "xx'; ignore rules") to a supported code, else <see cref="Default"/>.</summary>
    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 16)
        {
            return Default;
        }

        var exact = All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact.Code;
        }

        var primary = code.Split('-', '_')[0];
        var byPrimary = All.FirstOrDefault(l =>
            l.Code.StartsWith(primary + "-", StringComparison.OrdinalIgnoreCase)
            || (primary.Equals("no", StringComparison.OrdinalIgnoreCase) && l.Code == "nb-NO")
            || (primary.Equals("nn", StringComparison.OrdinalIgnoreCase) && l.Code == "nb-NO"));
        return byPrimary?.Code ?? Default;
    }
}
