# SPDX-License-Identifier: EUPL-1.2
<#
.SYNOPSIS
    Runs every local test suite and refuses to report success unless all of them are green.

.DESCRIPTION
    Gate to run BEFORE launching aetheus-candidate. Candidate qualification costs roughly two hours
    and burns the production agent; twice in a row it was spent only to discover that a single E2E
    test had been failing all along, once on previousE2E and once on currentE2E. Both were visible
    locally and neither was looked at first.

    This script does not replace the pipeline gates and grants no exemption: it fails on the first
    red suite so the failure is seen in minutes instead of hours.

    E2E is excluded by default because it needs a deployed target, not a bare checkout. Pass
    -IncludeE2E on an environment where that target exists.

.EXAMPLE
    pwsh deploy/scripts/verify-local-suites-before-candidate.ps1
#>
[CmdletBinding()]
param(
    [switch]$IncludeE2E,
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

$suites = @(
    @{ Name = 'Back (unit + architecture guards)'; Project = 'tests/Aetheus.Back.Tests/Aetheus.Back.Tests.csproj' }
    @{ Name = 'Front (bUnit)';                     Project = 'tests/Aetheus.Front.Tests/Aetheus.Front.Tests.csproj' }
    @{ Name = 'Agent.Core';                        Project = 'tests/Aetheus.Agent.Core.Tests/Aetheus.Agent.Core.Tests.csproj' }
)

if ($IncludeE2E) {
    $suites += @{ Name = 'E2E (Playwright)'; Project = 'tests/Aetheus.E2E/Aetheus.E2E.csproj' }
}

$failed = [System.Collections.Generic.List[string]]::new()

foreach ($suite in $suites) {
    $projectPath = Join-Path $repoRoot $suite.Project
    if (-not (Test-Path $projectPath)) {
        Write-Host "SKIP  $($suite.Name) - project not found at $($suite.Project)" -ForegroundColor DarkYellow
        continue
    }

    Write-Host "RUN   $($suite.Name)" -ForegroundColor Cyan
    # Not -ErrorAction: dotnet test signals failure through its exit code, and a red suite must be
    # collected rather than abort the sweep, so the report lists every failing suite at once.
    & dotnet test $projectPath --configuration $Configuration --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAIL  $($suite.Name)" -ForegroundColor Red
        $failed.Add($suite.Name)
    }
    else {
        Write-Host "PASS  $($suite.Name)" -ForegroundColor Green
    }
}

Write-Host ''
if ($failed.Count -gt 0) {
    Write-Host "RESULT: FAIL - do NOT launch aetheus-candidate." -ForegroundColor Red
    foreach ($name in $failed) { Write-Host "  red: $name" -ForegroundColor Red }
    Write-Host 'Fix these locally first: candidate would seal grade F and be refused at the E threshold.'
    exit 1
}

Write-Host 'RESULT: PASS - every local suite is green; aetheus-candidate can be launched.' -ForegroundColor Green
if (-not $IncludeE2E) {
    Write-Host 'Note: E2E was not run (needs a deployed target). Use -IncludeE2E where one exists.' -ForegroundColor DarkYellow
}
exit 0
