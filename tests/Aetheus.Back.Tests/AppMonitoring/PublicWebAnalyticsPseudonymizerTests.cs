// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AppMonitoring;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Tests.AppMonitoring;

public sealed class PublicWebAnalyticsPseudonymizerTests
{
    private const string Secret = "unit-test-secret-never-sent-to-browser";
    private const string Address = "203.0.113.24";

    [Fact]
    public void Create_SeparatesApplicationsAndPeriods_WithoutRetainingRawSignals()
    {
        var source = Request(new DateTime(2026, 7, 23, 12, 0, 0, DateTimeKind.Utc));
        var first = PublicWebAnalyticsPseudonymizer.Create(App(1), Secret, source, Address);
        var secondApp = PublicWebAnalyticsPseudonymizer.Create(App(2), Secret, source, Address);
        var nextDay = PublicWebAnalyticsPseudonymizer.Create(
            App(1),
            Secret,
            Request(source.OccurredAtUtc.AddDays(1)),
            Address);

        Assert.NotEqual(first.DailyPseudonym, secondApp.DailyPseudonym);
        Assert.NotEqual(first.WeeklyPseudonym, secondApp.WeeklyPseudonym);
        Assert.NotEqual(first.MonthlyPseudonym, secondApp.MonthlyPseudonym);
        Assert.NotEqual(first.SessionPseudonym, secondApp.SessionPseudonym);
        Assert.NotEqual(first.DailyPseudonym, nextDay.DailyPseudonym);
        Assert.Equal(first.WeeklyPseudonym, nextDay.WeeklyPseudonym);
        Assert.Equal(first.MonthlyPseudonym, nextDay.MonthlyPseudonym);
        Assert.NotEqual(first.SessionPseudonym, nextDay.SessionPseudonym);

        var persistedValues = string.Join(
            '\n',
            first.Route,
            first.DailyPseudonym,
            first.WeeklyPseudonym,
            first.MonthlyPseudonym,
            first.SessionPseudonym);
        Assert.DoesNotContain(Address, persistedValues, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_NormalizesIdentifiersAndRejectsUrlDetails()
    {
        var normalized = PublicWebAnalyticsPseudonymizer.Create(
            App(1),
            Secret,
            Request(DateTime.UtcNow) with { Route = "/Orders/123/28a03884-9f25-48f6-8c78-3cc57a22f1f0" },
            Address);

        Assert.Equal("/orders/{id}/{id}", normalized.Route);
        Assert.Throws<InvalidOperationException>(() => PublicWebAnalyticsPseudonymizer.Create(
            App(1),
            Secret,
            Request(DateTime.UtcNow) with { Route = "/orders?token=secret" },
            Address));
    }

    private static MonitoredApp App(int id) => new()
    {
        Id = id,
        AnalyticsSiteId = $"site-{id}",
        AnalyticsPseudonymKeyVersion = 3
    };

    private static PublicWebAnalyticsEventRequest Request(DateTime occurredAtUtc) => new()
    {
        SchemaVersion = 1,
        EventId = Guid.NewGuid(),
        OccurredAtUtc = occurredAtUtc,
        Kind = "page_view",
        Route = "/"
    };
}
