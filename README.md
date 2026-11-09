# ButikkPilot AI

Norsk AI-salgsassistent for elektronikkbutikker — **kiosk-demo**: ett vindu, guidet 90-sekunders historie («nettbrett på butikkrobot»), flerspråklig assistent, produktkort fra katalog, simulert robott-ledelse.

## Start demo (anbefalt — én URL)

```powershell
.\scripts\run-demo.ps1
```

Åpne URL i nettleser (telefon/nettbrett OK). Del med andre: [docs/share-demo.md](docs/share-demo.md).

Krever [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (.NET 8 går ut av støtte 10. nov 2026).

## Utvikling (delt API + Web)

```bash
dotnet test ButikkPilot.slnx
dotnet run --project src/ButikkPilot.Api     # http://localhost:5107
dotnet run --project src/ButikkPilot.Web     # hot reload for UI
```

Web bruker `http://localhost:5107` via `appsettings.Development.json` når den kjøres alene (filen publiseres aldri).

## Arkitektur

- **ButikkPilot.Contracts** — wire-DTOer delt av API og UI (ingen avhengigheter, source-generated JSON)
- **ButikkPilot.Core** — samtale, verktøy, domene (ingen I/O)
- **ButikkPilot.Infrastructure** — JSON-data, sesjoner, Stub/OpenAI/Azure OpenAI, analytics
- **ButikkPilot.RobotAdapter** — maskinvare-skjøten (`IRobotNavigation`; simulert nå, Pudu senere)
- **ButikkPilot.Api** — composition root, rate limiting, sikkerhetshoder, serverer `wwwroot`
- **ButikkPilot.Web** — Blazor WASM kiosk-UI (refererer kun Contracts)

## Bygg og publiser

```powershell
.\scripts\publish.ps1                 # artifacts\demo (API + UI + data)
.\scripts\publish.ps1 -SelfContained  # kjører uten .NET installert (win-x64)
.\scripts\smoke.ps1                   # starter publisert demo og sjekker at alle filer serveres
docker build -t butikkpilot .
```

CI: `.github/workflows/ci.yml` (build med warnings-as-errors, tester, sårbare pakker feiler bygget, publish + smoke, Docker).

## Språk

Velg språk i UI (nb / en / pl). `ChatRequest.Language` styrer LLM-systemprompt; **Stub** støtter offline demo uten API-nøkkel.

## OpenAI (valgfritt)

```bash
cd src/ButikkPilot.Api
dotnet user-secrets init
dotnet user-secrets set "Llm:Provider" "OpenAI"      # eller AzureOpenAI (+ Llm:Endpoint)
dotnet user-secrets set "Llm:Model" "<modell eller deployment>"
dotnet user-secrets set "Llm:ApiKey" "<nøkkel>"
```

Feil konfigurasjon stopper oppstart (ingen stille fallback til Stub). Daglig tak: `Llm:DailyRequestLimit` (standard 300).
I produksjon: miljøvariabler / Container Apps secrets (`Llm__ApiKey`), aldri i git eller image.

## Dokumentasjon

- [docs/demo-script.md](docs/demo-script.md) — pitch-dialog
- [docs/share-demo.md](docs/share-demo.md) — zip, tunnel, tablet
- [docs/azure-deploy.md](docs/azure-deploy.md) — billig, kostnadsbegrenset Azure-deploy (les «hard requirements»: kun én replika)

## Sikkerhet

- Ingen nøkler i git eller image; fail-fast konfigurasjon
- Rate limit per klient + global samtidighetsgrense + daglig LLM-tak
- Strenge inputgrenser (16 KB body, 800 tegn), tillatelsesliste for språk (klientstreng når aldri prompten)
- CSP, nosniff, no-referrer, X-Frame-Options, HSTS (utenom Development), ingen `Server`-header
- Fritekst fra «snakk med ansatt» lagres/logges aldri
- Demonstrasjonskatalog, simulert robot
