# SPDX-License-Identifier: EUPL-1.2
param(
    [Parameter(Mandatory = $true)][string]$PromptFile,
    [Parameter(Mandatory = $true)][string]$OutputFile,
    [Parameter(Mandatory = $true)][string]$WorkDirectory
)

$prompt = Get-Content -LiteralPath $PromptFile -Raw
$report = @"
# Rapport IA simulé

Le CLI local simulé a reçu une consigne de $($prompt.Length) caractères.
Aucun service externe n'a été contacté.

VERDICT: PASS
"@

Set-Content -LiteralPath $OutputFile -Value $report -Encoding utf8
Write-Output "Mock AI runner completed without network access."
