# SPDX-License-Identifier: EUPL-1.2
#Requires -Version 7.4

[CmdletBinding()]
param(
    [string]$EvidencePath = "",
    [switch]$Offline,
    [int]$ProcessTimeoutSeconds = 1800
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
Set-Location $repoRoot

$manifestPath = Join-Path $repoRoot "scanner-manifest.json"
$manifest = Get-Content -Raw $manifestPath | ConvertFrom-Json
$startedAt = [DateTimeOffset]::UtcNow
$campaignId = "{0}-{1}" -f $startedAt.ToString("yyyyMMddHHmmss"), ([Guid]::NewGuid().ToString("N").Substring(0, 8))
$campaignLabel = "aetheus.scan.campaign=$campaignId"
$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempRoot = [IO.Path]::GetFullPath((Join-Path $tempBase "aetheus-scanner-campaign-$campaignId"))
if (-not $tempRoot.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Campaign workspace must remain inside the operating-system temporary directory."
}

if ([string]::IsNullOrWhiteSpace($EvidencePath)) {
    $EvidencePath = Join-Path $repoRoot (
        "docs/security/evidence/scanner-campaign-{0}.json" -f $startedAt.ToString("yyyy-MM-dd"))
}
elseif (-not [IO.Path]::IsPathRooted($EvidencePath)) {
    $EvidencePath = Join-Path $repoRoot $EvidencePath
}
$EvidencePath = [IO.Path]::GetFullPath($EvidencePath)

$results = [Collections.Generic.List[object]]::new()
$images = [Collections.Generic.List[object]]::new()
$campaignError = $null
$cleanup = [ordered]@{
    attempted = $false
    workspaceRemoved = $false
    residualContainers = -1
    residualNetworks = -1
    passed = $false
}

function Get-Sha256Text {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Value)

    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    return [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($bytes))
}

function Get-OutputTail {
    param([string]$Value)

    if ([string]::IsNullOrEmpty($Value)) {
        return ""
    }
    $normalized = $Value.Replace("`r`n", "`n").Trim()
    if ($normalized.Length -le 4000) {
        return $normalized
    }
    return $normalized.Substring($normalized.Length - 4000)
}

function Invoke-NativeProcess {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$Arguments,
        [int]$TimeoutSeconds = $ProcessTimeoutSeconds
    )

    $start = [DateTimeOffset]::UtcNow
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "Unable to start '$FilePath'."
    }

    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $completed = $process.WaitForExit([Math]::Max(1, $TimeoutSeconds) * 1000)
    if (-not $completed) {
        try {
            $process.Kill($true)
        }
        catch {
            Write-Warning "Failed to terminate timed-out process $FilePath."
        }
        $process.WaitForExit()
    }

    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    $exitCode = if ($completed) { $process.ExitCode } else { -1 }
    $process.Dispose()

    return [ordered]@{
        file = $FilePath
        arguments = @($Arguments)
        command = "$FilePath " + (($Arguments | ForEach-Object {
            if ($_ -match "[\s`"]") { '"' + $_.Replace('"', '\"') + '"' } else { $_ }
        }) -join " ")
        exitCode = $exitCode
        timedOut = -not $completed
        durationMilliseconds = [int64]([DateTimeOffset]::UtcNow - $start).TotalMilliseconds
        stdoutSha256 = Get-Sha256Text $stdout
        stderrSha256 = Get-Sha256Text $stderr
        stdoutTail = Get-OutputTail $stdout
        stderrTail = Get-OutputTail $stderr
    }
}

function Get-ReportItemCount {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)]
        [ValidateSet("Sarif", "CycloneDx", "Zap", "Eslint", "Jscpd")]
        [string]$Kind
    )

    $document = Get-Content -Raw $Path | ConvertFrom-Json -Depth 100
    switch ($Kind) {
        "Sarif" {
            $count = 0
            foreach ($run in @($document.runs)) {
                $count += @($run.results).Count
            }
            return $count
        }
        "CycloneDx" {
            return @($document.components).Count
        }
        "Zap" {
            $count = 0
            foreach ($site in @($document.site)) {
                $count += @($site.alerts).Count
            }
            return $count
        }
        "Eslint" {
            $count = 0
            foreach ($file in @($document)) {
                $count += @($file.messages).Count
            }
            return $count
        }
        "Jscpd" {
            if ($null -ne $document.statistics.total.clones) {
                return [int]$document.statistics.total.clones
            }
            if ($null -ne $document.duplicates) {
                return @($document.duplicates).Count
            }
            return 0
        }
    }
}

