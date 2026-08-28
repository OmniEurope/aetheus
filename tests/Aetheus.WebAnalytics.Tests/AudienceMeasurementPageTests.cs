// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aetheus.WebAnalytics.Tests;

public sealed class AudienceMeasurementPageTests
{
    [Fact]
    public async Task AspNetCoreHost_ExplicitlyEnablesAndRendersConfiguredNotice()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddAetheusWebAnalytics(
            new ConfigurationBuilder().Build(),
            options =>
            {
                options.Enabled = true;
                options.ApplicationId = 7;
                options.SiteId = "portfolio";
                options.IngestEndpoint = new Uri(
                    "https://aetheus.example/api/ingest/web-analytics/v1/events");
                options.IngestKey = "ingest";
                options.PseudonymizationKey = new string('0', 32);
                options.EnablePrivacyPage = true;
                options.ControllerName = "Example Controller";
                options.Contact = "privacy@example.invalid";
                options.Purpose = "Anonymous internal audience statistics";
                options.LegalBasis = "Legitimate interest";
                options.HostingDescription = "Self-hosted in the European Union";
                options.PrivacyNoticeVersion = "2026-07-23";
            });
        await using var app = builder.Build();
        app.MapAetheusWebAnalytics();
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(
            "/privacy/audience-measurement",
            TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        Assert.Contains(
            $"<html lang=\"{PrivacyTranslations.CurrentLanguage}\">",
            html,
            StringComparison.Ordinal);
        Assert.Contains("Example Controller", html, StringComparison.Ordinal);
        Assert.Contains("privacy@example.invalid", html, StringComparison.Ordinal);
        Assert.Contains("2026-07-23", html, StringComparison.Ordinal);
        Assert.Contains("<form method=\"post\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain("cookie banner", html, StringComparison.OrdinalIgnoreCase);
    }
}
