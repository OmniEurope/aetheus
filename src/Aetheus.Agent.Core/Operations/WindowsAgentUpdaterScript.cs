// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Operations;

internal static class WindowsAgentUpdaterScript
{
    internal static string Write(string stagingRoot)
    {
        var scriptPath = Path.Combine(stagingRoot, "apply-update.ps1");
        // The script is constant: pid/src/dest arrive as bound -File parameters, never
        // interpolated into a Bypass-policy script body.
        const string script = """
            # Aetheus agent self-update applier (generated, ephemeral).
            param(
                [int]$AgentPid,
                [string]$Src,
                [string]$Dest,
                [int]$ConfirmationTimeoutSeconds = 330
            )
            $ErrorActionPreference = 'Stop'

            # Wait for the agent process to exit (max ~30s) so the EXE/DLLs are unlocked.
            for ($i = 0; $i -lt 60; $i++) {
                if (-not (Get-Process -Id $AgentPid -ErrorAction SilentlyContinue)) { break }
                Start-Sleep -Milliseconds 500
            }
            if (Get-Process -Id $AgentPid -ErrorAction SilentlyContinue) {
                Add-Content -LiteralPath (Join-Path (Split-Path $Src -Parent) 'update.log') `
                    -Value "Update refused: agent process $AgentPid did not stop within 30 seconds."
                exit 3
            }

            # Keep one bounded rollback snapshot and restore it automatically if any copy fails.
            $StagingRoot = Split-Path $Src -Parent
            $WorkRoot = Split-Path $StagingRoot -Parent
            $Rollback = Join-Path $WorkRoot '.agent-rollback'
            $RollbackReady = $false
            try {
                Remove-Item -LiteralPath $Rollback -Recurse -Force -ErrorAction SilentlyContinue
                New-Item -ItemType Directory -Path $Rollback -Force | Out-Null
                $UnsafeLink = Get-ChildItem -LiteralPath $Dest -Recurse -Force | Where-Object {
                    $_.Attributes -band [IO.FileAttributes]::ReparsePoint
                } | Select-Object -First 1
                if ($UnsafeLink) {
                    throw "Install directory contains a reparse point: $($UnsafeLink.FullName)"
                }
                Get-ChildItem -LiteralPath $Dest -Force | Where-Object {
                    $_.Name -ne 'appsettings.json' -and -not $_.Name.EndsWith('.bak')
                } | Copy-Item -Destination $Rollback -Recurse -Force
                $ExistingAgent = Join-Path $Dest 'Aetheus.Agent.Windows.exe'
                if (Test-Path -LiteralPath $ExistingAgent) {
                    (Get-Item -LiteralPath $ExistingAgent).VersionInfo.FileVersion |
                        Set-Content -LiteralPath (Join-Path $Rollback 'rollback-version.txt')
                }
                $RollbackReady = $true

                # Mirror new binaries, preserving the operator's appsettings.json.
                Remove-Item -Path (Join-Path $Src 'appsettings.json') -ErrorAction SilentlyContinue
                Get-ChildItem -LiteralPath $Dest -Force | Where-Object {
                    $_.Name -ne 'appsettings.json'
                } | Remove-Item -Recurse -Force
                Copy-Item -Path (Join-Path $Src '*') -Destination $Dest -Recurse -Force

                # The freshly started agent removes this marker only after a successful
                # backend heartbeat. Until then this updater remains the rollback guard.
                $Marker = Join-Path $WorkRoot '.agent-update-pending'
                Set-Content -LiteralPath $Marker -Value '0'
                $Confirmed = $false
                for ($i = 0; $i -lt $ConfirmationTimeoutSeconds; $i++) {
                    if (-not (Test-Path -LiteralPath $Marker)) {
                        $Confirmed = $true
                        break
                    }
                    Start-Sleep -Seconds 1
                }
                if ($Confirmed) { exit 0 }

                $NewPid = 0
                if (Test-Path -LiteralPath $Marker) {
                    [void][int]::TryParse(
                        (Get-Content -LiteralPath $Marker -Raw).Trim(),
                        [ref]$NewPid)
                }
                if ($NewPid -gt 0 -and (Get-Process -Id $NewPid -ErrorAction SilentlyContinue)) {
                    Stop-Process -Id $NewPid -Force -ErrorAction SilentlyContinue
                    for ($i = 0; $i -lt 60; $i++) {
                        if (-not (Get-Process -Id $NewPid -ErrorAction SilentlyContinue)) { break }
                        Start-Sleep -Milliseconds 500
                    }
                }

                Get-ChildItem -LiteralPath $Dest -Force | Where-Object {
                    $_.Name -ne 'appsettings.json'
                } | Remove-Item -Recurse -Force
                Get-ChildItem -LiteralPath $Rollback -Force | Where-Object {
                    $_.Name -ne 'rollback-version.txt'
                } | Copy-Item -Destination $Dest -Recurse -Force
                Remove-Item -LiteralPath $Marker -Force -ErrorAction SilentlyContinue
                Add-Content -LiteralPath (Join-Path $StagingRoot 'update.log') `
                    -Value 'No confirming heartbeat arrived; previous binaries restored automatically.'
                exit 4
            }
            catch {
                $UpdateError = $_.Exception.Message
                if ($RollbackReady) {
                    try {
                        Get-ChildItem -LiteralPath $Dest -Force | Where-Object {
                            $_.Name -ne 'appsettings.json'
                        } | Remove-Item -Recurse -Force
                        Get-ChildItem -LiteralPath $Rollback -Force | Where-Object {
                            $_.Name -ne 'rollback-version.txt'
                        } | Copy-Item -Destination $Dest -Recurse -Force
                    }
                    catch {
                        Add-Content -LiteralPath (Join-Path $StagingRoot 'update.log') `
                            -Value "Update failed: $UpdateError; automatic rollback also failed: $($_.Exception.Message)"
                        exit 2
                    }
                    Add-Content -LiteralPath (Join-Path $StagingRoot 'update.log') `
                        -Value "Update failed: $UpdateError; previous binaries restored automatically."
                    exit 1
                }
                Add-Content -LiteralPath (Join-Path $StagingRoot 'update.log') `
                    -Value "Update failed before replacement started: $UpdateError"
                exit 1
            }
            """;
        File.WriteAllText(scriptPath, script);
        return scriptPath;
    }
}
