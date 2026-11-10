using ButikkPilot.Contracts;

namespace ButikkPilot.Web.Services;

/// <summary>
/// All UI strings for one language. Every property is <c>required</c>, so adding a string without translating it
/// into EVERY language is a compile error (the previous version silently showed Norwegian in the Polish UI).
/// </summary>
public sealed class UiText
{
    public required string Code { get; init; }

    // Chrome
    public required string Tagline { get; init; }
    public required string StatusStrip { get; init; }
    public required string HelpLabel { get; init; }
    public required string LanguageLabel { get; init; }
    public required string ShowMap { get; init; }
    public required string VoiceLabel { get; init; }
    public required string MicTitle { get; init; }
    public required string PriceSuffix { get; init; }

    // Welcome overlay
    public required string WelcomeTitle { get; init; }
    public required string WelcomeLead { get; init; }
    public required string Bullet1 { get; init; }
    public required string Bullet2 { get; init; }
    public required string Bullet3 { get; init; }
    public required string DemoDisclaimer { get; init; }
    public required string StartGuided { get; init; }
    public required string ExploreSelf { get; init; }

    // Chat
    public required string GreetingChat { get; init; }
    public required string ChatLogLabel { get; init; }
    public required string Thinking { get; init; }
    public required string Send { get; init; }
    public required string InputPlaceholder { get; init; }
    public required string SuggestionGaming { get; init; }
    public required string SuggestionMonitor { get; init; }
    public required string SuggestionStaff { get; init; }

    // Messages the demo/chips send on the customer's behalf (also what the stub understands)
    public required string AskGaming { get; init; }
    public required string AskMonitor { get; init; }
    public required string AskStaff { get; init; }
    public required string AnswerUsage { get; init; }
    public required string AskWhere { get; init; }
    public required string AskGuide { get; init; }
    public required string AskWhereProduct { get; init; }

    // Side panel
    public required string Recommendations { get; init; }
    public required string InStock { get; init; }
    public required string OutOfStock { get; init; }
    public required string Shelf { get; init; }
    public required string FindInStore { get; init; }
    public required string RobotGuide { get; init; }
    public required string SecondsShort { get; init; }
    public required string HandoffNotice { get; init; }
    public required string StoreMap { get; init; }
    public required string Entrance { get; init; }
    public required string GuideStart { get; init; }
    public required string GuideDepartment { get; init; }
    public required string GuideAisle { get; init; }
    public required string GuideArrive { get; init; }

    // Guided demo
    public required string StepsLabel { get; init; }
    public required string StepGreet { get; init; }
    public required string StepRecommend { get; init; }
    public required string StepLocate { get; init; }
    public required string StepRobot { get; init; }
    public required string Pause { get; init; }
    public required string Resume { get; init; }
    public required string SkipStep { get; init; }
    public required string StopDemo { get; init; }
    public required string Replay { get; init; }
    public required string DemoDone { get; init; }
    public required string DemoFailed { get; init; }
    public required string DemoYourTurn { get; init; }
    public required string DemoPaused { get; init; }

    // Errors
    public required string ErrorGeneric { get; init; }
    public required string ErrorRateLimited { get; init; }
    public required string ErrorOffline { get; init; }
    public required string FatalTitle { get; init; }
    public required string FatalBody { get; init; }
    public required string FatalReload { get; init; }
    public required string FatalDetails { get; init; }

