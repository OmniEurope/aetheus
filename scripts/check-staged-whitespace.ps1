# SPDX-License-Identifier: EUPL-1.2
# Pre-commit check (recette R2-028): refuses a commit whose staged C# files are not whitespace-formatted,
# so the candidate stops reporting WHITESPACE findings on files nobody formatted.
#
# It checks the STAGED content, written out of the index with LF endings, not the working copy:
# a Windows checkout holds CRLF files that git normalizes to LF on commit, and dotnet format would
# otherwise refuse every one of them on ENDOFLINE although nothing wrong is being committed.
$ErrorActionPreference = 'Stop'

$files = @(git diff --cached --name-only --diff-filter=ACMR -- '*.cs')
if ($files.Count -eq 0) { exit 0 }

$root = Join-Path ([IO.Path]::GetTempPath()) ("aetheus-staged-format-" + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $root | Out-Null
    # In batches: a commit staging a few thousand C# files (a public distribution resync) exceeds the
    # Windows command-line limit in one call and git.exe cannot even start. Not --stdin: PowerShell
    # pipes CRLF lines, and git then looks for paths ending in a carriage return.
    $batches = @(@('.editorconfig') + $files) | ForEach-Object -Begin { $i = 0 } -Process { [pscustomobject]@{ Group = [math]::Floor($i++ / 200); Path = $_ } } |
        Group-Object Group
    foreach ($batch in $batches) {
        git -c core.autocrlf=false -c core.eol=lf checkout-index "--prefix=$root/" -- @($batch.Group.Path)
        if ($LASTEXITCODE -ne 0) { Write-Error 'Could not write the staged files out of the index.'; exit 1 }
    }

    dotnet format whitespace $root --folder --verify-no-changes --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host ''
        Write-Host 'Staged C# files listed above are not whitespace-formatted. Fix each with:'
        Write-Host '  dotnet format whitespace --folder --include <file>'
        Write-Host 'then stage it again.'
        exit 1
    }
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
