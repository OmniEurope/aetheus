// SPDX-License-Identifier: EUPL-1.2
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Analysis;

internal static class AnalysisPolicySnapshot
{
    public static string Serialize(AnalysisPolicy policy) => Serialize(
        policy,
        policy.ProjectId.HasValue
            ? AnalysisPolicyScope.Project
            : policy.OrganizationId.HasValue
                ? AnalysisPolicyScope.Organization
                : AnalysisPolicyScope.Global,
        false,
        false,
        null);

    public static string Serialize(ResolvedAnalysisPolicy policy) => Serialize(
        policy.Policy,
        policy.Scope,
        policy.IsInherited,
        policy.IsOverride,
        policy.OverriddenScope);

    private static string Serialize(
        AnalysisPolicy policy,
        AnalysisPolicyScope scope,
        bool isInherited,
        bool isOverride,
        AnalysisPolicyScope? overriddenScope) => JsonSerializer.Serialize(new
        {
            policy.Id,
            policy.Version,
            policy.OrganizationId,
            policy.ProjectId,
            PolicyKey = AnalysisPolicyKey.ForExisting(policy),
            policy.Name,
            policy.Category,
            policy.ScannerKey,
            policy.RuleId,
            policy.SeverityThreshold,
            policy.NewFindingsOnly,
            policy.MetricKey,
            policy.Operator,
            policy.Threshold,
            policy.BranchPattern,
            policy.EnvironmentPattern,
            policy.Behavior,
            policy.Priority,
            policy.Enabled,
            Scope = scope,
            IsInherited = isInherited,
            IsOverride = isOverride,
            OverriddenScope = overriddenScope
        });

    public static string Hash(string snapshot) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot)));

    public static UpsertAnalysisPolicyRequest DeserializeRequest(string snapshot)
    {
        var value = JsonSerializer.Deserialize<PolicySnapshot>(snapshot)
            ?? throw new InvalidDataException("The policy revision snapshot is empty.");
        return new UpsertAnalysisPolicyRequest
        {
            PolicyKey = value.PolicyKey,
            Name = value.Name,
            Category = value.Category,
            ScannerKey = value.ScannerKey,
            RuleId = value.RuleId,
            SeverityThreshold = value.SeverityThreshold,
            NewFindingsOnly = value.NewFindingsOnly,
            MetricKey = value.MetricKey,
            Operator = value.Operator,
            Threshold = value.Threshold,
            BranchPattern = value.BranchPattern,
            EnvironmentPattern = value.EnvironmentPattern,
            Behavior = value.Behavior,
            Priority = value.Priority,
            Enabled = value.Enabled
        };
    }

    private sealed record PolicySnapshot
    {
        public string? PolicyKey { get; init; }
        public string Name { get; init; } = string.Empty;
        public AnalysisCategory? Category { get; init; }
        public string? ScannerKey { get; init; }
        public string? RuleId { get; init; }
        public AnalysisSeverity? SeverityThreshold { get; init; }
        public bool NewFindingsOnly { get; init; }
        public string? MetricKey { get; init; }
        public AnalysisPolicyOperator? Operator { get; init; }
        public double? Threshold { get; init; }
        public string? BranchPattern { get; init; }
        public string? EnvironmentPattern { get; init; }
        public AnalysisGateBehavior Behavior { get; init; }
        public int Priority { get; init; }
        public bool Enabled { get; init; }
    }
}
