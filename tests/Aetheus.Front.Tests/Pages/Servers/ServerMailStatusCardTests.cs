// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.Servers.ServerDetailSections;
using Aetheus.Front.Tests.TestDoubles;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages.Servers;

/// <summary>PLAN-005 lots 2 and 3: the status card tells an adopted stack from a provisioned one, warns about
/// outdated helpers, and requests a certificate for the reported hostname.</summary>
public sealed class ServerMailStatusCardTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;
    private readonly ImmediateDialogService _dialog;

    public ServerMailStatusCardTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        BunitTestHelper.UseImmediateDialogs(this);
        _dialog = (ImmediateDialogService)Services.GetRequiredService<OmniDialogService>();
    }

    private IRenderedComponent<ServerMailStatusCard> RenderCard(MailDataDto mail, bool canManage = true) =>
        Render<ServerMailStatusCard>(p => p.Add(x => x.Mail, mail).Add(x => x.ServerId, 10).Add(x => x.CanManage, canManage));

    private static MailDataDto AdoptedSelfSigned(int helperVersion = 1) => new()
    {
        IsInstalled = true,
        Hostname = "mail.example.com",
        HelperVersion = helperVersion,
        Tls = new MailTlsStateDto { CertPath = "/etc/ssl/certs/ssl-cert-snakeoil.pem", IsReadable = true, IsSelfSigned = true },
        Diagnostics = ["aliases-skipped|2"]
    };

    [Fact]
    public void AdoptedStack_ShowsOwnershipHelperWarningTlsAndFindings()
    {
        var cut = RenderCard(AdoptedSelfSigned());

        Assert.Contains("MailAdoptedStack", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("MailHelperOutdated", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("MailTlsSelfSigned", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("MailFindingAliasesSkipped", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentHelperAndLetsEncryptLineage_HideTheWarningAndTheCertificateActions()
    {
        var mail = AdoptedSelfSigned(helperVersion: 2) with
        {
            IsManagedByAetheus = true,
            Tls = new MailTlsStateDto
            {
                CertPath = "/etc/letsencrypt/live/mail.example.com/fullchain.pem",
                IsReadable = true,
                ExpiresAt = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        };

        var cut = RenderCard(mail);

        Assert.Contains("MailManagedByAetheus", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("MailHelperOutdated", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("MailObtainCertificate", StringComparison.Ordinal));
    }

    [Fact]
    public void ObtainingACertificate_PostsTheAcmeEmail()
    {
        _dialog.OpenResult = new MailDialogModel { Mode = MailDialogMode.RequestCertificate, Email = "ops@example.com" };
        _handler.SetJsonResponse(System.Net.Http.HttpMethod.Post, "api/servers/10/mail/certificate", new MailTaskQueuedDto { TaskId = 71 });
        var cut = RenderCard(AdoptedSelfSigned());

        cut.FindAll("button").First(b => b.TextContent.Contains("MailObtainCertificate", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() => Assert.Contains(_handler.RequestDetails, r =>
            r.Method == "POST" && r.Url.EndsWith("mail/certificate", StringComparison.Ordinal)
            && r.Body!.Contains("ops@example.com", StringComparison.Ordinal)));
    }

    [Fact]
    public void ReadOnlyUsers_GetNoActions()
    {
        var cut = RenderCard(AdoptedSelfSigned(), canManage: false);

        Assert.Empty(cut.FindAll("button"));
        Assert.DoesNotContain("MailHelperOutdated", cut.Markup, StringComparison.Ordinal);
    }
}
