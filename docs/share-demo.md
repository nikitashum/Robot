# Del ButikkPilot-demo med noen

## Raskest (én URL på din PC)

```powershell
.\scripts\run-demo.ps1
```

Åpne URL-en som vises i terminalen (vanligvis `https://localhost:7028` eller `http://localhost:5107`).

- **Send til kollega på samme nett:** bruk maskinens LAN-IP i stedet for `localhost` (åpne brannmur for porten om nødvendig).
- **Send til noen på internett:** [Cloudflare Tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/) eller ngrok mot samme port.

## Hva de ser

1. Velkomstkort: «nettbrett på butikkrobot».
2. **Start guidet demo (~90 sek)** — fire steg uten README.
3. Språkvalg (assistent svarer på valgt språk).

Tving velkomst igjen: legg til `?kiosk=1` på URL.

## Zip / USB

```powershell
.\scripts\publish.ps1 -SelfContained
```

Zip mappen `artifacts\demo` og send den. Mottaker pakker ut og dobbeltklikker `Start-Demo.cmd`, deretter åpner
`http://localhost:5107` (Windows x64, ingenting må installeres; ca. 110 MB utpakket). Uten `-SelfContained` er mappen liten, men
krever [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0): `dotnet ButikkPilot.Api.dll`.

## Tablet / telefon

- Chrome eller Edge anbefales (tale).
- **Legg til på Hjem-skjerm** (PWA) for fullskjermsfølelse.
- Hold telefonen/portrett eller nettbrett — UI er touch-optimalisert.

## Azure (offentlig lenke)

Se [azure-deploy.md](azure-deploy.md). Ett enkelt container-image (`Dockerfile`) inneholder både API og UI.

## Kort talk track (30 sek)

- «Dette er det kunden ser på skjermen til en tjenesterobot eller et butikknettbrett.»
- «Produktdata kommer fra katalogen — ikke gjetning.»
- «Robott-ledelse er simulert; samme backend kan kobles til Pudu senere.»
