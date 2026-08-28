// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Aetheus.WebAnalytics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Playwright.NUnit;

namespace Aetheus.E2E;

[TestFixture]
[NonParallelizable]
public sealed class WebAnalyticsConsumerBrowserTests : PageTest
{
    private const string AntiforgeryCookiePrefix = "." + "AspNetCore.Antiforgery.";

    private static readonly string[] Languages =
    [
        "en", "fr", "nl", "de", "es", "it", "pt", "pl", "cs", "sk", "sl", "hr",
        "hu", "ro", "bg", "el", "da", "sv", "nb", "fi", "et", "lv", "lt", "ga"
    ];

    private WebApplication _app = null!;
    private string _baseUrl = string.Empty;
    private X509Certificate2 _certificate = null!;

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true
    };

    [OneTimeSetUp]
    public async Task StartConsumerHost()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production
        });
        _certificate = CreateCertificate();
        builder.WebHost.ConfigureKestrel(options =>
            options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(_certificate)));
        builder.Services.AddAetheusWebAnalytics(
            new ConfigurationBuilder().Build(),
            options =>
            {
                options.Enabled = true;
                options.ApplicationId = 7001;
                options.SiteId = "browser-contract";
                options.IngestEndpoint = new Uri("https://aetheus.invalid/events");
                options.IngestKey = "browser-test-ingest-key";
                options.PseudonymizationKey = new string('0', 32);
                options.EnablePrivacyPage = true;
                options.ControllerName = "Browser Test Controller";
                options.Contact = "privacy@example.invalid";
                options.Purpose = "Anonymous audience statistics";
                options.LegalBasis = "Legitimate interest";
                options.HostingDescription = "Self-hosted in the European Union";
                options.PrivacyNoticeVersion = "2026-07-23";
            });

        _app = builder.Build();
        _app.UseRequestLocalization(options =>
        {
            options.SetDefaultCulture("en");
            options.AddSupportedCultures(Languages);
            options.AddSupportedUICultures(Languages);
        });
        _app.MapAetheusWebAnalytics();
        await _app.StartAsync();

        var addresses = _app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses;
        _baseUrl = addresses?.Single()
            ?? throw new InvalidOperationException("The consumer browser-test host has no address.");
    }

    [OneTimeTearDown]
    public async Task StopConsumerHost()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        _certificate.Dispose();
    }

    [Test]
    public async Task InformationPage_RendersAllTwentyFourLanguagesWithoutBanner()
    {
        foreach (var language in Languages)
        {
            await Context.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
            {
                ["Accept-Language"] = language
            });
            var response = await Page.GotoAsync(
                $"{_baseUrl}/privacy/audience-measurement?culture={language}&ui-culture={language}");

            Assert.That(response?.Ok, Is.True, language);
            await Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", language);
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToBeVisibleAsync();
            await Expect(Page.GetByRole(AriaRole.Button)).ToBeVisibleAsync();
            Assert.That(await Page.GetByRole(AriaRole.Dialog).CountAsync(), Is.Zero, language);
        }
    }

    [Test]
    public async Task Opposition_SetsOnlyEssentialRefusalCookieAndShowsConfirmation()
    {
        await Context.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
        {
            ["Accept-Language"] = "en"
        });
        await Page.GotoAsync($"{_baseUrl}/privacy/audience-measurement");

        await Page.GetByRole(AriaRole.Button).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Status)).ToBeVisibleAsync();
        var cookies = await Context.CookiesAsync(_baseUrl);
        var cookie = cookies.Single(item => item.Name == "aetheus_analytics_optout");
        var unexpectedCookies = cookies
            .Where(item => item.Name != "aetheus_analytics_optout"
                           && !item.Name.StartsWith(
                                AntiforgeryCookiePrefix,
                                StringComparison.Ordinal))
            .ToList();
        Assert.Multiple(() =>
        {
            Assert.That(cookie.Value, Is.EqualTo("1"));
            Assert.That(cookie.HttpOnly, Is.True);
            Assert.That(cookie.Secure, Is.True);
            Assert.That(cookie.SameSite, Is.EqualTo(SameSiteAttribute.Strict));
            Assert.That(unexpectedCookies, Is.Empty);
        });
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection
                {
                    new("1.3.6.1.5.5.7.3.1")
                },
                false));
        var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
        subjectAlternativeNames.AddDnsName("localhost");
        subjectAlternativeNames.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(subjectAlternativeNames.Build());
        using var generated = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(
            generated.Export(X509ContentType.Pfx),
            password: null);
    }
}
