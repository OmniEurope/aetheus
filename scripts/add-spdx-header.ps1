# SPDX-License-Identifier: EUPL-1.2
<#
.SYNOPSIS
    Prepends the SPDX license header to .cs and .razor files that lack it.

.DESCRIPTION
    Scans src/ and tests/ for .cs and .razor files (excluding obj/, bin/,
    and EF Migrations/) and prepends "// SPDX-License-Identifier: EUPL-1.2"
    followed by a blank line to any file that does not already contain it
    within the first 5 lines.

.PARAMETER DryRun
    When set, lists files that would be modified without changing them.

.PARAMETER Path
    Root directory to scan. Defaults to the repository root (parent of scripts/).

.EXAMPLE
    pwsh scripts/add-spdx-header.ps1
    pwsh scripts/add-spdx-header.ps1 -DryRun
#>
[CmdletBinding()]
param(
    [switch]$DryRun,
    [string]$Path
)

$ErrorActionPreference = 'Stop'

if (-not $Path) {
    $Path = Split-Path -Parent $PSScriptRoot
}

$header = '// SPDX-License-Identifier: EUPL-1.2'
$razorHeader = '@* SPDX-License-Identifier: EUPL-1.2 *@'
$marker = 'SPDX-License-Identifier: EUPL-1.2'

$excludeDirs = @('obj', 'bin', 'Migrations')

function ShouldExclude([string]$filePath) {
    foreach ($dir in $excludeDirs) {
        if ($filePath -match "([\\/])$dir[\\/]") {
            return $true
        }
    }
    return $false
}

function HasHeader([string]$filePath) {
    $lines = Get-Content -Path $filePath -TotalCount 5 -ErrorAction SilentlyContinue
    foreach ($line in $lines) {
        if ($line -match [regex]::Escape($marker)) {
            return $true
        }
    }
    return $false
}

$srcDir = Join-Path $Path 'src'
$testsDir = Join-Path $Path 'tests'
$scanDirs = @($srcDir, $testsDir) | Where-Object { Test-Path $_ }

$csFiles = $scanDirs | ForEach-Object {
    Get-ChildItem -Path $_ -Filter '*.cs' -Recurse -File
} | Where-Object { -not (ShouldExclude $_.FullName) }

$razorFiles = $scanDirs | ForEach-Object {
    Get-ChildItem -Path $_ -Filter '*.razor' -Recurse -File
} | Where-Object { -not (ShouldExclude $_.FullName) }

$modified = 0
$skipped = 0

foreach ($file in $csFiles) {
    if (HasHeader $file.FullName) {
        $skipped++
        continue
    }
    if ($DryRun) {
        Write-Host "Would add header: $($file.FullName)"
        $modified++
        continue
    }
    $content = [System.IO.File]::ReadAllText($file.FullName)
    $newContent = "$header`n$content"
    [System.IO.File]::WriteAllText($file.FullName, $newContent)
    $modified++
}

foreach ($file in $razorFiles) {
    if (HasHeader $file.FullName) {
        $skipped++
        continue
    }
    if ($DryRun) {
        Write-Host "Would add header: $($file.FullName)"
        $modified++
        continue
    }
    $content = [System.IO.File]::ReadAllText($file.FullName)
    $newContent = "$razorHeader`n$content"
    [System.IO.File]::WriteAllText($file.FullName, $newContent)
    $modified++
}

$total = $modified + $skipped
$verb = if ($DryRun) { 'Would modify' } else { 'Modified' }
Write-Host ""
Write-Host "SPDX Header Summary"
Write-Host "  Total files scanned : $total"
Write-Host "  Already had header  : $skipped"
Write-Host "  $($verb)            : $modified"