function Invoke-ScannerCase {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string[]]$DockerArguments,
        [Parameter(Mandatory)][int[]]$ExpectedExitCodes,
        [Parameter(Mandatory)][string]$ReportPath,
        [Parameter(Mandatory)]
        [ValidateSet("Sarif", "CycloneDx", "Zap", "Eslint", "Jscpd")]
        [string]$ReportKind,
        [int]$MinimumItems = 0,
        [int]$MaximumItems = [int]::MaxValue,
        [int]$TimeoutSeconds = $ProcessTimeoutSeconds
    )

    if (Test-Path $ReportPath) {
        Remove-Item -LiteralPath $ReportPath -Force
    }
    Write-Host "[$Name] running"
    $processResult = Invoke-NativeProcess "docker" $DockerArguments $TimeoutSeconds
    $reportExists = Test-Path -LiteralPath $ReportPath -PathType Leaf
    $reportSize = if ($reportExists) { (Get-Item -LiteralPath $ReportPath).Length } else { 0 }
    $reportSha = if ($reportExists) {
        (Get-FileHash -LiteralPath $ReportPath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    else {
        ""
    }
    $itemCount = if ($reportExists -and $reportSize -gt 0) {
        Get-ReportItemCount $ReportPath $ReportKind
    }
    else {
        -1
    }
    $passed = $ExpectedExitCodes -contains $processResult.exitCode `
        -and -not $processResult.timedOut `
        -and $reportExists `
        -and $reportSize -gt 0 `
        -and $itemCount -ge $MinimumItems `
        -and $itemCount -le $MaximumItems

    $result = [ordered]@{
        name = $Name
        passed = $passed
        expectedExitCodes = @($ExpectedExitCodes)
        minimumItems = $MinimumItems
        maximumItems = $MaximumItems
        observedItems = $itemCount
        report = [ordered]@{
            kind = $ReportKind
            sizeBytes = $reportSize
            sha256 = $reportSha
        }
        process = $processResult
    }
    $results.Add($result)
    if (-not $passed) {
        throw "Scanner case '$Name' failed its executable evidence contract."
    }
    Write-Host "[$Name] passed (items=$itemCount, sha256=$reportSha)"
    return $result
}

function Get-Scanner {
    param([Parameter(Mandatory)][string]$Key)

    $scanner = @($manifest.scanners | Where-Object key -eq $Key)
    if ($scanner.Count -ne 1) {
        throw "scanner-manifest.json must contain exactly one '$Key' entry."
    }
    return $scanner[0]
}

function New-CaseDirectory {
    param([Parameter(Mandatory)][string]$Name)

    $path = Join-Path $tempRoot $Name
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    return [IO.Path]::GetFullPath($path)
}

function New-GitSourceSnapshot {
    param([Parameter(Mandatory)][string]$Name)

    $snapshot = New-CaseDirectory $Name
    $files = @(& git -C $repoRoot ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to enumerate the Git-visible source files."
    }

    $copied = 0
    foreach ($relativePath in $files) {
        if ([string]::IsNullOrWhiteSpace($relativePath)) {
            continue
        }
        if ([IO.Path]::IsPathRooted($relativePath) -or
            $relativePath -match '(^|[\\/])\.\.([\\/]|$)') {
            throw "Git returned an unsafe source path '$relativePath'."
        }

        $source = [IO.Path]::GetFullPath((Join-Path $repoRoot $relativePath))
        if (-not $source.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Git-visible source path escaped the repository root: '$relativePath'."
        }
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            continue
        }

        $destination = Join-Path $snapshot $relativePath
        $destinationDirectory = Split-Path -Parent $destination
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination -Force
        $copied++
    }

    if ($copied -eq 0) {
        throw "The Git-visible source snapshot is empty."
    }
    return [ordered]@{
        path = $snapshot
        fileCount = $copied
    }
}

function Get-BindMount {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination,
        [switch]$ReadOnly
    )

    $value = "type=bind,src=$([IO.Path]::GetFullPath($Source)),dst=$Destination"
    if ($ReadOnly) {
        $value += ",readonly"
    }
    return $value
}

function Get-CommonDockerArguments {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Network,
        [switch]$ReadOnly
    )

    $arguments = @(
        "run", "--rm",
        "--name", "aetheus-scan-$campaignId-$Name",
        "--label", $campaignLabel,
        "--network", $Network,
        "--cap-drop", "ALL",
        "--security-opt", "no-new-privileges"
    )
    if ($ReadOnly) {
        $arguments += "--read-only"
    }
    return $arguments
}

function Write-LockFixture {
    param(
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][string]$Version
    )

    $package = [ordered]@{
        name = "aetheus-scanner-campaign-fixture"
        version = "1.0.0"
        private = $true
        dependencies = [ordered]@{ lodash = $Version }
    }
    $lock = [ordered]@{
        name = "aetheus-scanner-campaign-fixture"
        version = "1.0.0"
        lockfileVersion = 3
        requires = $true
        packages = [ordered]@{
            "" = [ordered]@{
                name = "aetheus-scanner-campaign-fixture"
                version = "1.0.0"
                dependencies = [ordered]@{ lodash = $Version }
            }
            "node_modules/lodash" = [ordered]@{ version = $Version }
        }
        dependencies = [ordered]@{
            lodash = [ordered]@{ version = $Version }
        }
    }
    $package | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Directory "package.json") -Encoding utf8NoBOM
    $lock | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Directory "package-lock.json") -Encoding utf8NoBOM
}

function Get-LabeledResourceCount {
    param(
        [Parameter(Mandatory)]
        [ValidateSet("container", "network")]
        [string]$Kind
    )

    $arguments = if ($Kind -eq "container") {
        @("ps", "-aq", "--filter", "label=$campaignLabel")
    }
    else {
        @("network", "ls", "-q", "--filter", "label=$campaignLabel")
    }
    $result = Invoke-NativeProcess "docker" $arguments 30
    if ($result.exitCode -ne 0) {
        throw "Unable to inspect campaign $Kind resources."
    }
    if ([string]::IsNullOrWhiteSpace($result.stdoutTail)) {
        return 0
    }
    return @($result.stdoutTail -split "`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count
}

try {
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

    $requiredKeys = @(
        "gitleaks", "opengrep", "trivy-dependencies", "trivy-iac", "trivy-image",
        "syft", "ruff", "pmd-java", "eslint", "jscpd", "zap-passive", "zap-api", "zap-active"
    )
    foreach ($key in $requiredKeys) {
        [void](Get-Scanner $key)
    }

    $imageReferences = @($manifest.scanners |
        ForEach-Object {
            if ($null -ne $_.PSObject.Properties["image"]) {
                $_.image
            }
            if ($null -ne $_.PSObject.Properties["runtimeImage"]) {
                $_.runtimeImage
            }
        } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique)
    foreach ($reference in $imageReferences) {
        if (-not $Offline) {
            Write-Host "[image] pulling $reference"
            $pull = Invoke-NativeProcess "docker" @("pull", $reference) 3600
            if ($pull.exitCode -ne 0 -or $pull.timedOut) {
                throw "Unable to pull pinned scanner image '$reference'."
            }
        }
        $inspect = Invoke-NativeProcess "docker" @("image", "inspect", $reference, "--format", "{{.Id}}") 60
        if ($inspect.exitCode -ne 0 -or [string]::IsNullOrWhiteSpace($inspect.stdoutTail)) {
            throw "Pinned scanner image '$reference' is unavailable locally."
        }
        $images.Add([ordered]@{
            reference = $reference
            configuredDigest = ($reference -split "@", 2)[1]
            imageId = $inspect.stdoutTail.Trim()
        })
    }

    $gitleaks = Get-Scanner "gitleaks"
    $gitleaksPositive = New-CaseDirectory "gitleaks-positive"
    $gitleaksNegative = New-CaseDirectory "gitleaks-negative"
    $gitleaksOutput = New-CaseDirectory "gitleaks-output"
    $positiveGitHubToken = 'ghp_{0}{1}' -f '7YHf9Q2nK4mP8sV1xR', '6tB3cD5gL0jW2zA9uE'
    "token = `"$positiveGitHubToken`"" |
        Set-Content -LiteralPath (Join-Path $gitleaksPositive "fixture.txt") -Encoding utf8NoBOM
    'token = "synthetic-safe-value"' |
        Set-Content -LiteralPath (Join-Path $gitleaksNegative "fixture.txt") -Encoding utf8NoBOM

    $gitleaksPositiveReport = Join-Path $gitleaksOutput "positive.sarif"
    $arguments = Get-CommonDockerArguments "gitleaks-positive" "none" -ReadOnly
    $arguments += @(
        "--user", "65532:65532",
        "--mount", (Get-BindMount $gitleaksPositive "/src" -ReadOnly),
        "--mount", (Get-BindMount $gitleaksOutput "/out"),
        "--entrypoint", $gitleaks.entryPoint,
        $gitleaks.image,
        "dir", "/src", "--report-format", "sarif", "--report-path", "/out/positive.sarif",
        "--redact=100", "--exit-code", "1", "--no-banner"
    )
    Invoke-ScannerCase "gitleaks-positive" $arguments @(1) $gitleaksPositiveReport "Sarif" 1 | Out-Null

    $gitleaksNegativeReport = Join-Path $gitleaksOutput "negative.sarif"
    $arguments = Get-CommonDockerArguments "gitleaks-negative" "none" -ReadOnly
    $arguments += @(
        "--user", "65532:65532",
        "--mount", (Get-BindMount $gitleaksNegative "/src" -ReadOnly),
        "--mount", (Get-BindMount $gitleaksOutput "/out"),
        "--entrypoint", $gitleaks.entryPoint,
        $gitleaks.image,
        "dir", "/src", "--report-format", "sarif", "--report-path", "/out/negative.sarif",
        "--redact=100", "--exit-code", "1", "--no-banner"
    )
    Invoke-ScannerCase "gitleaks-negative" $arguments @(0) $gitleaksNegativeReport "Sarif" 0 0 | Out-Null

    $opengrep = Get-Scanner "opengrep"
    $opengrepBinaryDirectory = New-CaseDirectory "opengrep-binary"
    $opengrepBinary = Join-Path $opengrepBinaryDirectory "opengrep"
    if ($Offline) {
        throw "OpenGrep is an externally verified binary; the terminal campaign requires a fresh official download."
    }
    Write-Host "[opengrep] downloading verified binary"
    Invoke-WebRequest -Uri $opengrep.downloadUriLinuxAmd64 -OutFile $opengrepBinary -UseBasicParsing
    $opengrepHash = (Get-FileHash -LiteralPath $opengrepBinary -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($opengrepHash -ne $opengrep.sha256LinuxAmd64) {
        throw "OpenGrep binary SHA-256 mismatch."
    }

    $opengrepPositive = New-CaseDirectory "opengrep-positive"
    $opengrepNegative = New-CaseDirectory "opengrep-negative"
    $opengrepOutput = New-CaseDirectory "opengrep-output"
    $securityCorpus = Join-Path $repoRoot ".aetheus/security-rules/tests/corpus"
    $extensions = @{
        "csharp" = "cs"
        "javascript" = "js"
        "python" = "py"
        "java" = "java"
    }
    foreach ($language in $extensions.Keys) {
        Copy-Item -LiteralPath (Join-Path $securityCorpus "$language-positive.$($extensions[$language]).fixture") `
            -Destination (Join-Path $opengrepPositive "positive-$language.$($extensions[$language])")
        Copy-Item -LiteralPath (Join-Path $securityCorpus "$language-negative.$($extensions[$language]).fixture") `
            -Destination (Join-Path $opengrepNegative "negative-$language.$($extensions[$language])")
    }

    $opengrepRules = Join-Path $repoRoot ".aetheus/security-rules/opengrep/aetheus-security.yml"
    $opengrepRuns = [Collections.Generic.List[object]]::new()
    foreach ($iteration in 1..2) {
        $report = Join-Path $opengrepOutput "positive-$iteration.sarif"
        $arguments = Get-CommonDockerArguments "opengrep-positive-$iteration" "none" -ReadOnly
        $arguments += @(
            "--user", "65532:65532",
            "--tmpfs", "/tmp:rw,noexec,nosuid,nodev,size=134217728,mode=1777",
            "--tmpfs", "/opengrep-home:rw,exec,nosuid,nodev,size=536870912,mode=1777",
            "--env", "HOME=/opengrep-home",
            "--mount", (Get-BindMount $opengrepBinary "/input/opengrep" -ReadOnly),
            "--mount", (Get-BindMount $opengrepRules "/rules/aetheus-security.yml" -ReadOnly),
            "--mount", (Get-BindMount $opengrepPositive "/src" -ReadOnly),
            "--mount", (Get-BindMount $opengrepOutput "/out"),
            "--entrypoint", "/bin/sh",
            $opengrep.runtimeImage,
            "-c",
            "cp /input/opengrep /opengrep-home/opengrep && chmod 500 /opengrep-home/opengrep && /opengrep-home/opengrep scan --sarif-output=/out/positive-$iteration.sarif --config=/rules/aetheus-security.yml /src"
        )
        $opengrepRuns.Add(
            (Invoke-ScannerCase "opengrep-positive-$iteration" $arguments @(0, 1) $report "Sarif" 4 4)
        )
    }
    if ($opengrepRuns[0].report.sha256 -ne $opengrepRuns[1].report.sha256) {
        throw "Two identical OpenGrep executions did not produce the same SARIF SHA-256."
    }

    $opengrepNegativeReport = Join-Path $opengrepOutput "negative.sarif"
    $arguments = Get-CommonDockerArguments "opengrep-negative" "none" -ReadOnly
    $arguments += @(
        "--user", "65532:65532",
        "--tmpfs", "/tmp:rw,noexec,nosuid,nodev,size=134217728,mode=1777",
        "--tmpfs", "/opengrep-home:rw,exec,nosuid,nodev,size=536870912,mode=1777",
        "--env", "HOME=/opengrep-home",
        "--mount", (Get-BindMount $opengrepBinary "/input/opengrep" -ReadOnly),
        "--mount", (Get-BindMount $opengrepRules "/rules/aetheus-security.yml" -ReadOnly),
        "--mount", (Get-BindMount $opengrepNegative "/src" -ReadOnly),
        "--mount", (Get-BindMount $opengrepOutput "/out"),
        "--entrypoint", "/bin/sh",
        $opengrep.runtimeImage,
        "-c",
        "cp /input/opengrep /opengrep-home/opengrep && chmod 500 /opengrep-home/opengrep && /opengrep-home/opengrep scan --sarif-output=/out/negative.sarif --config=/rules/aetheus-security.yml /src"
    )
    Invoke-ScannerCase "opengrep-negative" $arguments @(0) $opengrepNegativeReport "Sarif" 0 0 | Out-Null

    $ruff = Get-Scanner "ruff"
    $ruffPositive = New-CaseDirectory "ruff-positive"
    $ruffNegative = New-CaseDirectory "ruff-negative"
    $ruffOutput = New-CaseDirectory "ruff-output"
    Copy-Item -LiteralPath (Join-Path $repoRoot ".aetheus/quality-rules/tests/corpus/python-positive.py.fixture") `
        -Destination (Join-Path $ruffPositive "positive.py")
    Copy-Item -LiteralPath (Join-Path $repoRoot ".aetheus/quality-rules/tests/corpus/python-negative.py.fixture") `
        -Destination (Join-Path $ruffNegative "negative.py")
    foreach ($case in @(
        @{ Name = "positive"; Source = $ruffPositive; Exit = @(1); Min = 1; Max = [int]::MaxValue },
        @{ Name = "negative"; Source = $ruffNegative; Exit = @(0); Min = 0; Max = 0 }
    )) {
        $report = Join-Path $ruffOutput "$($case.Name).sarif"
        $arguments = Get-CommonDockerArguments "ruff-$($case.Name)" "none" -ReadOnly
        $arguments += @(
            "--user", "65532:65532",
            "--tmpfs", "/tmp:rw,nosuid,nodev,size=268435456,mode=1777",
            "--env", "RUFF_CACHE_DIR=/tmp/ruff-cache",
            "--mount", (Get-BindMount $case.Source "/src" -ReadOnly),
            "--mount", (Get-BindMount $ruffOutput "/out"),
            "--entrypoint", $ruff.entryPoint,
            $ruff.image,
            "check", "/src", "--output-format", "sarif", "--output-file", "/out/$($case.Name).sarif"
        )
        Invoke-ScannerCase "ruff-$($case.Name)" $arguments $case.Exit $report "Sarif" $case.Min $case.Max | Out-Null
    }

    $pmd = Get-Scanner "pmd-java"
    $pmdPositive = New-CaseDirectory "pmd-positive"
    $pmdNegative = New-CaseDirectory "pmd-negative"
    $pmdOutput = New-CaseDirectory "pmd-output"
    Copy-Item -LiteralPath (Join-Path $repoRoot ".aetheus/quality-rules/tests/corpus/java-positive.java.fixture") `
        -Destination (Join-Path $pmdPositive "QualityPositive.java")
    Copy-Item -LiteralPath (Join-Path $repoRoot ".aetheus/quality-rules/tests/corpus/java-negative.java.fixture") `
        -Destination (Join-Path $pmdNegative "QualityNegative.java")
    foreach ($case in @(
        @{ Name = "positive"; Source = $pmdPositive; Exit = @(4); Min = 1; Max = [int]::MaxValue },
        @{ Name = "negative"; Source = $pmdNegative; Exit = @(0); Min = 0; Max = 0 }
    )) {
        $report = Join-Path $pmdOutput "$($case.Name).sarif"
        $arguments = Get-CommonDockerArguments "pmd-$($case.Name)" "none" -ReadOnly
        $arguments += @(
            "--user", "65532:65532",
            "--tmpfs", "/tmp:rw,nosuid,nodev,size=268435456,mode=1777",
            "--mount", (Get-BindMount $case.Source "/src" -ReadOnly),
            "--mount", (Get-BindMount $pmdOutput "/out"),
            "--entrypoint", $pmd.entryPoint,
            $pmd.image,
            "check", "-d", "/src", "-R", "rulesets/java/quickstart.xml",
            "-f", "sarif", "-r", "/out/$($case.Name).sarif"
        )
        Invoke-ScannerCase "pmd-$($case.Name)" $arguments $case.Exit $report "Sarif" $case.Min $case.Max | Out-Null
    }

    $trivy = Get-Scanner "trivy-dependencies"
    $trivyCache = New-CaseDirectory "trivy-cache"
    $trivyOutput = New-CaseDirectory "trivy-output"
    $trivyPositive = New-CaseDirectory "trivy-positive"
    $trivyNegative = New-CaseDirectory "trivy-negative"
    Write-LockFixture $trivyPositive "4.17.20"
    Write-LockFixture $trivyNegative "4.18.0"

    if (-not $Offline) {
        Write-Host "[trivy-db] refreshing vulnerability database"
        $arguments = Get-CommonDockerArguments "trivy-db" "bridge" -ReadOnly
        $arguments += @(
            "--tmpfs", "/tmp:rw,noexec,nosuid,nodev,size=268435456,mode=1777",
            "--env", "HOME=/tmp",
            "--mount", (Get-BindMount $trivyCache "/cache"),
            "--entrypoint", $trivy.entryPoint,
            $trivy.image,
            "--cache-dir", "/cache", "image", "--download-db-only"
        )
        $dbResult = Invoke-NativeProcess "docker" $arguments 1800
        if ($dbResult.exitCode -ne 0 -or $dbResult.timedOut) {
            throw "Trivy vulnerability database refresh failed: $($dbResult.stderrTail)"
        }
    }
    foreach ($case in @(
        @{ Name = "positive"; Source = $trivyPositive; Exit = @(1); Min = 1; Max = [int]::MaxValue },
        @{ Name = "negative"; Source = $trivyNegative; Exit = @(0); Min = 0; Max = 0 }
    )) {
        $report = Join-Path $trivyOutput "dependencies-$($case.Name).sarif"
        $arguments = Get-CommonDockerArguments "trivy-dependencies-$($case.Name)" "none" -ReadOnly
        $arguments += @(
            "--tmpfs", "/tmp:rw,noexec,nosuid,nodev,size=268435456,mode=1777",
            "--env", "HOME=/tmp",
            "--mount", (Get-BindMount $case.Source "/src" -ReadOnly),
            "--mount", (Get-BindMount $trivyOutput "/out"),
            "--mount", (Get-BindMount $trivyCache "/cache"),
            "--entrypoint", $trivy.entryPoint,
            $trivy.image,
            "--cache-dir", "/cache", "fs", "--skip-db-update", "--scanners", "vuln",
            "--format", "sarif", "--output", "/out/dependencies-$($case.Name).sarif",
            "--exit-code", "1", "--severity", "HIGH,CRITICAL", "/src"
        )
        Invoke-ScannerCase "trivy-dependencies-$($case.Name)" $arguments $case.Exit $report "Sarif" $case.Min $case.Max | Out-Null
    }

    $trivyIac = Get-Scanner "trivy-iac"
    $trivyIacReport = Join-Path $trivyOutput "iac.sarif"
    $gitSourceSnapshot = New-GitSourceSnapshot "git-source"
    $arguments = Get-CommonDockerArguments "trivy-iac" "none" -ReadOnly
    $arguments += @(
        "--tmpfs", "/tmp:rw,noexec,nosuid,nodev,size=268435456,mode=1777",
        "--env", "HOME=/tmp",
        "--mount", (Get-BindMount $gitSourceSnapshot.path "/src" -ReadOnly),
        "--mount", (Get-BindMount $trivyOutput "/out"),
        "--entrypoint", $trivyIac.entryPoint,
        $trivyIac.image,
        "fs", "--scanners", "misconfig", "--skip-check-update",
        "--format", "sarif", "--output", "/out/iac.sarif",
        "--exit-code", "1", "--severity", "HIGH,CRITICAL", "/src"
    )
    Invoke-ScannerCase "trivy-iac" $arguments @(0, 1) $trivyIacReport "Sarif" 0 | Out-Null

    $nodeScanner = Get-Scanner "eslint"
    $imageArchiveDirectory = New-CaseDirectory "image-archive"
    $imageArchive = Join-Path $imageArchiveDirectory "node.tar"
    Write-Host "[image-archive] saving exact pinned Node image"
    $saveResult = Invoke-NativeProcess "docker" @("image", "save", "--output", $imageArchive, $nodeScanner.image) 1800
    if ($saveResult.exitCode -ne 0 -or -not (Test-Path $imageArchive)) {
        throw "Unable to save exact pinned image archive."
    }

    $trivyImage = Get-Scanner "trivy-image"
    $trivyImageReport = Join-Path $trivyOutput "image.sarif"
    $arguments = Get-CommonDockerArguments "trivy-image" "none" -ReadOnly
    $arguments += @(
        "--tmpfs", "/tmp:rw,noexec,nosuid,nodev,size=268435456,mode=1777",
        "--env", "HOME=/tmp",
        "--mount", (Get-BindMount $imageArchiveDirectory "/src" -ReadOnly),
        "--mount", (Get-BindMount $trivyOutput "/out"),
        "--mount", (Get-BindMount $trivyCache "/cache"),
        "--entrypoint", $trivyImage.entryPoint,
        $trivyImage.image,
        "--cache-dir", "/cache", "image", "--skip-db-update", "--input", "/src/node.tar",
        "--format", "sarif", "--output", "/out/image.sarif",
        "--exit-code", "1", "--severity", "HIGH,CRITICAL"
    )
    Invoke-ScannerCase "trivy-image" $arguments @(0, 1) $trivyImageReport "Sarif" 0 | Out-Null

    $syft = Get-Scanner "syft"
    $syftOutput = New-CaseDirectory "syft-output"
    $syftReport = Join-Path $syftOutput "report.cdx.json"
    $arguments = Get-CommonDockerArguments "syft" "none" -ReadOnly
    $arguments += @(
        "--user", "65532:65532",
        "--tmpfs", "/tmp:rw,nosuid,nodev,size=536870912,mode=1777",
        "--env", "SYFT_CHECK_FOR_APP_UPDATE=false",
        "--env", "SYFT_CACHE_DIR=/tmp/syft-cache",
        "--mount", (Get-BindMount $imageArchiveDirectory "/src" -ReadOnly),
        "--mount", (Get-BindMount $syftOutput "/out"),
        "--entrypoint", $syft.entryPoint,
        $syft.image,
        "scan", "docker-archive:/src/node.tar", "--output", "cyclonedx-json=/out/report.cdx.json"
    )
    Invoke-ScannerCase "syft" $arguments @(0) $syftReport "CycloneDx" 1 | Out-Null

    $nodeWorkspace = New-GitSourceSnapshot "node-workspace"
    Write-Host "[node-restore] installing the exact Linux dependency graph from package-lock.json"
    $nodeRestoreArguments = Get-CommonDockerArguments "node-restore" "bridge"
    $nodeRestoreArguments += @(
        "--workdir", "/src",
        "--mount", (Get-BindMount $nodeWorkspace.path "/src"),
        "--entrypoint", "npm",
        $nodeScanner.image,
        "ci", "--ignore-scripts", "--no-audit", "--no-fund"
    )
    $nodeRestoreEvidence = Invoke-NativeProcess "docker" $nodeRestoreArguments 900
    if ($nodeRestoreEvidence.exitCode -ne 0 -or $nodeRestoreEvidence.timedOut) {
        throw "The locked Linux Node dependency restore failed."
    }

    $eslintOutput = New-CaseDirectory "eslint-output"
    $eslintReport = Join-Path $eslintOutput "report.json"
    $arguments = Get-CommonDockerArguments "eslint" "none" -ReadOnly
    $arguments += @(
        "--user", "65532:65532",
        "--tmpfs", "/tmp:rw,nosuid,nodev,size=268435456,mode=1777",
        "--workdir", $nodeScanner.containerWorkingDirectory,
        "--mount", (Get-BindMount $nodeWorkspace.path "/src" -ReadOnly),
        "--mount", (Get-BindMount $eslintOutput "/out"),
        "--entrypoint", $nodeScanner.entryPoint,
        $nodeScanner.image,
        "--prefix", "/src", "exec", "--offline", "--", "eslint",
        "src/Aetheus.Front/wwwroot/js", "--format", "json", "--output-file", "/out/report.json"
    )
    Invoke-ScannerCase "eslint" $arguments @(0) $eslintReport "Eslint" 0 0 | Out-Null

    $jscpd = Get-Scanner "jscpd"
    $jscpdOutput = New-CaseDirectory "jscpd-output"
    $jscpdReport = Join-Path $jscpdOutput "jscpd-report.json"
    $arguments = Get-CommonDockerArguments "jscpd" "none" -ReadOnly
    $arguments += @(
        "--user", "65532:65532",
        "--tmpfs", "/tmp:rw,nosuid,nodev,size=268435456,mode=1777",
        "--workdir", $jscpd.containerWorkingDirectory,
        "--mount", (Get-BindMount $nodeWorkspace.path "/src" -ReadOnly),
        "--mount", (Get-BindMount $jscpdOutput "/out"),
        "--entrypoint", $jscpd.entryPoint,
        $jscpd.image,
        "--prefix", "/src", "exec", "--offline", "--", "jscpd", "/src",
        "--config", "/src/.jscpd.json", "--reporters", "json", "--output", "/out"
    )
    Invoke-ScannerCase "jscpd" $arguments @(0) $jscpdReport "Jscpd" 0 | Out-Null

    $zap = Get-Scanner "zap-active"
    $zapFixture = New-CaseDirectory "zap-fixture"
    $zapOutput = New-CaseDirectory "zap-output"
    $zapHome = New-CaseDirectory "zap-home"
    $zapServer = Join-Path $zapFixture "server.cjs"
    @'
const http = require("node:http");
const spec = {
  openapi: "3.0.3",
  info: { title: "Aetheus scanner campaign", version: "1.0.0" },
  servers: [{ url: "http://aetheus-scan-target:8080" }],
  paths: {
    "/api/ping": {
      get: {
        responses: {
          "200": { description: "pong", content: { "application/json": { schema: { type: "object" } } } }
        }
      }
    }
  }
};
http.createServer((request, response) => {
  if (request.url === "/openapi.json") {
    response.writeHead(200, { "content-type": "application/json" });
    response.end(JSON.stringify(spec));
    return;
  }
  if (request.url === "/api/ping") {
    response.writeHead(200, { "content-type": "application/json" });
    response.end(JSON.stringify({ value: request.headers["x-campaign-input"] || "pong" }));
    return;
  }
  response.writeHead(200, { "content-type": "text/html" });
  response.end('<html><body><a href="/api/ping?value=campaign">Ping</a></body></html>');
}).listen(8080, "0.0.0.0");
'@ | Set-Content -LiteralPath $zapServer -Encoding utf8NoBOM

    $networkName = "aetheus-scan-net-$campaignId"
    $networkResult = Invoke-NativeProcess "docker" @(
        "network", "create", "--label", $campaignLabel, $networkName
    ) 60
    if ($networkResult.exitCode -ne 0) {
        throw "Unable to create the campaign-only ZAP network."
    }

    $targetArguments = @(
        "run", "-d",
        "--name", "aetheus-scan-target",
        "--label", $campaignLabel,
        "--network", $networkName,
        "--read-only",
        "--cap-drop", "ALL",
        "--security-opt", "no-new-privileges",
        "--user", "1000:1000",
        "--tmpfs", "/tmp:rw,nosuid,nodev,size=67108864,mode=1777",
        "--mount", (Get-BindMount $zapFixture "/fixture" -ReadOnly),
        $nodeScanner.image,
        "node", "/fixture/server.cjs"
    )
    $targetResult = Invoke-NativeProcess "docker" $targetArguments 60
    if ($targetResult.exitCode -ne 0) {
        throw "Unable to start the campaign-only ZAP target."
    }

    $ready = $false
    foreach ($attempt in 1..30) {
        $probe = Invoke-NativeProcess "docker" @(
            "exec", "aetheus-scan-target", "node", "-e",
            "fetch('http://127.0.0.1:8080/api/ping').then(r=>{if(!r.ok)process.exit(1)}).catch(()=>process.exit(1))"
        ) 10
        if ($probe.exitCode -eq 0) {
            $ready = $true
            break
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) {
        throw "Campaign-only ZAP target did not become ready."
    }

    $seedArguments = Get-CommonDockerArguments "zap-home-seed" "none" -ReadOnly
    $seedArguments += @(
        "--mount", (Get-BindMount $zapHome "/seed"),
        "--entrypoint", "/bin/sh",
        $zap.image,
        "-c", "mkdir -p /seed/.ZAP && cp -a /home/zap/.ZAP/. /seed/.ZAP/"
    )
    $seedResult = Invoke-NativeProcess "docker" $seedArguments 300
    if ($seedResult.exitCode -ne 0) {
        throw "Unable to seed the bounded ZAP home."
    }

    foreach ($case in @(
        @{
            Name = "zap-passive"
            Scanner = (Get-Scanner "zap-passive")
            Arguments = @("-t", "http://aetheus-scan-target:8080", "-J", "passive.json", "-m", "5", "-z", "-silent")
            Report = "passive.json"
        },
        @{
            Name = "zap-api"
            Scanner = (Get-Scanner "zap-api")
            Arguments = @("-t", "http://aetheus-scan-target:8080/openapi.json", "-f", "openapi", "-J", "api.json", "-m", "5", "-z", "-silent")
            Report = "api.json"
        },
        @{
            Name = "zap-active"
            Scanner = (Get-Scanner "zap-active")
            Arguments = @("-t", "http://aetheus-scan-target:8080", "-J", "active.json", "-m", "5", "-z", "-silent")
            Report = "active.json"
        }
    )) {
        $report = Join-Path $zapOutput $case.Report
        $arguments = Get-CommonDockerArguments $case.Name $networkName -ReadOnly
        $arguments += @(
            "--tmpfs", "/tmp:rw,nosuid,nodev,size=268435456,mode=1777",
            "--env", "HOME=/home/zap/runtime",
            "--env", "_JAVA_OPTIONS=-Duser.home=/home/zap/runtime",
            "--mount", (Get-BindMount $zapHome "/home/zap/runtime"),
            "--mount", (Get-BindMount $zapOutput "/zap/wrk"),
            "--workdir", "/zap/wrk",
            "--entrypoint", $case.Scanner.entryPoint,
            $case.Scanner.image
        )
        $arguments += $case.Arguments
        Invoke-ScannerCase $case.Name $arguments @(0, 1, 2) $report "Zap" 1 2147483647 900 | Out-Null
    }
}
catch {
    $campaignError = $_.Exception.ToString()
}
finally {
    $cleanup.attempted = $true
    try {
        $containerList = Invoke-NativeProcess "docker" @(
            "ps", "-aq", "--filter", "label=$campaignLabel"
        ) 30
        if ($containerList.exitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($containerList.stdoutTail)) {
            $containerIds = @($containerList.stdoutTail -split "`n" |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
            [void](Invoke-NativeProcess "docker" (@("rm", "-f") + $containerIds) 120)
        }

        $networkList = Invoke-NativeProcess "docker" @(
            "network", "ls", "-q", "--filter", "label=$campaignLabel"
        ) 30
        if ($networkList.exitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($networkList.stdoutTail)) {
            $networkIds = @($networkList.stdoutTail -split "`n" |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
            [void](Invoke-NativeProcess "docker" (@("network", "rm") + $networkIds) 120)
        }

        if (Test-Path -LiteralPath $tempRoot) {
            $resolvedTemp = [IO.Path]::GetFullPath($tempRoot)
            $resolvedTempLeaf = Split-Path -Leaf $resolvedTemp
            if ((-not $resolvedTemp.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase)) -or
                (-not $resolvedTempLeaf.StartsWith("aetheus-scanner-campaign-", [StringComparison]::Ordinal))) {
                throw "Refusing to remove a campaign workspace outside the guarded temporary prefix."
            }
            Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
        }

        $cleanup.workspaceRemoved = -not (Test-Path -LiteralPath $tempRoot)
        $cleanup.residualContainers = Get-LabeledResourceCount "container"
        $cleanup.residualNetworks = Get-LabeledResourceCount "network"
        $cleanup.passed = $cleanup.workspaceRemoved `
            -and $cleanup.residualContainers -eq 0 `
            -and $cleanup.residualNetworks -eq 0
    }
    catch {
        $cleanup.error = $_.Exception.ToString()
        if ($null -eq $campaignError) {
            $campaignError = "Cleanup failed: $($_.Exception)"
        }
    }
}

