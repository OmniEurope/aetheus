// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// A360-35. <c>src/Aetheus.Back/Services/</c> sits outside <c>Components/</c>, so neither
/// <c>NetArchTests</c> nor <c>DependencyCycleAuditTests</c> ever looked at it - both scan
/// <c>Components/</c> only. That blind spot let a generic timeout sweeper accumulate knowledge that
/// belongs to the Pipelines module: which deployment operations are safe to replay after an outage.
///
/// This guard does not attempt to layer <c>Services/</c> (those are hosted services, not a layered
/// graph). It enforces the one boundary the audit actually found broken: a background service may
/// ORCHESTRATE a module, but it must not restate that module's business rules, because the copy in the
/// neighbour is the one nobody updates.
/// </summary>
public sealed class BackgroundServiceBoundaryAuditTests
{
    /// <summary>
    /// Domain vocabulary that only its owning module should reason about. A background service naming
    /// these is deciding something the module should decide.
    /// </summary>
    private static readonly string[] DeploymentOperationTokens =
    [
        "OperationKind.BlueGreenMigrate",
        "OperationKind.BlueGreenUp",
        "OperationKind.BlueGreenSwitch",
        "OperationKind.BlueGreenCommit",
        "OperationKind.BlueGreenRollback",
        "OperationKind.PipelineDeploy",
        "OperationKind.PipelineCreateRelease"
    ];

    [Fact]
    public void NoBackgroundService_RestatesTheDeploymentReplayPolicy()
    {
        var servicesDirectory = Path.Combine(RepositoryScan.Root, "src", "Aetheus.Back", "Services");
        var violations = new List<string>();

        foreach (var file in RepositoryScan.Enumerate(servicesDirectory, "*.cs"))
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                var trimmed = line.TrimStart();
                // A comment may legitimately explain the rule; only code may not restate it.
                if (trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith("///", StringComparison.Ordinal)) continue;

                foreach (var token in DeploymentOperationTokens)
                    if (line.Contains(token, StringComparison.Ordinal))
                        violations.Add($"{Path.GetFileName(file)}:{index + 1} names {token}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "A background service under Services/ is restating the Pipelines module's deployment "
            + "policy. Ask the module instead (PipelineOutageReplayPolicy), so a new deployment "
            + "operation does not have to be remembered in two places:"
            + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", violations));
    }

    [Fact]
    public void TheReplayPolicy_StillRefusesEveryDeploymentOperation()
    {
        // Moving the rule must not have softened it: the point of the list is that a half-applied
        // deployment is never replayed blind after an outage.
        Assert.False(Aetheus.Back.Components.Pipelines.PipelineOutageReplayPolicy
            .MayReplayAfterAnOutage(Aetheus.Shared.Components.Tasks.OperationKind.BlueGreenSwitch));
        Assert.False(Aetheus.Back.Components.Pipelines.PipelineOutageReplayPolicy
            .MayReplayAfterAnOutage(Aetheus.Shared.Components.Tasks.OperationKind.PipelineDeploy));
        // And ordinary work is still replayable, or the stand-by feature would be pointless.
        Assert.True(Aetheus.Back.Components.Pipelines.PipelineOutageReplayPolicy
            .MayReplayAfterAnOutage(Aetheus.Shared.Components.Tasks.OperationKind.None));
    }
}
