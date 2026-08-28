// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;

namespace Aetheus.Back.Components.Analysis;

internal sealed class AnalysisPolicyDecisionSnapshotBuilder
{
    private const int MaxSnapshotLength = 100_000;
    private const int MaxCompactedEntries = 100;

    private readonly Dictionary<string, string> _policies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime?> _exceptions = new(StringComparer.Ordinal);
    private readonly Dictionary<DecisionGroupKey, int> _aggregates = [];
    private int _evaluatedFindingCount;
    private int _evaluatedMetricCount;

    public void AddFinding(
        string source,
        string? key,
        string outcome,
        DateTime? expiresAt,
        string? policySnapshotJson,
        string? observedValue = null,
        string? threshold = null,
        string? policyOperator = null)
    {
        _evaluatedFindingCount++;
        Add(source, "finding", key, outcome, expiresAt, policySnapshotJson,
            observedValue, threshold, policyOperator);
    }

    public void AddMetric(
        string source,
        string key,
        string outcome,
        DateTime? expiresAt,
        string? policySnapshotJson,
        string? observedValue = null,
        string? threshold = null,
        string? policyOperator = null)
    {
        _evaluatedMetricCount++;
        Add(source, "metric", key, outcome, expiresAt, policySnapshotJson,
            observedValue, threshold, policyOperator);
    }

    private void Add(
        string source,
        string targetKind,
        string? targetKey,
        string outcome,
        DateTime? expiresAt,
        string? policySnapshotJson,
        string? observedValue,
        string? threshold,
        string? policyOperator)
    {
        string? policySnapshotHash = null;
        if (policySnapshotJson is not null)
        {
            policySnapshotHash = AnalysisPolicySnapshot.Hash(policySnapshotJson);
            _policies.TryAdd(source, policySnapshotHash);
        }
        if (source.StartsWith("exception:", StringComparison.Ordinal))
            _exceptions.TryAdd(source, expiresAt);

        var aggregateKey = new DecisionGroupKey(
            source,
            targetKind,
            targetKey,
            outcome,
            expiresAt,
            policySnapshotHash,
            observedValue,
            threshold,
            policyOperator);
        _aggregates[aggregateKey] = _aggregates.GetValueOrDefault(aggregateKey) + 1;
    }

    public string Build()
    {
        var policies = _policies
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new PolicyEvidence(item.Key, item.Value))
            .ToList();
        var exceptions = _exceptions
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new ExceptionEvidence(item.Key, item.Value))
            .ToList();
        var aggregates = _aggregates
            .Select(item => new DecisionAggregate(
                item.Key.Source,
                item.Key.TargetKind,
                item.Key.TargetKey,
                item.Key.Outcome,
                item.Key.ExpiresAt,
                item.Key.PolicySnapshotHash,
                item.Key.ObservedValue,
                item.Key.Threshold,
                item.Key.Operator,
                item.Value))
            .OrderBy(item => IsNonActionable(item.Outcome))
            .ThenBy(item => item.Source, StringComparer.Ordinal)
            .ThenBy(item => item.TargetKind, StringComparer.Ordinal)
            .ThenBy(item => item.Outcome, StringComparer.Ordinal)
            .ThenBy(item => item.TargetKey, StringComparer.Ordinal)
            .ToList();

        var policyEvidenceJson = JsonSerializer.Serialize(policies);
        var exceptionEvidenceJson = JsonSerializer.Serialize(exceptions);
        var aggregateEvidenceJson = JsonSerializer.Serialize(aggregates);
        var fullSnapshot = JsonSerializer.Serialize(new
        {
            SchemaVersion = 3,
            Compacted = false,
            EvaluatedFindings = _evaluatedFindingCount,
            EvaluatedMetrics = _evaluatedMetricCount,
            Policies = policies,
            Exceptions = exceptions,
            Aggregates = aggregates,
            PolicyEvidenceHash = AnalysisPolicySnapshot.Hash(policyEvidenceJson),
            ExceptionEvidenceHash = AnalysisPolicySnapshot.Hash(exceptionEvidenceJson),
            AggregateEvidenceHash = AnalysisPolicySnapshot.Hash(aggregateEvidenceJson),
            OmittedPolicyCount = 0,
            OmittedExceptionCount = 0,
            OmittedAggregateCount = 0
        });
        if (fullSnapshot.Length <= MaxSnapshotLength)
            return fullSnapshot;

        var compactPolicies = policies
            .Take(MaxCompactedEntries)
            .ToList();
        var compactExceptions = exceptions.Take(MaxCompactedEntries).ToList();
        var compactAggregates = aggregates.Take(MaxCompactedEntries).ToList();
        var compactSnapshot = JsonSerializer.Serialize(new
        {
            SchemaVersion = 3,
            Compacted = true,
            EvaluatedFindings = _evaluatedFindingCount,
            EvaluatedMetrics = _evaluatedMetricCount,
            PolicyCount = policies.Count,
            ExceptionCount = exceptions.Count,
            AggregateCount = aggregates.Count,
            Policies = compactPolicies,
            Exceptions = compactExceptions,
            Aggregates = compactAggregates,
            PolicyEvidenceHash = AnalysisPolicySnapshot.Hash(policyEvidenceJson),
            ExceptionEvidenceHash = AnalysisPolicySnapshot.Hash(exceptionEvidenceJson),
            AggregateEvidenceHash = AnalysisPolicySnapshot.Hash(aggregateEvidenceJson),
            OmittedPolicyCount = policies.Count - compactPolicies.Count,
            OmittedExceptionCount = exceptions.Count - compactExceptions.Count,
            OmittedAggregateCount = aggregates.Count - compactAggregates.Count
        });
        if (compactSnapshot.Length <= MaxSnapshotLength)
            return compactSnapshot;

        return JsonSerializer.Serialize(new
        {
            SchemaVersion = 3,
            Compacted = true,
            EvaluatedFindings = _evaluatedFindingCount,
            EvaluatedMetrics = _evaluatedMetricCount,
            PolicyCount = policies.Count,
            ExceptionCount = exceptions.Count,
            AggregateCount = aggregates.Count,
            PolicyEvidenceHash = AnalysisPolicySnapshot.Hash(policyEvidenceJson),
            ExceptionEvidenceHash = AnalysisPolicySnapshot.Hash(exceptionEvidenceJson),
            AggregateEvidenceHash = AnalysisPolicySnapshot.Hash(aggregateEvidenceJson),
            OmittedPolicyCount = policies.Count,
            OmittedExceptionCount = exceptions.Count,
            OmittedAggregateCount = aggregates.Count
        });
    }

    private static bool IsNonActionable(string outcome) =>
        string.Equals(outcome, "pass", StringComparison.Ordinal)
        || string.Equals(outcome, "excepted", StringComparison.Ordinal);

    private sealed record PolicyEvidence(string Source, string SnapshotHash);
    private sealed record ExceptionEvidence(string Source, DateTime? ExpiresAt);
    private sealed record DecisionAggregate(
        string Source,
        string TargetKind,
        string? TargetKey,
        string Outcome,
        DateTime? ExpiresAt,
        string? PolicySnapshotHash,
        string? ObservedValue,
        string? Threshold,
        string? Operator,
        int Count);
    private sealed record DecisionGroupKey(
        string Source,
        string TargetKind,
        string? TargetKey,
        string Outcome,
        DateTime? ExpiresAt,
        string? PolicySnapshotHash,
        string? ObservedValue,
        string? Threshold,
        string? Operator);
}
