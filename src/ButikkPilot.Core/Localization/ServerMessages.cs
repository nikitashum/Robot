using ButikkPilot.Contracts;

namespace ButikkPilot.Core.Localization;

/// <summary>The few strings the server itself must produce (everything else comes from the LLM or the UI).</summary>
public static class ServerMessages
{
    public static string ServiceBusy(string language) => Pick(language,
        nb: "Beklager, AI-assistenten er midlertidig utilgjengelig. Prøv igjen om litt, eller spør en medarbeider i butikken.",
        en: "Sorry, the AI assistant is temporarily unavailable. Please try again shortly, or ask a staff member in the store.",
        pl: "Przepraszamy, asystent AI jest chwilowo niedostępny. Spróbuj ponownie za chwilę lub zapytaj pracownika sklepu.");

    public static string EmptyReply(string language) => Pick(language,
        nb: "Beklager, jeg fikk ikke til å svare akkurat nå. Kan du prøve å formulere det på en annen måte?",
        en: "Sorry, I couldn't answer that just now. Could you try rephrasing?",
        pl: "Przepraszamy, nie udało mi się teraz odpowiedzieć. Spróbuj sformułować pytanie inaczej.");

    public static string NeedMoreInfo(string language) => Pick(language,
        nb: "Jeg trenger litt mer informasjon for å hjelpe deg videre. Kan du presisere budsjett og hva du skal bruke produktet til?",
        en: "I need a bit more information to help you. Could you tell me your budget and what you'll use the product for?",
        pl: "Potrzebuję trochę więcej informacji. Podaj proszę budżet i do czego będziesz używać produktu.");

    public static string ShelfWord(string language) => Pick(language, nb: "Hylle", en: "Shelf", pl: "Półka");

    private static string Pick(string language, string nb, string en, string pl) =>
        SupportedLanguages.Normalize(language) switch
        {
            "en-US" => en,
            "pl-PL" => pl,
            _ => nb,
        };
}