    public string FormatPrice(decimal value)
    {
        var digits = ((long)Math.Round(value)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var grouped = new System.Text.StringBuilder();
        for (var i = 0; i < digits.Length; i++)
        {
            if (i > 0 && (digits.Length - i) % 3 == 0)
            {
                grouped.Append('\u00A0');
            }

            grouped.Append(digits[i]);
        }

        return $"{grouped}\u00A0{PriceSuffix}";
    }

    public string FormatStep(GuideStep step) => step.Kind switch
    {
        GuideStepKind.Start => GuideStart,
        GuideStepKind.Department => string.Format(System.Globalization.CultureInfo.InvariantCulture, GuideDepartment, step.Label),
        GuideStepKind.Aisle => string.Format(System.Globalization.CultureInfo.InvariantCulture, GuideAisle, step.Label),
        GuideStepKind.Arrive => string.Format(System.Globalization.CultureInfo.InvariantCulture, GuideArrive, step.Label),
        _ => step.Label,
    };

    public string ErrorFor(ApiErrorKind kind) => kind switch
    {
        ApiErrorKind.RateLimited => ErrorRateLimited,
        ApiErrorKind.Offline => ErrorOffline,
        _ => ErrorGeneric,
    };

    public static UiText For(string? languageCode) => SupportedLanguages.Normalize(languageCode) switch
    {
        "en-US" => English,
        "pl-PL" => Polish,
        _ => Norwegian,
    };

    public static readonly UiText Norwegian = new()
    {
        Code = "nb-NO",
        Tagline = "AI-salgsassistent for elektronikk — kundeopplevelse i butikk.",
        StatusStrip = "ButikkPilot · Demonstrasjon",
        HelpLabel = "Vis introduksjonen igjen",
        LanguageLabel = "Språk",
        ShowMap = "Vis kart",
        VoiceLabel = "Les svar høyt",
        MicTitle = "Snakk (nettleseren kan sende lyd til leverandøren)",
        PriceSuffix = "kr",

        WelcomeTitle = "Velkommen til ButikkPilot",
        WelcomeLead = "Dette er kundegrensesnittet til en AI-assistent i butikk: anbefalinger fra katalogen, plassering og simulert veiledning. Prøv selv, eller følg den guidede gjennomgangen (ca. 90 sekunder).",
        Bullet1 = "Spør om en gaming-laptop eller skjerm, med eget budsjett",
        Bullet2 = "Få anbefalinger innenfor budsjettet, og se hvor varen står",
        Bullet3 = "Se simulert veiledning til hyllen (klargjort for robott-integrasjon)",
        DemoDisclaimer = "Demonstrasjonskatalog, ikke ekte butikkdata. Ingen roboter eller medarbeidere blir varslet.",
        StartGuided = "Start guidet demo (90 sek)",
        ExploreSelf = "Utforsk selv",

        GreetingChat = "Hei! Jeg er ButikkPilot. Hva leter du etter i dag?",
        ChatLogLabel = "Samtale",
        Thinking = "Tenker …",
        Send = "Send",
        InputPlaceholder = "Skriv eller snakk …",
        SuggestionGaming = "Gaming-laptop ~10 000 kr",
        SuggestionMonitor = "Gaming-skjerm",
        SuggestionStaff = "Snakk med en medarbeider",

        AskGaming = "Jeg trenger en gaming-laptop til rundt 10 000 kr.",
        AskMonitor = "Har dere en 27\" gaming-skjerm under 5000 kr?",
        AskStaff = "Jeg vil snakke med en medarbeider.",
        AnswerUsage = "Jeg spiller Fortnite og FC 25, helst 15 tommer skjerm.",
        AskWhere = "Hvor finner jeg den første?",
        AskGuide = "Kan roboten guide meg dit?",
        AskWhereProduct = "Hvor finner jeg {0}?",

        Recommendations = "Anbefalinger",
        InStock = "På lager",
        OutOfStock = "Ikke på lager",
        Shelf = "Hylle",
        FindInStore = "Slik finner du den",
        RobotGuide = "Robotguide (simulert)",
        SecondsShort = "sek",
        HandoffNotice = "Demo: i en ekte butikk ville en medarbeider blitt varslet nå.",
        StoreMap = "Butikkart",
        Entrance = "Inngang",
        GuideStart = "Roboten starter ved inngangen",
        GuideDepartment = "Kjører mot {0}",
        GuideAisle = "Svinger inn i {0}",
        GuideArrive = "Stopper ved {0}",

        StepsLabel = "Demo-fremdrift",
        StepGreet = "Hei",
        StepRecommend = "Anbefaling",
        StepLocate = "Finn varen",
        StepRobot = "Robotguide",
        Pause = "Pause",
        Resume = "Fortsett",
        SkipStep = "Hopp over",
        StopDemo = "Avslutt",
        Replay = "Spill av på nytt",
        DemoDone = "Demoen er ferdig. Prøv selv, eller spill den av på nytt.",
        DemoFailed = "Demoen stoppet på grunn av en feil.",
        DemoYourTurn = "Kundens tur — neste melding kommer straks …",
        DemoPaused = "Pause. Prøv chatten selv, eller trykk Fortsett.",

        ErrorGeneric = "Beklager, noe gikk galt. Prøv igjen.",
        ErrorRateLimited = "For mange meldinger på kort tid. Vent litt og prøv igjen.",
        ErrorOffline = "Får ikke kontakt med tjenesten. Sjekk nettverket og prøv igjen.",
        FatalTitle = "Noe gikk galt",
        FatalBody = "Siden støtte på en uventet feil. Last siden på nytt for å fortsette.",
        FatalReload = "Last på nytt",
        FatalDetails = "Tekniske detaljer",
    };

    public static readonly UiText English = new()
    {
        Code = "en-US",
        Tagline = "In-store AI sales assistant for electronics retail.",
        StatusStrip = "ButikkPilot · Demonstration",
        HelpLabel = "Show the introduction again",
        LanguageLabel = "Language",
        ShowMap = "Show map",
        VoiceLabel = "Read answers aloud",
        MicTitle = "Speak (your browser may send audio to its vendor)",
        PriceSuffix = "NOK",

        WelcomeTitle = "Welcome to ButikkPilot",
        WelcomeLead = "This is the customer-facing interface for an in-store AI assistant: catalog-backed recommendations, product location, and simulated wayfinding. Try it yourself, or follow the guided walkthrough (about 90 seconds).",
        Bullet1 = "Ask for a gaming laptop or monitor with your own budget",
        Bullet2 = "Get budget-aware recommendations and see where the product is shelved",
        Bullet3 = "View simulated guidance to the shelf (ready for robot integration)",
        DemoDisclaimer = "Demo catalog, not real store data. No robot or staff member is actually notified.",
        StartGuided = "Start guided demo (90 sec)",
        ExploreSelf = "Explore on my own",

        GreetingChat = "Hi! I'm ButikkPilot. What are you looking for today?",
        ChatLogLabel = "Conversation",
        Thinking = "Thinking …",
        Send = "Send",
        InputPlaceholder = "Type or speak …",
        SuggestionGaming = "Gaming laptop ~10,000 NOK",
        SuggestionMonitor = "Gaming monitor",
        SuggestionStaff = "Talk to a staff member",

        AskGaming = "I need a gaming laptop for around 10,000 NOK.",
        AskMonitor = "Do you have a 27-inch gaming monitor under 5000 NOK?",
        AskStaff = "I'd like to talk to a staff member.",
        AnswerUsage = "I play Fortnite and FC 25, preferably a 15-inch screen.",
        AskWhere = "Where can I find the first one?",
        AskGuide = "Can the robot guide me there?",
        AskWhereProduct = "Where can I find the {0}?",

        Recommendations = "Recommendations",
        InStock = "In stock",
        OutOfStock = "Out of stock",
        Shelf = "Shelf",
        FindInStore = "How to find it",
        RobotGuide = "Robot guide (simulated)",
        SecondsShort = "sec",
        HandoffNotice = "Demo: in a real store a staff member would be alerted now.",
        StoreMap = "Store map",
        Entrance = "Entrance",
        GuideStart = "The robot starts at the entrance",
        GuideDepartment = "Driving towards {0}",
        GuideAisle = "Turning into {0}",
        GuideArrive = "Stopping at {0}",

        StepsLabel = "Demo progress",
        StepGreet = "Hello",
        StepRecommend = "Recommendation",
        StepLocate = "Find it",
        StepRobot = "Robot guide",
        Pause = "Pause",
        Resume = "Resume",
        SkipStep = "Skip step",
        StopDemo = "Stop",
        Replay = "Replay",
        DemoDone = "The demo is finished. Try it yourself, or replay it.",
        DemoFailed = "The demo stopped because of an error.",
        DemoYourTurn = "Customer's turn — next message in a moment …",
        DemoPaused = "Paused. Try the chat yourself, or press Continue.",

        ErrorGeneric = "Sorry, something went wrong. Please try again.",
        ErrorRateLimited = "Too many messages in a short time. Please wait a moment and try again.",
        ErrorOffline = "Can't reach the service. Check your connection and try again.",
        FatalTitle = "Something went wrong",
        FatalBody = "The page hit an unexpected error. Reload to continue.",
        FatalReload = "Reload",
        FatalDetails = "Technical details",
    };

    public static readonly UiText Polish = new()
    {
        Code = "pl-PL",
        Tagline = "Asystent sprzedaży AI w sklepie z elektroniką.",
        StatusStrip = "ButikkPilot · Demonstracja",
        HelpLabel = "Pokaż wprowadzenie ponownie",
        LanguageLabel = "Język",
        ShowMap = "Pokaż mapę",
        VoiceLabel = "Czytaj odpowiedzi na głos",
        MicTitle = "Mów (przeglądarka może wysyłać dźwięk do swojego dostawcy)",
        PriceSuffix = "NOK",

        WelcomeTitle = "Witaj w ButikkPilot",
        WelcomeLead = "To interfejs dla klienta w sklepie: rekomendacje z katalogu, lokalizacja produktu i symulowane prowadzenie. Wypróbuj sam lub skorzystaj z przewodnika (ok. 90 s).",
        Bullet1 = "Zapytaj o laptop lub monitor gamingowy z własnym budżetem",
        Bullet2 = "Otrzymaj rekomendacje w budżecie i zobacz, gdzie leży produkt",
        Bullet3 = "Zobacz symulowane prowadzenie do półki (gotowe pod integrację z robotem)",
        DemoDisclaimer = "Katalog demonstracyjny, nie prawdziwe dane sklepu. Żaden robot ani pracownik nie jest faktycznie powiadamiany.",
        StartGuided = "Uruchom demo z przewodnikiem (90 s)",
        ExploreSelf = "Sprawdzę sam",

        GreetingChat = "Cześć! Jestem ButikkPilot. Czego szukasz dzisiaj?",
        ChatLogLabel = "Rozmowa",
        Thinking = "Myślę …",
        Send = "Wyślij",
        InputPlaceholder = "Wpisz lub powiedz …",
        SuggestionGaming = "Laptop gamingowy ~10 000 NOK",
        SuggestionMonitor = "Monitor gamingowy",
        SuggestionStaff = "Porozmawiaj z pracownikiem",

        AskGaming = "Potrzebuję laptopa gamingowego za około 10 000 NOK.",
        AskMonitor = "Czy macie monitor gamingowy 27\" do 5000 NOK?",
        AskStaff = "Chcę porozmawiać z pracownikiem.",
        AnswerUsage = "Gram w Fortnite i FC 25, najlepiej ekran 15 cali.",
        AskWhere = "Gdzie znajdę pierwszy?",
        AskGuide = "Czy robot może mnie tam zaprowadzić?",
        AskWhereProduct = "Gdzie znajdę {0}?",

        Recommendations = "Rekomendacje",
        InStock = "Dostępny",
        OutOfStock = "Niedostępny",
        Shelf = "Półka",
        FindInStore = "Jak go znaleźć",
        RobotGuide = "Przewodnik robot (symulacja)",
        SecondsShort = "s",
        HandoffNotice = "Demo: w prawdziwym sklepie pracownik zostałby teraz powiadomiony.",
        StoreMap = "Mapa sklepu",
        Entrance = "Wejście",
        GuideStart = "Robot rusza od wejścia",
        GuideDepartment = "Jedzie w stronę: {0}",
        GuideAisle = "Skręca do: {0}",
        GuideArrive = "Zatrzymuje się przy: {0}",

        StepsLabel = "Postęp dema",
        StepGreet = "Powitanie",
        StepRecommend = "Rekomendacja",
        StepLocate = "Znajdź produkt",
        StepRobot = "Przewodnik robot",
        Pause = "Pauza",
        Resume = "Wznów",
        SkipStep = "Pomiń krok",
        StopDemo = "Zakończ",
        Replay = "Odtwórz ponownie",
        DemoDone = "Demo zakończone. Spróbuj sam lub odtwórz je ponownie.",
        DemoFailed = "Demo zatrzymało się z powodu błędu.",
        DemoYourTurn = "Tura klienta — za chwilę kolejna wiadomość …",
        DemoPaused = "Pauza. Napisz coś sam lub naciśnij Kontynuuj.",

        ErrorGeneric = "Przepraszamy, coś poszło nie tak. Spróbuj ponownie.",
        ErrorRateLimited = "Zbyt wiele wiadomości w krótkim czasie. Poczekaj chwilę i spróbuj ponownie.",
        ErrorOffline = "Brak połączenia z usługą. Sprawdź sieć i spróbuj ponownie.",
        FatalTitle = "Coś poszło nie tak",
        FatalBody = "Strona napotkała nieoczekiwany błąd. Odśwież, aby kontynuować.",
        FatalReload = "Odśwież",
        FatalDetails = "Szczegóły techniczne",
    };
}
