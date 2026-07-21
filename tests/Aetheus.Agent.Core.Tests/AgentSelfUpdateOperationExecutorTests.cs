// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Executors;
using Aetheus.Agent.Core.Operations;
using Aetheus.Agent.Core.Services;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Aetheus.Agent.Core.Tests;

public class AgentSelfUpdateOperationExecutorTests : IDisposable
{
    private readonly string _workDir = Directory.CreateTempSubdirectory("agent-update-test-").FullName;
    private readonly IServerApiClient _api = Substitute.For<IServerApiClient>();
    private readonly AgentState _state = new() { ServerId = 7 };

    public void Dispose()
    {
        try { Directory.Delete(_workDir, recursive: true); } catch (Exception) { /* best effort */ }
    }

    private sealed class StubHandler(HttpStatusCode status, byte[]? body, string? sha, long? contentLength) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var resp = new HttpResponseMessage(status) { Content = new ByteArrayContent(body ?? []) };
            if (contentLength.HasValue) resp.Content.Headers.ContentLength = contentLength.Value;
            // X-Content-SHA256 must go on the RESPONSE (message) headers, NOT Content.Headers: the
            // backend sets it via httpContext.Response.Headers and the agent reads it from
            // response.Headers.TryGetValues. Putting it on Content.Headers (the old stub) made every
            // "checksum" test silently hit the missing-header branch, so the real hash-comparison
            // happy path was never exercised.
            if (sha is not null) resp.Headers.TryAddWithoutValidation("X-Content-SHA256", sha);
            return Task.FromResult(resp);
        }
    }

    private AgentSelfUpdateOperationExecutor Build(
        HttpStatusCode status, byte[]? body, string? sha, bool allowInsecure, long? contentLength = null)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ =>
            new HttpClient(new StubHandler(status, body, sha, contentLength)));

        var options = Options.Create(new AetheusAgentOptions
        {
            WorkDirectory = _workDir,
            ServerUrl = "https://backend.test",
            AllowInsecureCerts = allowInsecure
        });

        return new AgentSelfUpdateOperationExecutor(
            factory, options, _api, _state,
            NullLogger<AgentSelfUpdateOperationExecutor>.Instance);
    }

    private Task<ExecutorResult> Run(AgentSelfUpdateOperationExecutor sut) =>
        sut.ExecuteAsync(OperationKind.AgentSelfUpdate, "", 60, (_, _) => Task.CompletedTask, TestContext.Current.CancellationToken);

    [Fact]
    public void CanHandle_OnlyAgentSelfUpdate()
    {
        var sut = Build(HttpStatusCode.OK, [1], null, false);
        Assert.True(sut.CanHandle(OperationKind.AgentSelfUpdate));
        Assert.False(sut.CanHandle(OperationKind.ServiceStart));
    }

    [Fact]
    public void IsSameOrChildPath_RejectsOnlyPathsInsideInstallRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "aetheus-install");

        Assert.True(AgentSelfUpdateFileSystem.IsSameOrChildPath(root, root));
        Assert.True(AgentSelfUpdateFileSystem.IsSameOrChildPath(Path.Combine(root, "work"), root));
        Assert.False(AgentSelfUpdateFileSystem.IsSameOrChildPath(root + "-other", root));
    }

    [Fact]
    public async Task Execute_WrongKind_ReturnsFailure()
    {
        var sut = Build(HttpStatusCode.OK, [1], null, false);

        var result = await sut.ExecuteAsync(OperationKind.ServiceStart, "", 60, (_, _) => Task.CompletedTask, TestContext.Current.CancellationToken);

        Assert.Equal(-1, result.ExitCode);
    }

    [Fact]
    public async Task Execute_DownloadHttpError_ReturnsFailure()
    {
        var result = await Run(Build(HttpStatusCode.NotFound, null, null, false));

        Assert.Equal(-1, result.ExitCode);
        await _api.Received().ReportUpdateProgressAsync(7, Arg.Any<Shared.DTOs.AgentUpdateProgressReport>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_DownloadFailure_PreservesLastKnownRollback()
    {
        var rollbackDir = Directory.CreateDirectory(Path.Combine(_workDir, ".agent-rollback"));
        var marker = Path.Combine(rollbackDir.FullName, "previous-agent.dll");
        await File.WriteAllTextAsync(marker, "last-known-good", TestContext.Current.CancellationToken);

        var result = await Run(Build(HttpStatusCode.NotFound, null, null, false));

        Assert.Equal(-1, result.ExitCode);
        Assert.Equal("last-known-good", await File.ReadAllTextAsync(
            marker, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Execute_EmptyArchive_ReturnsFailure()
    {
        var result = await Run(Build(HttpStatusCode.OK, [], null, false));

        Assert.Equal(-1, result.ExitCode);
    }

    [Fact]
    public async Task Execute_MissingChecksumHeader_Rejected_WhenNotInsecure()
    {
        var result = await Run(Build(HttpStatusCode.OK, [1, 2, 3, 4], sha: null, allowInsecure: false));

        Assert.Equal(-1, result.ExitCode);
    }

    [Fact]
    public async Task Execute_InvalidChecksum_Rejected()
    {
        var result = await Run(Build(HttpStatusCode.OK, [1, 2, 3, 4], sha: "DEADBEEF", allowInsecure: false));

        Assert.Equal(-1, result.ExitCode);
    }

    [Fact]
    public async Task Execute_OversizedContentLength_IsRejectedBeforeDownload()
    {
        var result = await Run(Build(
            HttpStatusCode.OK,
            [1],
            sha: new string('A', 64),
            allowInsecure: false,
            contentLength: AgentSelfUpdateOperationExecutor.MaxArchiveBytes + 1));

        Assert.Equal(-1, result.ExitCode);
        var archiveName = OperatingSystem.IsWindows()
            ? "aetheus-agent-win-x64.zip"
            : "aetheus-agent-linux-x64.tar.gz";
        Assert.False(File.Exists(Path.Combine(_workDir, ".agent-update", archiveName)));
    }

    [Fact]
    public async Task Execute_ValidChecksum_PassesIntegrityGate_ThenFailsAtExtraction()
    {
        // Exercises the REAL fail-closed integrity happy path: a matching X-Content-SHA256 header
        // (on response.Headers, where the agent reads it) passes the gate, THEN extraction of
        // non-zip bytes fails. Proof the gate passed: the Downloaded phase (reported only AFTER
        // DownloadAsync returns true, i.e. after the hash comparison succeeds) fired.
        var body = new byte[] { 1, 2, 3, 4, 5 };
        var sha = Convert.ToHexString(SHA256.HashData(body));

        var result = await Run(Build(HttpStatusCode.OK, body, sha, allowInsecure: false));

        Assert.Equal(-1, result.ExitCode);
        await _api.Received().ReportUpdateProgressAsync(7,
            Arg.Is<Shared.DTOs.AgentUpdateProgressReport>(r => r.Phase == AgentUpdatePhase.Downloaded),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_ValidArchive_DownloadsAndExtracts_ThenFailsStagingWithoutMarkerBinary()
    {
        // Success-path coverage for the previously-untested middle (download SHA gate + real
        // extraction) WITHOUT reaching the binary swap: the swap spawns a detached updater that
        // would overwrite the running binaries, so we stop it at staging validation by omitting
        // the expected agent binary from an otherwise-valid archive. A correct SHA header exercises
        // the real integrity gate (no allowInsecure bypass) end-to-end.
        var archive = BuildPlatformArchive(("readme.txt", "not the agent binary"));
        var sha = Convert.ToHexString(SHA256.HashData(archive));

        var result = await Run(Build(HttpStatusCode.OK, archive, sha, allowInsecure: false));

        Assert.Equal(-1, result.ExitCode);
        // Proof the download SHA gate passed and extraction actually ran: those phases were reported.
        await _api.Received().ReportUpdateProgressAsync(7,
            Arg.Is<Shared.DTOs.AgentUpdateProgressReport>(r => r.Phase == AgentUpdatePhase.Downloaded),
            Arg.Any<CancellationToken>());
        await _api.Received().ReportUpdateProgressAsync(7,
            Arg.Is<Shared.DTOs.AgentUpdateProgressReport>(r => r.Phase == AgentUpdatePhase.Extracting),
            Arg.Any<CancellationToken>());
        // And it failed at staging validation, not before extraction.
        await _api.Received().ReportUpdateProgressAsync(7,
            Arg.Is<Shared.DTOs.AgentUpdateProgressReport>(r => r.Phase == AgentUpdatePhase.Failed && r.Message == "staged payload invalid"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SnapshotRollback_KeepsPreviousBinariesButNotConfiguration()
    {
        var installDir = Directory.CreateDirectory(Path.Combine(_workDir, "install"));
        var rollbackDir = Path.Combine(_workDir, "rollback");
        await File.WriteAllTextAsync(Path.Combine(installDir.FullName, "Aetheus.Agent.Linux.dll"), "binary", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(installDir.FullName, "appsettings.json"), "secret", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(installDir.FullName, "stale.bak"), "stale", TestContext.Current.CancellationToken);

        AgentSelfUpdateFileSystem.SnapshotRollback(installDir.FullName, rollbackDir);

        Assert.True(File.Exists(Path.Combine(rollbackDir, "Aetheus.Agent.Linux.dll")));
        Assert.True(File.Exists(Path.Combine(rollbackDir, "rollback-version.txt")));
        Assert.False(File.Exists(Path.Combine(rollbackDir, "appsettings.json")));
        Assert.False(File.Exists(Path.Combine(rollbackDir, "stale.bak")));
    }

    [Fact]
    public async Task ValidateArchive_RejectsPathTraversal()
    {
        var archivePath = Path.Combine(_workDir, "traversal.zip");
        await File.WriteAllBytesAsync(archivePath, BuildZip(("../escape.txt", "forbidden")),
            TestContext.Current.CancellationToken);
        var extractDir = Directory.CreateDirectory(Path.Combine(_workDir, "extract-traversal"));

        var valid = AgentSelfUpdateOperationExecutor.ValidateArchive(
            archivePath, extractDir.FullName, isWindows: true, out var error);

        Assert.False(valid);
        Assert.Contains("escapes", error, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(_workDir, "escape.txt")));
    }

    [Fact]
    public void ValidateArchive_RejectsSymbolicLinkEntries()
    {
        var archivePath = Path.Combine(_workDir, "symlink.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("Aetheus.Agent.Windows.exe");
            entry.ExternalAttributes = 0xA000 << 16;
            using var writer = new StreamWriter(entry.Open());
            writer.Write("target");
        }
        var extractDir = Directory.CreateDirectory(Path.Combine(_workDir, "extract-symlink"));

        var valid = AgentSelfUpdateOperationExecutor.ValidateArchive(
            archivePath, extractDir.FullName, isWindows: true, out var error);

        Assert.False(valid);
        Assert.Contains("symbolic link", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateArchive_RejectsExcessiveEntryCount()
    {
        var archivePath = Path.Combine(_workDir, "too-many-entries.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            for (var index = 0; index <= AgentSelfUpdateOperationExecutor.MaxArchiveEntries; index++)
                archive.CreateEntry($"payload/{index}.txt");
        }
        var extractDir = Directory.CreateDirectory(Path.Combine(_workDir, "extract-too-many"));

        var valid = AgentSelfUpdateOperationExecutor.ValidateArchive(
            archivePath, extractDir.FullName, isWindows: true, out var error);

        Assert.False(valid);
        Assert.Contains("entries", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReplaceInstallFilesWithRollback_PartialFailureRestoresPreviousBinariesAndConfiguration()
    {
        var installDir = Directory.CreateDirectory(Path.Combine(_workDir, "install-partial"));
        var sourceDir = Directory.CreateDirectory(Path.Combine(_workDir, "source-partial"));
        var rollbackDir = Path.Combine(_workDir, "rollback-partial");
        await File.WriteAllTextAsync(Path.Combine(installDir.FullName, "Aetheus.Agent.Linux.dll"), "old-agent",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(installDir.FullName, "dependency.dll"), "old-dependency",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(installDir.FullName, "appsettings.json"), "operator-config",
            TestContext.Current.CancellationToken);
        var nestedInstall = Directory.CreateDirectory(Path.Combine(installDir.FullName, "runtimes", "linux-x64", "native"));
        await File.WriteAllTextAsync(Path.Combine(nestedInstall.FullName, "native.so"), "old-native",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(sourceDir.FullName, "a-new.dll"), "new-file",
            TestContext.Current.CancellationToken);
        var nestedSource = Directory.CreateDirectory(Path.Combine(sourceDir.FullName, "runtimes", "linux-x64", "native"));
        await File.WriteAllTextAsync(Path.Combine(nestedSource.FullName, "new-native.so"), "new-native",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(sourceDir.FullName, "z-blocked.dll"), "copy-must-fail",
            TestContext.Current.CancellationToken);
        AgentSelfUpdateFileSystem.SnapshotRollback(installDir.FullName, rollbackDir);

        var error = Assert.Throws<InvalidOperationException>(() =>
            AgentSelfUpdateFileSystem.ReplaceInstallFilesWithRollback(
                sourceDir.FullName, installDir.FullName, rollbackDir,
                relativePath =>
                {
                    if (relativePath == "z-blocked.dll") throw new IOException("injected copy failure");
                }));

        Assert.Contains("restored automatically", error.Message, StringComparison.Ordinal);
        Assert.Equal("old-agent", await File.ReadAllTextAsync(
            Path.Combine(installDir.FullName, "Aetheus.Agent.Linux.dll"), TestContext.Current.CancellationToken));
        Assert.Equal("old-dependency", await File.ReadAllTextAsync(
            Path.Combine(installDir.FullName, "dependency.dll"), TestContext.Current.CancellationToken));
        Assert.Equal("old-native", await File.ReadAllTextAsync(
            Path.Combine(installDir.FullName, "runtimes", "linux-x64", "native", "native.so"),
            TestContext.Current.CancellationToken));
        Assert.Equal("operator-config", await File.ReadAllTextAsync(
            Path.Combine(installDir.FullName, "appsettings.json"), TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(installDir.FullName, "a-new.dll")));
        Assert.False(File.Exists(Path.Combine(
            installDir.FullName, "runtimes", "linux-x64", "native", "new-native.so")));
    }

    [Fact]
    public void WindowsUpdater_ContainsFailClosedRestoreAndParsesOnWindows()
    {
        var stagingRoot = Directory.CreateDirectory(Path.Combine(_workDir, "windows-updater"));

        var updater = AgentSelfUpdateOperationExecutor.WriteWindowsUpdater(stagingRoot.FullName);
        var script = File.ReadAllText(updater);

        Assert.Contains("$RollbackReady = $true", script, StringComparison.Ordinal);
        Assert.Contains("previous binaries restored automatically", script, StringComparison.Ordinal);
        Assert.Contains("automatic rollback also failed", script, StringComparison.Ordinal);
        Assert.True(
            script.IndexOf("Copy-Item -Path (Join-Path $Src '*')", StringComparison.Ordinal)
            < script.IndexOf("Get-ChildItem -LiteralPath $Rollback -Force", StringComparison.Ordinal));

        if (!OperatingSystem.IsWindows()) return;

        var parser = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        parser.ArgumentList.Add("-NoProfile");
        parser.ArgumentList.Add("-NonInteractive");
        parser.ArgumentList.Add("-Command");
        parser.ArgumentList.Add(
            "$errors = $null; [System.Management.Automation.Language.Parser]::ParseFile($env:AETHEUS_UPDATER_PATH, [ref]$null, [ref]$errors) | Out-Null; " +
            "if ($errors.Count -gt 0) { $errors | ForEach-Object { [Console]::Error.WriteLine($_.Message) }; exit 1 }");
        parser.Environment["AETHEUS_UPDATER_PATH"] = updater;
        using var process = Process.Start(parser);
        Assert.NotNull(process);
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, standardError);
    }

    [Fact]
    public async Task WindowsUpdater_PartialCopyFailureRestoresPreviousBinaries()
    {
        if (!OperatingSystem.IsWindows()) return;

        var stagingRoot = Directory.CreateDirectory(Path.Combine(_workDir, "windows-update-runtime"));
        var sourceDir = Directory.CreateDirectory(Path.Combine(stagingRoot.FullName, "source"));
        var installDir = Directory.CreateDirectory(Path.Combine(_workDir, "windows-install"));
        await File.WriteAllTextAsync(Path.Combine(installDir.FullName, "Aetheus.Agent.Windows.exe"), "old-agent",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(installDir.FullName, "dependency.dll"), "old-dependency",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(installDir.FullName, "appsettings.json"), "operator-config",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(sourceDir.FullName, "a-new.dll"), "partial-new-file",
            TestContext.Current.CancellationToken);
        var blockedSource = Path.Combine(sourceDir.FullName, "z-blocked.dll");
        await File.WriteAllTextAsync(blockedSource, "copy-must-fail", TestContext.Current.CancellationToken);
        var updater = AgentSelfUpdateOperationExecutor.WriteWindowsUpdater(stagingRoot.FullName);

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", updater,
            "-AgentPid", int.MaxValue.ToString(), "-Src", sourceDir.FullName, "-Dest", installDir.FullName
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using (File.Open(blockedSource, FileMode.Open, FileAccess.Read, FileShare.None))
        using (var process = Process.Start(startInfo))
        {
            Assert.NotNull(process);
            var standardError = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.True(process.ExitCode == 1, standardError);
        }

        Assert.Equal("old-agent", await File.ReadAllTextAsync(
            Path.Combine(installDir.FullName, "Aetheus.Agent.Windows.exe"), TestContext.Current.CancellationToken));
        Assert.Equal("old-dependency", await File.ReadAllTextAsync(
            Path.Combine(installDir.FullName, "dependency.dll"), TestContext.Current.CancellationToken));
        Assert.Equal("operator-config", await File.ReadAllTextAsync(
            Path.Combine(installDir.FullName, "appsettings.json"), TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(installDir.FullName, "a-new.dll")));
        Assert.Contains("previous binaries restored automatically",
            await File.ReadAllTextAsync(Path.Combine(stagingRoot.FullName, "update.log"),
                TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    private static byte[] BuildZip(params (string Name, string Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var w = new StreamWriter(entry.Open());
                w.Write(content);
            }
        }
        return ms.ToArray();
    }

    private static byte[] BuildPlatformArchive(params (string Name, string Content)[] entries)
    {
        if (OperatingSystem.IsWindows()) return BuildZip(entries);

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var archive = new System.Formats.Tar.TarWriter(gzip, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var data = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
                var entry = new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, name)
                {
                    DataStream = data
                };
                archive.WriteEntry(entry);
            }
        }

        return output.ToArray();
    }
}