$gitHead = (Invoke-NativeProcess "git" @("rev-parse", "HEAD") 30).stdoutTail.Trim()
$gitBranch = (Invoke-NativeProcess "git" @("branch", "--show-current") 30).stdoutTail.Trim()
$gitStatus = (Invoke-NativeProcess "git" @("status", "--porcelain=v1", "--untracked-files=all") 60).stdoutTail
$manifestSha = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
$rulesSha = (Get-FileHash -LiteralPath (
    Join-Path $repoRoot ".aetheus/security-rules/opengrep/aetheus-security.yml"
) -Algorithm SHA256).Hash.ToLowerInvariant()
$allCasesPassed = $results.Count -gt 0 -and @($results | Where-Object { -not $_.passed }).Count -eq 0
$status = if ($null -eq $campaignError -and $allCasesPassed -and $cleanup.passed) { "Passed" } else { "Failed" }

$attestation = [ordered]@{
    schemaVersion = 1
    campaign = "PLAN-006-terminal-scanner-campaign"
    campaignId = $campaignId
    status = $status
    startedAt = $startedAt.ToString("O")
    completedAt = [DateTimeOffset]::UtcNow.ToString("O")
    source = [ordered]@{
        repository = $repoRoot
        branch = $gitBranch
        commit = $gitHead
        workingTreeStatusSha256 = Get-Sha256Text $gitStatus
        workingTreeDirty = -not [string]::IsNullOrWhiteSpace($gitStatus)
        gitVisibleFileCount = if ($null -ne (Get-Variable gitSourceSnapshot -ErrorAction SilentlyContinue)) {
            $gitSourceSnapshot.fileCount
        }
        else {
            0
        }
        scannerManifestSha256 = $manifestSha
        openGrepRulesSha256 = $rulesSha
    }
    environment = [ordered]@{
        os = [Environment]::OSVersion.ToString()
        architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        powershell = $PSVersionTable.PSVersion.ToString()
        dockerServer = (Invoke-NativeProcess "docker" @("version", "--format", "{{.Server.Version}}") 30).stdoutTail.Trim()
        offline = [bool]$Offline
    }
    images = @($images)
    preconditions = [ordered]@{
        lockedLinuxNodeRestore = if ($null -ne (Get-Variable nodeRestoreEvidence -ErrorAction SilentlyContinue)) {
            $nodeRestoreEvidence
        }
        else {
            $null
        }
    }
    cases = @($results)
    reproducibility = [ordered]@{
        openGrepPositiveRuns = 2
        identicalSarifSha256 = if ($results.Count -gt 0) {
            $openGrepEvidence = @($results | Where-Object { $_.name -like "opengrep-positive-*" })
            $openGrepEvidence.Count -eq 2 `
                -and $openGrepEvidence[0].report.sha256 -eq $openGrepEvidence[1].report.sha256
        }
        else {
            $false
        }
    }
    cleanup = $cleanup
    failure = $campaignError
}

$evidenceDirectory = Split-Path -Parent $EvidencePath
New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
$attestation | ConvertTo-Json -Depth 30 |
    Set-Content -LiteralPath $EvidencePath -Encoding utf8NoBOM

Write-Host "Campaign status: $status"
Write-Host "Evidence: $EvidencePath"
if ($status -ne "Passed") {
    throw "PLAN-006 scanner campaign failed. See '$EvidencePath'."
}
