<#
.SYNOPSIS
Assembles the static frontend bundle for Azure Static Web Apps: the two UI
pages, a config.js pointing at the Tailscale Funnel backend, and the SWA
config. With -PublishSnapshot it also bundles the latest report snapshot so a
shared link renders a situational view even with the backend completely down.

CI ALWAYS bundles the committed, provenance-verified seed-42 snapshot
(deploy/snapshot/): in a fresh CI checkout it is the only report-latest.json
present, so this script (which bundles the NEWEST candidate) always picks it and
a push deploy is never a 404. -PublishSnapshot is OPT-IN only for a LOCALLY
generated runtime snapshot: the script cannot tell real from placeholder data,
and the hard rule bans placeholder-derived data in any demo, so verify against
docs/mock-data-register.md before passing it for a runtime snapshot. See
docs/decisions/0023-deploy-hardening-snapshot-and-pna.md.

NOTE: keep this file ASCII-only. PowerShell 5.1 reads BOM-less scripts as
ANSI, and e.g. an em dash decodes into a CP1252 smart quote that PS parses as
a real quote character.

.EXAMPLE
.\tools\publish-frontend.ps1 -ApiBase "https://machine.tailnet.ts.net" -PublishSnapshot
Then deploy the dist/ folder with the SWA CLI or the Azure portal.
Re-run (and re-deploy) to refresh the published snapshot.
#>
param(
    [Parameter(Mandatory = $true)] [string]$ApiBase,
    [string]$OutDir = "",
    [switch]$PublishSnapshot
)

$ErrorActionPreference = "Stop"

# Everything resolves against the repo root, not the caller's CWD.
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $OutDir) { $OutDir = Join-Path $repoRoot "dist" }

if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force -Confirm:$false }
New-Item -ItemType Directory $OutDir | Out-Null

Copy-Item (Join-Path $repoRoot "src/FeedbackIntelligence.Api/wwwroot/index.html") $OutDir/
Copy-Item (Join-Path $repoRoot "src/FeedbackIntelligence.Api/wwwroot/desk.html") $OutDir/
Copy-Item (Join-Path $repoRoot "deploy/staticwebapp.config.json") $OutDir/

# UTF-8 without BOM so browsers parse it identically everywhere.
[System.IO.File]::WriteAllText(
    (Join-Path (Resolve-Path $OutDir) "config.js"),
    "window.API_BASE = `"$($ApiBase.TrimEnd('/'))`";`n")

if ($PublishSnapshot) {
    # Take the NEWEST report-latest.json from: the committed demo snapshot
    # (deploy/snapshot - the ONLY one present in CI, so a push deploy always bundles a
    # snapshot and never leaves the SWA with a 404 fallback), or a locally generated
    # runtime snapshot (Report:SnapshotDir is relative to the API's working dir).
    # Outer @() forces an array: with a SINGLE match (as in CI, where only the
    # committed deploy/snapshot exists) a bare pipeline returns a scalar string, and
    # $candidates[0] would then be its first CHARACTER, not the path.
    $candidates = @(@("deploy/snapshot", "data/snapshots", "src/FeedbackIntelligence.Api/data/snapshots") |
        ForEach-Object { Join-Path $repoRoot "$_/report-latest.json" } |
        Where-Object { Test-Path $_ } |
        Sort-Object { (Get-Item $_).LastWriteTime } -Descending)
    if ($candidates.Count -gt 0) {
        $snapshot = $candidates[0]
        Copy-Item $snapshot $OutDir/
        $html = [System.IO.Path]::ChangeExtension($snapshot, ".html")
        if (Test-Path $html) { Copy-Item $html $OutDir/ }
        $stamp = Get-Date (Get-Item $snapshot).LastWriteTime -Format 'yyyy-MM-dd HH:mm'
        Write-Host "Snapshot published from $snapshot ($stamp)."
        Write-Host "VERIFY its provenance against docs/mock-data-register.md - placeholder-derived snapshots must never be deployed."
    } else {
        Write-Warning "No snapshot found - generate a report first."
    }
} else {
    Write-Host "Snapshot NOT bundled (opt in with -PublishSnapshot after verifying provenance; see docs/mock-data-register.md)."
}

Write-Host "Bundle ready in $OutDir - deploy with: swa deploy $OutDir --env production (or the Azure portal)."
Write-Host "Remember: the API's Ingest:AllowedCorsOrigins must include the SWA origin (no trailing slash)."
