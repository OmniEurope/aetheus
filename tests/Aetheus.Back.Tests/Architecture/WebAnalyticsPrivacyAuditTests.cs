// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json.Serialization;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Tests.Architecture;

public sealed class WebAnalyticsPrivacyAuditTests
{
    private static readonly Type[] PersistedAnalyticsTypes =
    [
        typeof(AppAnalyticsEvent),
        typeof(AppAnalyticsSession),
        typeof(AppAnalyticsPeriodIdentity),
        typeof(AppAnalyticsAggregate),
        typeof(AppAnalyticsPageAggregate),
        typeof(AppAnalyticsRejection)
    ];

    private static readonly string[] ForbiddenNames =
    [
        "IpAddress",
        "RemoteIp",
        "UserAgent",
        "QueryString",
        "Fragment",
        "RequestBody",
        "FormValue",
        "Secret",
        "Token",
        "Email"
    ];

    [Fact]
    public void AnalyticsPersistenceModel_ContainsNoRawBrowserOrSecretFields()
    {
        var forbidden = PersistedAnalyticsTypes
            .SelectMany(type => type.GetProperties().Select(property => $"{type.Name}.{property.Name}"))
            .Where(name => ForbiddenNames.Any(forbiddenName =>
                name.Contains(forbiddenName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.Empty(forbidden);
    }

    [Fact]
    public void PublicBrowserContract_IsClosedAndContainsOnlyAllowListedFields()
    {
        var names = typeof(PublicWebAnalyticsEventRequest)
            .GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            new[]
            {
                "DurationMs",
                "ErrorType",
                "EventId",
                "Kind",
                "OccurredAtUtc",
                "Route",
                "SchemaVersion"
            }
                .Order(StringComparer.Ordinal),
            names);
        var handling = typeof(PublicWebAnalyticsEventRequest)
            .GetCustomAttributes(typeof(JsonUnmappedMemberHandlingAttribute), inherit: false)
            .Cast<JsonUnmappedMemberHandlingAttribute>()
            .Single();
        Assert.Equal(JsonUnmappedMemberHandling.Disallow, handling.UnmappedMemberHandling);
    }

    [Fact]
    public void RawRequestSignalHandlers_DoNotWriteLogs()
    {
        var root = FindRepoRoot();
        var files = new[]
        {
            Path.Combine(root, "src", "Aetheus.Back", "Components", "AppMonitoring",
                "PublicWebAnalyticsController.cs"),
            Path.Combine(root, "src", "Aetheus.Back", "Components", "AppMonitoring",
                "PublicWebAnalyticsPseudonymizer.cs"),
            Path.Combine(root, "src", "Aetheus.WebAnalytics", "AnalyticsPrivacyPolicy.cs"),
            Path.Combine(root, "src", "Aetheus.WebAnalytics", "AnalyticsPseudonymizer.cs")
        };

        var offenders = files
            .Where(file => File.ReadAllText(file).Contains(".Log", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(root, file))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Classes handling raw IP/user-agent material must hash or discard it without logging:\n"
            + string.Join('\n', offenders));
    }

    private static string FindRepoRoot() => Aetheus.Back.Tests.Architecture.RepositoryScan.Root;
}
