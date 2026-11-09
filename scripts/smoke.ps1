# Starts a published demo and proves that every file the browser may request is actually served
# (the class of bug where one unmapped extension, e.g. ICU .dat, turned the whole demo into a blank page).
#   .\scripts\smoke.ps1 [-Folder artifacts/demo] [-Port 5199]
param(
    [string]$Folder = "artifacts/demo",
    [int]$Port = 5199
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dir = Join-Path $root $Folder
$dll = Join-Path $dir "ButikkPilot.Api.dll"
if (-not (Test-Path -LiteralPath $dll)) { throw "Nothing to test at $dir. Run scripts/publish.ps1 first." }

$base = "http://127.0.0.1:$Port"
$env:ASPNETCORE_URLS = $base
$env:ASPNETCORE_ENVIRONMENT = "Production"
$onWindows = ($PSVersionTable.PSEdition -eq "Desktop") -or $IsWindows
$startArgs = @{ FilePath = "dotnet"; ArgumentList = "`"$dll`""; WorkingDirectory = $dir; PassThru = $true }
if ($onWindows) { $startArgs.WindowStyle = "Hidden" }
$proc = Start-Process @startArgs

try {
    $ready = $false
    for ($i = 0; $i -lt 60 -and -not $ready; $i++) {
        Start-Sleep -Milliseconds 500
        try { $ready = (Invoke-RestMethod "$base/health").status -eq "ok" } catch { }
    }
    if (-not $ready) { throw "API did not become healthy." }

    $failures = @()
    $wwwroot = Join-Path $dir "wwwroot"
    $files = Get-ChildItem -LiteralPath $wwwroot -Recurse -File
    foreach ($file in $files) {
        $rel = $file.FullName.Substring($wwwroot.Length).Replace('\', '/')
        $url = $base + (($rel -split '/' | ForEach-Object { [uri]::EscapeDataString($_) }) -join '/')
        try {
            $r = Invoke-WebRequest $url -UseBasicParsing
            if (-not $r.Headers["Content-Type"]) { $failures += "$rel -> no Content-Type" }
        } catch {
            $failures += "$rel -> $($_.Exception.Message)"
        }
    }

    $chat = Invoke-RestMethod "$base/api/chat" -Method Post -ContentType "application/json" `
        -Body (@{ message = "Do you have a 27-inch gaming monitor under 5000 NOK?"; language = "en-US" } | ConvertTo-Json)
    if (-not $chat.recommendedProducts -or $chat.recommendedProducts.Count -eq 0) { $failures += "chat returned no products" }

    Write-Host "Checked $($files.Count) static files + health + chat."
    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Host "FAIL $_" -ForegroundColor Red }
        exit 1
    }
    Write-Host "OK" -ForegroundColor Green
}
finally {
    if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
}
