# SPDX-License-Identifier: EUPL-1.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$DownloadsPath,
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9A-Za-z.+-]+$')]
    [string]$SoftwareVersion,
    [string]$Commit
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Commit)) {
    $Commit = (& git rev-parse HEAD).Trim()
}
if ($Commit -notmatch '^[0-9a-fA-F]{40}([0-9a-fA-F]{24})?$') {
    throw 'Commit must be a full immutable hexadecimal SHA.'
}

$linuxName = "aetheus-agent-linux-x64-v$SoftwareVersion.tar.gz"
$windowsName = "aetheus-agent-win-x64-v$SoftwareVersion.zip"
$linuxSource = Join-Path $DownloadsPath $linuxName
$windowsSource = Join-Path $DownloadsPath $windowsName
if (-not (Test-Path -LiteralPath $linuxSource -PathType Leaf)) {
    throw "Missing Linux archive: $linuxSource"
}
if (-not (Test-Path -LiteralPath $windowsSource -PathType Leaf)) {
    throw "Missing Windows archive: $windowsSource"
}

$releasePath = Join-Path $DownloadsPath "releases\$SoftwareVersion"
New-Item -ItemType Directory -Path $releasePath -Force | Out-Null
Copy-Item -LiteralPath $linuxSource -Destination (Join-Path $releasePath $linuxName) -Force
Copy-Item -LiteralPath $windowsSource -Destination (Join-Path $releasePath $windowsName) -Force

$capabilities = @(
    'agent.self-update',
    'ai.run',
    'analysis.run',
    'artifact.collect',
    'artifact.restore',
    'pipeline.build',
    'shell.execute'
)
$archives = @(
    [ordered]@{
        platform = 'linux'
        architecture = 'x64'
        fileName = $linuxName
        sizeBytes = (Get-Item -LiteralPath $linuxSource).Length
        sha256 = (Get-FileHash -LiteralPath $linuxSource -Algorithm SHA256).Hash.ToLowerInvariant()
    },
    [ordered]@{
        platform = 'windows'
        architecture = 'x64'
        fileName = $windowsName
        sizeBytes = (Get-Item -LiteralPath $windowsSource).Length
        sha256 = (Get-FileHash -LiteralPath $windowsSource -Algorithm SHA256).Hash.ToLowerInvariant()
    }
)
# Bridge protocol-2 agents whose historical updater requires the 1-2 window.
$manifest = [ordered]@{
    softwareVersion = $SoftwareVersion
    protocolVersion = 2
    minimumSupportedProtocol = 1
    maximumSupportedProtocol = 2
    softwareCapabilities = $capabilities
    archives = $archives
    producedAtUtc = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
    commit = $Commit
}
$manifestPath = Join-Path $DownloadsPath 'agent-release-manifest.json'
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
Write-Output $manifestPath
