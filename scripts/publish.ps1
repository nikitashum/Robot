# Builds the complete single-URL demo (API + Blazor UI + data) into one folder.
#   .\scripts\publish.ps1                    -> artifacts\demo   (needs the .NET 10 runtime to run)
#   .\scripts\publish.ps1 -SelfContained     -> artifacts\demo   (runs on a PC with nothing installed, win-x64)
param(
    [string]$Output = "artifacts/demo",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$web = Join-Path $root "artifacts/_web"
$out = Join-Path $root $Output
foreach ($dir in @($web, $out)) {
    if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Write-Host "1/3 Publishing UI (Blazor WASM, trimmed)..."
dotnet publish src/ButikkPilot.Web -c Release -o $web --nologo
if ($LASTEXITCODE -ne 0) { throw "UI publish failed." }

Write-Host "2/3 Publishing API..."
$apiArgs = @("publish", "src/ButikkPilot.Api", "-c", "Release", "-o", $out, "--nologo")
if ($SelfContained) { $apiArgs += @("-r", "win-x64", "--self-contained", "true") }
dotnet @apiArgs
if ($LASTEXITCODE -ne 0) { throw "API publish failed." }

Write-Host "3/3 Combining..."
Copy-Item -LiteralPath (Join-Path $web "wwwroot") -Destination (Join-Path $out "wwwroot") -Recurse
Remove-Item -LiteralPath $web -Recurse -Force

if (-not (Test-Path -LiteralPath (Join-Path $out "wwwroot/index.html"))) { throw "wwwroot/index.html missing." }
if (-not (Test-Path -LiteralPath (Join-Path $out "data/products.json"))) { throw "data/products.json missing." }

if ($SelfContained) {
    Set-Content -LiteralPath (Join-Path $out "Start-Demo.cmd") -Encoding ASCII -Value @(
        "@echo off",
        "echo ButikkPilot demo: open http://localhost:5107 in your browser (Ctrl+C to stop)",
        "set ASPNETCORE_URLS=http://localhost:5107",
        "ButikkPilot.Api.exe")
}

$size = [math]::Round((Get-ChildItem -LiteralPath $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host ""
Write-Host "Done: $out ($size MB)"
