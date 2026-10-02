// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using System.Security.Cryptography;

namespace Aetheus.Agent.Core.Operations;

internal sealed class ScannerReportPublisher(IServerApiClient apiClient, TimeProvider timeProvider)
{
    public Task<ExecutorResult> PublishFailureAsync(
        ScannerManifestEntry scanner, int runId, IReadOnlyDictionary<string, string> envVars,
        DateTime startedAt, AnalysisReportStatus status, string error,
        Func<string, TaskLogLevel, Task> onOutput, CancellationToken ct) =>
        PublishStatusAsync(scanner, scanner.Version, runId, envVars, startedAt, status, error, onOutput, ct);

    public async Task<ExecutorResult> PublishStatusAsync(
        ScannerManifestEntry scanner, string scannerVersion, int runId,
        IReadOnlyDictionary<string, string> envVars, DateTime startedAt,
        AnalysisReportStatus status, string message, Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken ct)
    {
        var isFailure = status is not AnalysisReportStatus.NotApplicable;
        await onOutput(message, isFailure ? TaskLogLevel.Error : TaskLogLevel.Info).ConfigureAwait(false);
        var published = await apiClient.PublishAnalysisReportAsync(runId, new PublishAnalysisReportRequest
        {
            ScannerKey = scanner.Key,
            ScannerName = scanner.Name,
            ScannerVersion = scannerVersion,
            Category = Enum.Parse<AnalysisCategory>(scanner.Category, true),
            Status = status,
            Format = Enum.Parse<AnalysisReportFormat>(scanner.ReportFormat, true),
            StageName = ScannerOperationSupport.Value(envVars, "AETHEUS_STAGE_NAME"),
            StepName = ScannerOperationSupport.Value(envVars, "AETHEUS_STEP_NAME"),
            EnvironmentName = ScannerOperationSupport.Value(envVars, "AETHEUS_SCANNER_ENVIRONMENT_NAME"),
            DastLeaseToken = ScannerOperationSupport.Value(envVars, "AETHEUS_SCANNER_DAST_LEASE_TOKEN"),
            DastTargetUrl = ScannerOperationSupport.Value(envVars, "AETHEUS_SCANNER_TARGET_URL"),
            StartedAt = startedAt,
            CompletedAt = timeProvider.GetUtcNow().UtcDateTime,
            ErrorMessage = isFailure ? message : null
        }, ct).ConfigureAwait(false);
        if (published is null || published.GateStatus is AnalysisGateStatus.Blocked or AnalysisGateStatus.Error)
            return new ExecutorResult(-1, status == AnalysisReportStatus.TimedOut);
        return new ExecutorResult(isFailure ? -1 : 0, status == AnalysisReportStatus.TimedOut);
    }
}

internal static class ScannerOperationSupport
{
    internal static string ResolveSourceDirectory(
        string workDirectory, int runId, IReadOnlyDictionary<string, string> envVars)
    {
        if (string.Equals(Value(envVars, "AETHEUS_WORKSPACE_MODE"), "container", StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(Path.Combine(workDirectory, "cw", runId.ToString()));
        var configured = Value(envVars, "AETHEUS_WORKING_DIR");
        return string.IsNullOrWhiteSpace(configured) ? string.Empty : Path.GetFullPath(configured);
    }

    internal static string ScannerUnavailableMessage(Exception exception) =>
        exception is HttpRequestException
            ? "Scanner runtime download failed because of a transient network error."
            : "Scanner runtime or verified binary is unavailable.";

    internal static Task<ImageAssociationResult> ValidateImageAssociationAsync(
        ScannerManifestEntry scanner, string sourceDirectory,
        IReadOnlyDictionary<string, string> envVars, CancellationToken ct) =>
        ScannerOperationValidator.ValidateImageAssociationAsync(scanner, sourceDirectory, envVars, ct);

    internal static ProcessStartInfo BaseProcess(string executable) => new()
    {
        FileName = executable,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    internal static async Task<bool> HasExpectedHashAsync(string path, string expected, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    internal static Task<bool> ValidateReportStructureAsync(
        ScannerManifestEntry scanner, string path, CancellationToken ct) =>
        ScannerOperationValidator.ValidateReportStructureAsync(scanner, path, ct);

    internal static string BuildRawArtifactName(
        ScannerManifestEntry scanner,
        IReadOnlyDictionary<string, string> envVars)
    {
        var baseName = $"analysis-{scanner.Key}";
        if (!string.Equals(scanner.Category, nameof(AnalysisCategory.Dast), StringComparison.OrdinalIgnoreCase))
            return baseName;

        var stageName = Value(envVars, "AETHEUS_STAGE_NAME") ?? string.Empty;
        var stepName = Value(envVars, "AETHEUS_STEP_NAME") ?? string.Empty;
        var identity = System.Text.Encoding.UTF8.GetBytes($"{stageName}\n{stepName}");
        var suffix = Convert.ToHexStringLower(SHA256.HashData(identity))[..12];
        return $"{baseName}-{suffix}";
    }

    internal static bool TryPositiveInt(IReadOnlyDictionary<string, string> env, string key, out int value) =>
        int.TryParse(Value(env, key), out value) && value > 0;

    internal static string? Value(IReadOnlyDictionary<string, string> env, string key) =>
        env.TryGetValue(key, out var value) ? value : null;
}

internal sealed record ScanInvocation(
    ScannerManifestEntry Scanner, int RunId, IReadOnlyDictionary<string, string> Environment,
    int TimeoutSeconds, DateTime StartedAt, string ScanRoot, string OutputDirectory,
    Func<string, TaskLogLevel, Task> OnOutput, CancellationToken CancellationToken);

internal sealed class ScannerExecutionResources
{
    public RestrictedScannerEgress? Egress { get; set; }
    public ScannerSourceProjectionLease? SourceProjection { get; set; }
    public IAsyncDisposable? ResourceReservation { get; set; }
}

internal sealed record ProcessCreation(ProcessStartInfo? Process, ExecutorResult? Failure);
