// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Operations;

namespace Aetheus.Agent.Core.Tests;

public sealed class ScannerDownloadResilienceAuditTests
{
    [Fact]
    public void ImmutableScannerDownload_RetriesTransientFailuresWithoutBackendCredentials()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Aetheus.Agent.Core",
            "Extensions",
            "AgentCoreServiceCollectionExtensions.cs"));
        var registrationStart = source.IndexOf(
            "services.AddHttpClient(\"AetheusScannerDownload\"",
            StringComparison.Ordinal);
        var nextRegistration = source.IndexOf(
            "services.AddHttpClient(",
            registrationStart + 1,
            StringComparison.Ordinal);

        Assert.True(registrationStart >= 0);
        Assert.True(nextRegistration > registrationStart);
        var registration = source[registrationStart..nextRegistration];

        Assert.Contains(".AddStandardResilienceHandler();", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("BearerTokenHandler", registration, StringComparison.Ordinal);
        Assert.Contains("UseCookies = false", registration, StringComparison.Ordinal);
        Assert.Contains("client.Timeout = Timeout.InfiniteTimeSpan", registration, StringComparison.Ordinal);
    }

    [Fact]
    public void ScannerDownloadFailure_ExposesAStableTransientMarkerForPipelineRetry()
    {
        var transientMessage = ScannerOperationExecutor.GetScannerUnavailableMessage(
            new HttpRequestException("Name or service not known"));
        var permanentMessage = ScannerOperationExecutor.GetScannerUnavailableMessage(
            new IOException("Hash mismatch"));

        Assert.Contains("download", transientMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("network", transientMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("network", permanentMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot() => Aetheus.Agent.Core.Tests.RepositoryScan.Root;
}
