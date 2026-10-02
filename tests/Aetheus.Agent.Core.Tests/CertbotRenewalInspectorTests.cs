// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Collectors;

namespace Aetheus.Agent.Core.Tests;

/// <summary>
/// PLAN-007: the convention verdict per lineage, from real certbot renewal file shapes. The two
/// production failures of 2026-09 are the NotWebroot (Apache plugin) and WrongWebroot (old path) cases.
/// </summary>
public sealed class CertbotRenewalInspectorTests
{
    private const string Webroot = "/var/www/aetheus-acme";

    private const string CliIni = """
        # operator line kept outside the block
        email = ops@example.com
        # BEGIN aetheus-managed
        authenticator = webroot
        webroot-path = /var/www/aetheus-acme
        # END aetheus-managed
        """;

    private const string ConformingRenewal = """
        version = 2.9.0
        archive_dir = /etc/letsencrypt/archive/app.example.com
        [renewalparams]
        account = 0123456789abcdef
        authenticator = webroot
        webroot_path = /var/www/aetheus-acme,
        server = https://acme-v02.api.letsencrypt.org/directory
        [[webroot_map]]
        app.example.com = /var/www/aetheus-acme
        www.example.com = /var/www/aetheus-acme
        """;

    private const string OldWebrootRenewal = """
        [renewalparams]
        authenticator = webroot
        webroot_path = /var/lib/aetheus-agent/acme-webroot,
        [[webroot_map]]
        docs.example.com = /var/lib/aetheus-agent/acme-webroot
        """;

    private const string ApachePluginRenewal = """
        [renewalparams]
        authenticator = apache
        installer = apache
        """;

    private const string StandaloneRenewal = """
        [renewalparams]
        authenticator = standalone
        """;

    [Theory]
    [InlineData(ConformingRenewal, true, CertbotRenewalConvention.Conforming)]
    [InlineData(OldWebrootRenewal, true, CertbotRenewalConvention.WrongWebroot)]
    [InlineData(ApachePluginRenewal, true, CertbotRenewalConvention.NotWebroot)]
    [InlineData(StandaloneRenewal, true, CertbotRenewalConvention.NotWebroot)]
    [InlineData(StandaloneRenewal, false, CertbotRenewalConvention.Conforming)]
    [InlineData(ApachePluginRenewal, false, CertbotRenewalConvention.NotWebroot)]
    public void Annotate_ComparesEachLineageWithTheCliIniWebroot(
        string renewal, bool aliasEnabled, CertbotRenewalConvention expected)
    {
        var files = new Dictionary<string, string>
        {
            ["/etc/letsencrypt/cli.ini"] = CliIni,
            ["/etc/letsencrypt/renewal/site.conf"] = renewal
        };
        var sut = new CertbotRenewalInspector(files.GetValueOrDefault, _ => aliasEnabled);

        var certificate = Assert.Single(sut.Annotate([new CertbotCertificateDto { Name = "site" }]));

        Assert.Equal(expected, certificate.RenewalConvention);
    }

    [Fact]
    public void Annotate_ReportsAuthenticatorAndEveryWebrootTheFileNames()
    {
        var files = new Dictionary<string, string>
        {
            ["/etc/letsencrypt/cli.ini"] = CliIni,
            ["/etc/letsencrypt/renewal/docs.conf"] = """
                [renewalparams]
                authenticator = webroot
                webroot_path = /var/www/aetheus-acme,
                [[webroot_map]]
                docs.example.com = /var/lib/aetheus-agent/acme-webroot
                """
        };
        var sut = new CertbotRenewalInspector(files.GetValueOrDefault, _ => true);

        var certificate = Assert.Single(sut.Annotate([new CertbotCertificateDto { Name = "docs" }]));

        Assert.Equal("webroot", certificate.Authenticator);
        Assert.Equal("/var/lib/aetheus-agent/acme-webroot, /var/www/aetheus-acme", certificate.WebrootPath);
        // One stale domain in the map is enough to break that domain's renewal.
        Assert.Equal(CertbotRenewalConvention.WrongWebroot, certificate.RenewalConvention);
    }

    [Fact]
    public void Annotate_WithoutConventionOrReadableRenewalFile_IsUnknownNeverConforming()
    {
        var noCliIni = new CertbotRenewalInspector(
            new Dictionary<string, string> { ["/etc/letsencrypt/renewal/a.conf"] = ConformingRenewal }.GetValueOrDefault,
            _ => true);
        var noRenewal = new CertbotRenewalInspector(
            new Dictionary<string, string> { ["/etc/letsencrypt/cli.ini"] = CliIni }.GetValueOrDefault,
            _ => true);

        Assert.Equal(CertbotRenewalConvention.Unknown,
            Assert.Single(noCliIni.Annotate([new CertbotCertificateDto { Name = "a" }])).RenewalConvention);
        Assert.Equal(CertbotRenewalConvention.Unknown,
            Assert.Single(noRenewal.Annotate([new CertbotCertificateDto { Name = "a" }])).RenewalConvention);
    }

    [Fact]
    public void ParseCliIniWebroot_KeepsTheLastValueLikeCertbot()
    {
        Assert.Equal(Webroot, CertbotRenewalInspector.ParseCliIniWebroot(CliIni));
        Assert.Equal("/srv/other", CertbotRenewalInspector.ParseCliIniWebroot(CliIni + "\nwebroot-path = /srv/other\n"));
        Assert.Null(CertbotRenewalInspector.ParseCliIniWebroot("# webroot-path = /commented\n"));
    }

    [Theory]
    [InlineData("checked_at=1789733288\nexit_code=0\n", true)]
    [InlineData("checked_at=1789733288\nexit_code=1\n", false)]
    public void ReadRenewalCheck_ReportsTheRecordedOutcome(string content, bool succeeded)
    {
        var sut = new CertbotRenewalInspector(
            new Dictionary<string, string> { ["/var/lib/aetheus-certbot/renewal-check"] = content }.GetValueOrDefault,
            _ => false);

        var (checkedAt, result) = sut.ReadRenewalCheck();

        Assert.Equal(new DateTime(2026, 9, 18, 12, 8, 8, DateTimeKind.Utc), checkedAt);
        Assert.Equal(succeeded, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("checked_at=1789733288\n")]
    [InlineData("checked_at=soon\nexit_code=0\n")]
    public void ReadRenewalCheck_MissingOrPartialRecord_IsAbsent(string? content)
    {
        var files = new Dictionary<string, string>();
        if (content is not null) files["/var/lib/aetheus-certbot/renewal-check"] = content;
        var sut = new CertbotRenewalInspector(files.GetValueOrDefault, _ => false);

        Assert.Equal((null, null), sut.ReadRenewalCheck());
    }
}
