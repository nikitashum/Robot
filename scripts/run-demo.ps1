# One URL: builds the demo (API + UI) and starts it. Open http://localhost:5107
param(
    [int]$Port = 5107,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $SkipPublish -or -not (Test-Path "artifacts/demo/ButikkPilot.Api.dll")) {
    & "$PSScriptRoot/publish.ps1"
}

Write-Host ""
Write-Host "Demo running at http://localhost:$Port  (Ctrl+C to stop)"
Write-Host "Same Wi-Fi? Use this PC's LAN IP instead of localhost and allow the port in the firewall."
Write-Host ""

$env:ASPNETCORE_URLS = "http://0.0.0.0:$Port"
dotnet artifacts/demo/ButikkPilot.Api.dll
