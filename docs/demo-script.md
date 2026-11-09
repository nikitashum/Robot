# ButikkPilot AI — demoscenarie for retailerpitch

## Start (én URL)

```powershell
.\scripts\run-demo.ps1
```

Åpne lenken i nettleser. Trykk **Start guidet demo (~90 sek)** — ingen README nødvendig.

Send til andre: [share-demo.md](share-demo.md).

## Mål

Vise det kunden ville sett på et **nettbrett på en tjenesterobot** i butikken: tale/tekst, anbefalinger fra katalog, plassering, simulert guiding.

## Guidet demo (app gjør mesteparten)

| Steg | Stegper | Innhold |
|------|---------|---------|
| 1 | Hei | Gaming laptop 10 000 kr + oppfølging om spill |
| 2 | Anbefaling | Tre produktkort fra katalog |
| 3 | Plassering | Velg kort → hylla i butikk |
| 4 | Robott | Simulert rute på kart |

Etterpå: bytt språk (EN/PL), trykk forslagschips, eller «Tilkall hjelp».

## Manuell dialog (valgfritt)

| Kunde | Forventet assistent |
|-------|---------------------|
| Gaming laptop 10 000 kr | Oppfølging, deretter tre anbefalinger |
| Klikk produktkort | Veibeskrivelse |
| Simuler robott | Steg + kart |
| Tilkall medarbeider | Handoff |

## Poenger for forhandler

- Produktfakta fra verktøy, ikke gjetting
- Samme backend → Pudu SDK senere
- Ikke ekte POS/lager i demo

## Feilsøking

- **Blank side:** kjør `scripts\run-demo.ps1` (bygger UI + API til `artifacts\demo`), eller `scripts\smoke.ps1` for å se hvilken fil som feiler
- **API-feil:** sjekk at `data/products.json` følger API-build
- **Språk:** Stub støtter nb/en/pl uten nøkkel
