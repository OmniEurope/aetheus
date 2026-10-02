// SPDX-License-Identifier: EUPL-1.2
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace Aetheus.E2E;

/// <summary>
/// Marks a test that asserts a behaviour the deployed baseline (V-1) does not have yet.
///
/// When that baseline is a payload-only fast release (rollback contract 4), it retains no test
/// runtime of its own, so the QA gate runs THIS suite against the V-1 images. A test of a feature
/// introduced after V-1 then fails there by construction: candidate 2444 was graded F on the R-355
/// "Top pages" tab, which V-1 simply does not have. The gate says so through
/// <c>E2E_PREVIOUS_PAYLOAD_ONLY=true</c>, and only then these tests are reported as skipped, with
/// the reason, instead of failing. Everywhere else (V, local runs, a V-1 that carries its own
/// suite) they run.
///
/// Remove the marker once a candidate carrying the feature has been deployed: from then on V-1 has
/// the feature and the test must run against it again.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class NewSinceDeployedBaselineAttribute(string recetteId) : NUnitAttribute, IApplyToTest
{
    internal static readonly bool TargetIsPayloadOnlyPrevious =
        bool.TryParse(Environment.GetEnvironmentVariable("E2E_PREVIOUS_PAYLOAD_ONLY"), out var value) && value;

    public string RecetteId { get; } = recetteId;

    public void ApplyToTest(Test test)
    {
        if (!TargetIsPayloadOnlyPrevious || test.RunState == RunState.NotRunnable) return;
        test.RunState = RunState.Ignored;
        test.Properties.Set(PropertyNames.SkipReason,
            $"{RecetteId} is newer than the deployed baseline, and this run targets that baseline's images.");
    }
}
