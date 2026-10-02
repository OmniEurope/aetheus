// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.Servers.ServerDetailSections;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages.Servers;

/// <summary>PLAN-007: the Certbot section shows how each certificate renews and the last renewal
/// rehearsal, and offers Normalize and Test renewal.</summary>
public sealed class ServerCertbotConventionTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ServerCertbotConventionTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        BunitTestHelper.UseImmediateDialogs(this);
        _handler.SetJsonResponse("api/servers/10/certbot/action", true);
    }

    private Aetheus.Front.Tests.TestDoubles.ImmediateDialogService Confirmation =>
        (Aetheus.Front.Tests.TestDoubles.ImmediateDialogService)Services.GetRequiredService<OmniDialogService>();

    [Fact]
    public void Certificates_ShowTheirConventionVerdict()
    {
        var cut = Render(new CertbotDataDto
        {
            IsInstalled = true,
            Certificates =
            [
                Certificate("ok.example.com", CertbotRenewalConvention.Conforming, "webroot", "/var/www/aetheus-acme"),
                Certificate("random.example.com", CertbotRenewalConvention.NotWebroot, "apache", string.Empty),
                Certificate("docs.example.com", CertbotRenewalConvention.WrongWebroot, "webroot", "/var/lib/aetheus-agent/acme-webroot"),
                Certificate("legacy.example.com", CertbotRenewalConvention.Unknown, string.Empty, string.Empty)
            ]
        });

        var badges = cut.FindAll("[data-testid=certbot-convention]");
        Assert.Equal(
            ["CertbotConventionConforming", "CertbotConventionNotWebroot", "CertbotConventionWrongWebroot", "CertbotConventionUnknown"],
            badges.Select(badge => badge.TextContent.Trim()));
        Assert.Equal("webroot: /var/lib/aetheus-agent/acme-webroot", badges[2].GetAttribute("title"));
    }

    [Theory]
    [InlineData(null, "CertbotRenewalCheckNever")]
    [InlineData(true, "CertbotRenewalCheckPassed")]
    [InlineData(false, "CertbotRenewalCheckFailed")]
    public void RenewalCheck_ShowsTheLastOutcome(bool? succeeded, string expected)
    {
        var cut = Render(new CertbotDataDto
        {
            IsInstalled = true,
            RenewalCheckedAt = succeeded is null ? null : new DateTime(2026, 9, 18, 12, 8, 8, DateTimeKind.Utc),
            RenewalCheckSucceeded = succeeded
        });

        Assert.Equal(expected, cut.Find("[data-testid=certbot-renewal-check]").TextContent.Trim());
    }

    [Fact]
    public void TestRenewal_PostsTheRenewalCheckAction()
    {
        var cut = Render(new CertbotDataDto { IsInstalled = true });

        Button(cut, "CertbotRenewalCheck").Click();

        cut.WaitForAssertion(() => Assert.Contains(_handler.RequestDetails, request =>
            request.Method == "POST" && request.Url.Contains("api/servers/10/certbot/action")
            && request.Body!.Contains($"\"action\":{(int)CertbotAction.RenewalCheck}", StringComparison.Ordinal)));
    }

    [Fact]
    public void Normalize_AsksForConfirmationThenPostsTheNormalizeAction()
    {
        var cut = Render(new CertbotDataDto { IsInstalled = true });

        // Declined first: nothing is sent, and the question named its verb on an ordinary (blue) button.
        Button(cut, "CertbotNormalize").Click();
        Assert.DoesNotContain(_handler.RequestDetails, request => request.Url.Contains("certbot/action"));
        Assert.Equal("ConfirmCertbotNormalize", Confirmation.LastConfirmMessage);
        Assert.Equal("CertbotNormalize", Confirmation.LastConfirmOptions!.OkButtonText);
        Assert.False(Confirmation.LastConfirmOptions.Destructive);

        Confirmation.ConfirmResult = true;
        Button(cut, "CertbotNormalize").Click();

        cut.WaitForAssertion(() => Assert.Contains(_handler.RequestDetails, request =>
            request.Method == "POST" && request.Url.Contains("api/servers/10/certbot/action")
            && request.Body!.Contains($"\"action\":{(int)CertbotAction.Normalize}", StringComparison.Ordinal)));
    }

    [Fact]
    public void NotInstalled_HidesTheRenewalCheckAndDisablesTheConventionActions()
    {
        var cut = Render(new CertbotDataDto { IsInstalled = false });

        Assert.Empty(cut.FindAll("[data-testid=certbot-renewal-check]"));
        Assert.True(Button(cut, "CertbotNormalize").HasAttribute("disabled"));
        Assert.True(Button(cut, "CertbotRenewalCheck").HasAttribute("disabled"));
    }

    private IRenderedComponent<ServerCertbotSection> Render(CertbotDataDto certbot) =>
        Render<ServerCertbotSection>(parameters => parameters
            .Add(section => section.Server, new ServerDetailDto
            {
                Id = 10,
                Name = "cert-srv",
                Hostname = "10.0.0.10",
                Type = ServerType.Normal,
                Status = ServerStatus.Online,
                Tags = [],
                Services = [],
                Certbot = certbot
            })
            .Add(section => section.ServerId, 10));

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<ServerCertbotSection> cut, string text) =>
        cut.FindAll("button").Single(button =>
            button.TextContent.Trim() == text);

    private static CertbotCertificateDto Certificate(
        string name, CertbotRenewalConvention convention, string authenticator, string webroot) => new()
        {
            Name = name,
            Domains = [name],
            ExpiryDate = DateTime.UtcNow.AddDays(60),
            Authenticator = authenticator,
            WebrootPath = webroot,
            RenewalConvention = convention
        };
}
