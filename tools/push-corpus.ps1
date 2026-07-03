<#
.SYNOPSIS
Pushes a generated corpus through the public ingest endpoint — the demo
pipeline is exercised end to end, never via direct DB writes.

.PARAMETER MockStructuresFromGroundTruth
PLACEHOLDER RUNS ONLY (non-evidential, see docs/mock-data-register.md): when
the LLM window is unavailable, attach mock acceptedStructure values derived
from the ground-truth story definitions so the report's deterministic layer
has structured items to group. The REAL corpus run omits this switch — Poro
structures every item live at ingest.
#>
param(
    [Parameter(Mandatory = $true)] [string]$Corpus,
    [string]$GroundTruth = "",
    [string]$BaseUrl = "http://localhost:5088",
    [switch]$MockStructuresFromGroundTruth
)

$ErrorActionPreference = "Stop"

$storyByItemId = @{}
if ($MockStructuresFromGroundTruth) {
    if (-not $GroundTruth) { throw "MockStructuresFromGroundTruth requires -GroundTruth." }
    $truth = Get-Content $GroundTruth -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($story in $truth.stories) {
        foreach ($feedbackId in $story.feedbackIds) { $storyByItemId[$feedbackId] = $story }
    }
    Write-Host "MOCK MODE: structures derived from ground truth for $($storyByItemId.Count) story items (non-evidential)."
}

$created = 0; $duplicate = 0; $failed = 0
foreach ($line in Get-Content $Corpus -Encoding UTF8) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $item = $line | ConvertFrom-Json
    $body = @{ id = $item.id; source = $item.source; text = $item.text; timestamp = $item.timestamp }

    if ($MockStructuresFromGroundTruth -and $storyByItemId.ContainsKey($item.id)) {
        $story = $storyByItemId[$item.id]
        $body.acceptedStructure = @{
            department = $story.expectedDepartment
            theme      = $story.expectedThemeKeywords[0]
            severity   = if ($story.expectAlert) { "critical" } else { "medium" }
            type       = "complaint"
            language   = "fi"
        }
    }

    try {
        Invoke-RestMethod "$BaseUrl/feedback" -Method Post -ContentType "application/json; charset=utf-8" `
            -Body ([System.Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 5))) | Out-Null
        $created++
    } catch {
        $status = try { $_.Exception.Response.StatusCode.value__ } catch { 0 }
        if ($status -eq 409) { $duplicate++ } else { $failed++; Write-Host "FAIL $($item.id): HTTP $status" }
    }
}
Write-Host "Pushed: $created created, $duplicate duplicates (already stored), $failed failed."
if ($failed -gt 0) { exit 1 }
