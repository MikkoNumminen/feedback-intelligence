<#
.SYNOPSIS
Assembles the static frontend bundle for Azure Static Web Apps: the two UI
pages, a config.js pointing at the Tailscale Funnel backend, the SWA config,
and the latest report snapshot, so a shared link renders a situational view
even with the backend completely down.

NOTE: keep this file ASCII-only. PowerShell 5.1 reads BOM-less scripts as
ANSI, and e.g. an em dash decodes into a CP1252 smart quote that PS parses as
a real quote character.

.EXAMPLE
.\tools\publish-frontend.ps1 -ApiBase "https://machine.tailnet.ts.net"
Then deploy the dist/ folder with the SWA CLI or the Azure portal.
Re-run (and re-deploy) to refresh the published snapshot.
#>
param(
    [Parameter(Mandatory = $true)] [string]$ApiBase,
    [string]$OutDir = "dist"
)

$ErrorActionPreference = "Stop"

if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force -Confirm:$false }
New-Item -ItemType Directory $OutDir | Out-Null

Copy-Item src/RetailFeedback.Api/wwwroot/index.html $OutDir/
Copy-Item src/RetailFeedback.Api/wwwroot/desk.html $OutDir/
Copy-Item deploy/staticwebapp.config.json $OutDir/

# UTF-8 without BOM so browsers parse it identically everywhere.
[System.IO.File]::WriteAllText(
    (Join-Path (Resolve-Path $OutDir) "config.js"),
    "window.API_BASE = `"$($ApiBase.TrimEnd('/'))`";`n")

# Report:SnapshotDir is relative to the API's working directory, which under
# `dotnet run` is the project dir - check both plausible locations.
$snapshotDirs = @("data/snapshots", "src/RetailFeedback.Api/data/snapshots")
$snapshotDir = $snapshotDirs | Where-Object { Test-Path (Join-Path $_ "report-latest.json") } | Select-Object -First 1
if ($snapshotDir) {
    Copy-Item (Join-Path $snapshotDir "report-latest.json") $OutDir/
    $stamp = Get-Date (Get-Item (Join-Path $snapshotDir "report-latest.json")).LastWriteTime -Format 'yyyy-MM-dd HH:mm'
    Write-Host "Snapshot published from ${snapshotDir}: report-latest.json ($stamp)"
    $snapshotHtml = Join-Path $snapshotDir "report-latest.html"
    if (Test-Path $snapshotHtml) { Copy-Item $snapshotHtml $OutDir/ }
} else {
    Write-Warning "No snapshot found in [$($snapshotDirs -join ', ')] - the backend-down fallback will have nothing to show. Generate a report first."
}

Write-Host "Bundle ready in $OutDir/ - deploy with: swa deploy $OutDir --env production (or the Azure portal)."
Write-Host "Remember: the API's Ingest:AllowedCorsOrigins must include the SWA origin."
