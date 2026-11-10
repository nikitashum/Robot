using System.Globalization;
using ButikkPilot.Contracts;

namespace ButikkPilot.Infrastructure.Llm;

/// <summary>Canned replies for the offline stub assistant in all supported languages.</summary>
internal static class StubLocale
{
    public static string Money(decimal value) =>
        value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '\u00A0') + "\u00A0NOK";

    /// <summary>True when the assistant just asked the standard gaming-laptop follow-up (any supported language).</summary>
    public static bool IsLaptopDetailsPrompt(string? assistantText) =>
        assistantText is not null && (
            assistantText.Contains("gaming laptop around", StringComparison.OrdinalIgnoreCase)
            || assistantText.Contains("gaming-laptop rundt", StringComparison.OrdinalIgnoreCase)
            || assistantText.Contains("laptop gamingowy w okolicy", StringComparison.OrdinalIgnoreCase)
            || assistantText.Contains("Which games do you play", StringComparison.OrdinalIgnoreCase)
            || assistantText.Contains("Hvilke spill spiller du", StringComparison.OrdinalIgnoreCase)
            || assistantText.Contains("W jakie gry grasz", StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the assistant offered shelf directions for the products just shown.</summary>
    public static bool IsProductLocationOffer(string? assistantText) =>
        assistantText is not null && (
            assistantText.Contains("Would you like to know where they are", StringComparison.OrdinalIgnoreCase)
            || assistantText.Contains("where the first one is", StringComparison.OrdinalIgnoreCase)
            || assistantText.Contains("Vil du vite hvor de står", StringComparison.OrdinalIgnoreCase)
            || assistantText.Contains("Skal jeg vise hvor den første står", StringComparison.OrdinalIgnoreCase)
            || assistantText.Contains("Chcesz wiedzieć, gdzie stoją", StringComparison.OrdinalIgnoreCase)
            || assistantText.Contains("Pokazać, gdzie stoi pierwszy", StringComparison.OrdinalIgnoreCase));

    public static string AskLaptopDetails(string lang, decimal? budget) => Pick(lang,
        nb: budget is { } b
            ? $"Kult, en gaming-laptop rundt {Money(b)}! Hvilke spill spiller du, og vil du helst ha den lett eller mest mulig kraftig?"
            : "Gjerne! Hva er budsjettet ditt, og hvilke spill eller oppgaver skal laptopen brukes til?",
        en: budget is { } b2
            ? $"Great, a gaming laptop around {Money(b2)}! Which games do you play, and do you prefer it light or as powerful as possible?"
            : "Happy to help! What is your budget, and which games or tasks will the laptop be used for?",
        pl: budget is { } b3
            ? $"Świetnie, laptop gamingowy w okolicy {Money(b3)}! W jakie gry grasz i czy wolisz lekki, czy jak najmocniejszy?"
            : "Chętnie pomogę! Jaki masz budżet i do jakich gier lub zadań ma służyć laptop?");

    public static string LaptopPicks(string lang, decimal budget, IReadOnlyList<string> lines) => Pick(lang,
        nb: $"Her er de beste gaming-laptopene innenfor {Money(budget)}:\n{Join(lines)}\nSkal jeg vise hvor den første står, eller la roboten guide deg dit?",
        en: $"Here are the best gaming laptops within {Money(budget)}:\n{Join(lines)}\nShall I show where the first one is, or let the robot guide you there?",
        pl: $"Oto najlepsze laptopy gamingowe do {Money(budget)}:\n{Join(lines)}\nPokazać, gdzie stoi pierwszy, albo zaprowadzić Cię tam robotem?");

    public static string LaptopPicksRefined(string lang, decimal budget, string usage, string topPick, IReadOnlyList<string> lines) => Pick(lang,
        nb: $"Takk — {usage}. Innenfor {Money(budget)} passer disse best; jeg vil fremheve {topPick} for esportspill og jevn bildefrekvens:\n{Join(lines)}\nVil du vite hvor den første står, eller få en simulert robotguide?",
        en: $"Thanks — {usage}. Within {Money(budget)}, these still fit best; I'd lean toward the {topPick} for esports and smooth frame rates:\n{Join(lines)}\nWant directions to the first one, or a simulated robot guide?",
        pl: $"Dzięki — {usage}. W budżecie {Money(budget)} te modele nadal pasują; na esport typu Fortnite/FC poleciłbym {topPick}:\n{Join(lines)}\nPokazać lokalizację pierwszego, czy symulować robot?");

    public static string MonitorPicks(string lang, decimal budget, IReadOnlyList<string> lines) => Pick(lang,
        nb: $"Gaming-skjermer innenfor {Money(budget)}:\n{Join(lines)}\nVil du vite hvor de står?",
        en: $"Gaming monitors within {Money(budget)}:\n{Join(lines)}\nWould you like to know where they are?",
        pl: $"Monitory gamingowe do {Money(budget)}:\n{Join(lines)}\nChcesz wiedzieć, gdzie stoją?");

    public static string NoMatch(string lang, decimal budget) => Pick(lang,
        nb: $"Jeg fant ingenting på lager innenfor {Money(budget)}. Vil du øke budsjettet litt, eller snakke med en medarbeider?",
        en: $"I found nothing in stock within {Money(budget)}. Would you like to raise the budget a bit, or talk to a staff member?",
        pl: $"Nie znalazłem nic dostępnego do {Money(budget)}. Podnieść budżet czy porozmawiać z pracownikiem?");

    public static string Help(string lang) => Pick(lang,
        nb: "Jeg kan hjelpe deg å finne gaming-laptop eller gaming-skjerm, vise hvor produktene står, la roboten guide deg, eller hente en medarbeider. Hva vil du ha hjelp med?",
        en: "I can help you find a gaming laptop or gaming monitor, show where products are, let the robot guide you, or call a staff member. What do you need?",
        pl: "Pomogę znaleźć laptop lub monitor gamingowy, pokażę, gdzie leżą produkty, poprowadzę Cię robotem lub zawołam pracownika. W czym mogę pomóc?");

    public static string AnythingElse(string lang) => Pick(lang,
        nb: "Noe mer jeg kan hjelpe med? Jeg kan sammenligne, vise veien eller hente en medarbeider.",
        en: "Anything else I can help with? I can compare, show the way, or call a staff member.",
        pl: "Czy mogę w czymś jeszcze pomóc? Mogę porównać, pokazać drogę lub zawołać pracownika.");

    public static string NeedProductFirst(string lang) => Pick(lang,
        nb: "Jeg må først finne et produkt til deg. Fortell meg hva du leter etter, så viser jeg deg hvor det står.",
        en: "I first need to find a product for you. Tell me what you're looking for and I'll show you where it is.",
        pl: "Najpierw muszę znaleźć dla Ciebie produkt. Powiedz, czego szukasz, a pokażę Ci, gdzie jest.");

    public static string Location(string lang, string shelf, string aisle, string department, string directions) => Pick(lang,
        nb: $"Du finner den i {department}, {aisle}, {shelf}. {directions}",
        en: $"You'll find it in {department}, {aisle}, {shelf}. {directions}",
        pl: $"Znajdziesz go w dziale {department}, {aisle}, {shelf}. {directions}");

    public static string Guide(string lang, int seconds, string shelf) => Pick(lang,
        nb: $"Roboten kjører deg til {shelf} (ca. {seconds} sekunder). Følg den lysende ruten på kartet.",
        en: $"The robot will take you to {shelf} (about {seconds} seconds). Follow the lit-up route on the map.",
        pl: $"Robot zaprowadzi Cię do {shelf} (ok. {seconds} sekund). Podążaj za podświetloną trasą na mapie.");

    public static string Handoff(string lang) => Pick(lang,
        nb: "Greit! (Demo) I en ekte butikk ville en medarbeider blitt varslet nå og kommet til deg.",
        en: "Sure! (Demo) In a real store a staff member would be alerted now and come to you.",
        pl: "Jasne! (Demo) W prawdziwym sklepie pracownik zostałby teraz powiadomiony i do Ciebie przyszedł.");

    public static string ToolFailed(string lang) => Pick(lang,
        nb: "Beklager, jeg klarte ikke å slå opp det akkurat nå. Vil du at jeg skal hente en medarbeider?",
        en: "Sorry, I couldn't look that up right now. Would you like me to call a staff member?",
        pl: "Przepraszam, nie udało się tego teraz sprawdzić. Czy zawołać pracownika?");

    private static string Join(IReadOnlyList<string> lines) => string.Join('\n', lines);

    private static string Pick(string language, string nb, string en, string pl) =>
        SupportedLanguages.Normalize(language) switch
        {
            "en-US" => en,
            "pl-PL" => pl,
            _ => nb,
        };
}
