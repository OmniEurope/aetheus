// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using System.Globalization;
using Aetheus.Agent.Core.Operations;

namespace Aetheus.Back.Tests.Pipelines;

/// <summary>
/// The rule that separates "the suite ran and failed" from "the suite proved nothing" now exists
/// twice: in C# for the typed <c>dotnet-test</c> step (PLAN-006 lot 11.3), and in
/// <c>deploy/scripts/classify-dotnet-test-result.mjs</c> for the pipeline steps that orchestrate
/// their own suites through shell (the QA integration and E2E runners, which drive docker before
/// they ever reach a test).
///
/// Two implementations of a safety rule drift, and the drift is invisible until the day one of them
/// grades a broken run as a clean one. So neither is allowed to move alone: every case below is fed
/// to both, and the verdicts must match. A case that one accepts and the other refuses fails here.
///
/// This is a conformance test, not a duplicate of either suite's own tests. Each implementation is
/// pinned on its own side (<c>DotnetTestResultClassifierTests</c>, <c>classify-dotnet-test-result.test.mjs</c>);
/// what this adds is that they agree.
/// </summary>
public sealed class DotnetTestClassifierConformanceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "aetheus-classifier-conformance-" + Guid.NewGuid().ToString("N"));

    /// <summary>Every case is (exit code, TRX, coverage reports). Named so a failure says which
    /// situation the two implementations disagreed about.</summary>
    public static TheoryData<string, int, string, string[]> Cases()
    {
        const string validCoverage =
            """<coverage lines-valid="10" lines-covered="8" line-rate="0.8" />""";

        return new TheoryData<string, int, string, string[]>
        {
            { "clean run", 0, Trx(), [validCoverage] },
            { "failed assertions", 1, Trx(failed: 2, outcome: "Failed"), [validCoverage] },
            { "test host error", 1, Trx(error: 1, outcome: "Failed"), [validCoverage] },
            // Reaches the technical-failure check rather than stopping at the exit-code
            // reconciliation before it: the counters here are otherwise a perfectly gradeable
            // "ran and failed", so only the error counter can refuse it.
            { "test host error alongside real failures", 1, Trx(failed: 1, error: 1, outcome: "Failed"), [validCoverage] },
            { "zero exit contradicted by failures", 0, Trx(failed: 1, outcome: "Failed"), [validCoverage] },
            { "non-zero exit with nothing failed", 1, Trx(), [validCoverage] },
            { "aborted outcome", 1, Trx(outcome: "Aborted"), [validCoverage] },
            { "executed exceeds total", 0, Trx(executedDelta: 1), [validCoverage] },
            { "missing failed counter", 0, TrxWithoutFailedCounter(), [validCoverage] },
            { "no coverage report", 0, Trx(), [] },
            { "second coverage report malformed", 0, Trx(), [validCoverage, "<not-coverage />"] },
            {
                "impossible coverage counters", 0, Trx(),
                ["""<coverage lines-valid="4" lines-covered="9" line-rate="0.75" />"""]
            },
            { "empty trx", 0, string.Empty, [validCoverage] },
        };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task BothImplementationsReachTheSameVerdict(
        string caseName, int exitCode, string trx, string[] coverageReports)
    {
        var directory = Path.Combine(_root, SafeName(caseName));
        var trxPath = Path.Combine(directory, "result.trx");
        var coverageDirectory = Path.Combine(directory, "coverage");
        Directory.CreateDirectory(coverageDirectory);
        await File.WriteAllTextAsync(trxPath, trx, TestContext.Current.CancellationToken);
        for (var index = 0; index < coverageReports.Length; index++)
        {
            // Nested one level down for every report after the first, which is how a multi-assembly
            // run actually lays them out.
            var reportDirectory = index == 0
                ? coverageDirectory
                : Path.Combine(coverageDirectory, "assembly" + index.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(reportDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(reportDirectory, "coverage.cobertura.xml"),
                coverageReports[index],
                TestContext.Current.CancellationToken);
        }

        var managed = DotnetTestResultClassifier.Classify(
            exitCode, trxPath, coverageDirectory, new PhysicalFileSystemReader());
        var script = await RunScriptAsync(exitCode, trxPath, coverageDirectory);

        Assert.True(
            managed.IsValue == script.Accepted,
            $"'{caseName}': C# {(managed.IsValue ? "accepted" : "refused")} but the script "
            + $"{(script.Accepted ? "accepted" : "refused")} it. Script said: {script.Output}");

        if (managed.IsValue)
        {
            Assert.True(
                managed.Value == script.Status,
                $"'{caseName}': C# graded {managed.Value}, the script graded {script.Status}.");
            return;
        }

        // Agreeing to refuse is not enough. Two implementations that refuse the same case for
        // different reasons have already diverged: one of them is applying a rule the other does
        // not, and the next case is where that shows up as one grading a broken run clean.
        Assert.True(
            script.Output.Contains(Distinguishing(managed.Failure.Reason), StringComparison.Ordinal),
            $"'{caseName}': both refused, but for different reasons. C#: "
            + $"{managed.Failure.Reason} | script: {script.Output}");
    }

    /// <summary>
    /// The part of a refusal that identifies WHICH rule refused, stripped of the wording around it.
    /// The two implementations phrase their messages the same way on purpose, but only these
    /// fragments are the contract; matching whole sentences would fail on a comma.
    /// </summary>
    private static readonly string[] RefusalMarkers =
    [
        "technical test-host failure",
        "exit code and TRX counters are inconsistent",
        "execution counters are inconsistent",
        "is incomplete",
        "missing or invalid",
        "at least one Cobertura report",
        "no coverage root",
        "line counters are missing or invalid",
        "TRX evidence",
        "Cobertura evidence",
        "exit code is invalid",
    ];

    private static string Distinguishing(string reason)
    {
        foreach (var marker in RefusalMarkers)
            if (reason.Contains(marker, StringComparison.Ordinal)) return marker;
        throw new InvalidOperationException(
            $"The C# classifier refused with an unrecognised reason: '{reason}'. Add its marker to "
            + "RefusalMarkers so this conformance test can compare it against the script.");
    }

    private sealed record ScriptVerdict(bool Accepted, int Status, string Output);

    private static async Task<ScriptVerdict> RunScriptAsync(
        int exitCode, string trxPath, string coverageDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = RequireNode(),
            WorkingDirectory = Architecture.RepositoryScan.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(Path.Combine(
            Architecture.RepositoryScan.Root, "deploy", "scripts", "classify-dotnet-test-result.mjs"));
        startInfo.ArgumentList.Add(exitCode.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(trxPath);
        startInfo.ArgumentList.Add(coverageDirectory);

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        var output = await stdout;
        var error = await stderr;

        return process.ExitCode == 0
            ? new ScriptVerdict(true, int.Parse(output.Trim(), CultureInfo.InvariantCulture), output)
            : new ScriptVerdict(false, -1, error.Trim());
    }

    private static string Trx(
        int failed = 0, int error = 0, string outcome = "Completed", int executedDelta = 0)
    {
        var passed = failed + error > 0 ? 1 : 2;
        var total = passed + failed + error;
        return $"""<TestRun><ResultSummary outcome="{outcome}"><Counters total="{total}" executed="{total + executedDelta}" passed="{passed}" failed="{failed}" error="{error}" timeout="0" aborted="0" disconnected="0" /></ResultSummary></TestRun>""";
    }

    private static string TrxWithoutFailedCounter() =>
        """<TestRun><ResultSummary outcome="Completed"><Counters total="2" executed="2" passed="2" error="0" timeout="0" aborted="0" disconnected="0" /></ResultSummary></TestRun>""";

    private static string SafeName(string caseName) =>
        string.Concat(caseName.Select(character => char.IsLetterOrDigit(character) ? character : '-'));

    // node is what the pipelines themselves run to classify a suite, so a host without it cannot
    // exercise this conformance at all - fail instead of skipping.
    private static string RequireNode() => ExecutableLocator.Require(
        "node",
        OperatingSystem.IsWindows() ? @"C:\Program Files\nodejs\node.exe" : "/usr/local/bin/node");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
