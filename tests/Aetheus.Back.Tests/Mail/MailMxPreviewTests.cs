// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Mail;
using Aetheus.Back.Components.Servers;
using Aetheus.Back.Exceptions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Aetheus.Back.Tests;

public sealed class MailMxPreviewTests
{
    private readonly IMailDnsResolver _dns = Substitute.For<IMailDnsResolver>();
    private readonly MailDiagnosticsService _sut;

    public MailMxPreviewTests()
    {
        _sut = new MailDiagnosticsService(
            Substitute.For<IMailInventoryRepository>(),
            Substitute.For<IMailRepository>(),
            Substitute.For<IServerRepository>(),
            _dns,
            Substitute.For<IMailDnsVerifier>(),
            TimeProvider.System);
    }

    [Fact]
    public async Task Preview_ReportsAllObservedMxHostsBeforeSetup()
    {
        _dns.MxAsync("sonytumen.com", Arg.Any<CancellationToken>())
            .Returns(["mx1.ovh.net", "mail.sonytumen.com"]);

        var result = await _sut.PreviewMxAsync("sonytumen.com", TestContext.Current.CancellationToken);

        Assert.Equal(MailCheckVerdict.Ok, result.Verdict);
        Assert.Equal(["mx1.ovh.net", "mail.sonytumen.com"], result.Hosts);
    }

    [Fact]
    public async Task ResolverFailure_RemainsUnknownInsteadOfMissing()
    {
        _dns.MxAsync("sonytumen.com", Arg.Any<CancellationToken>())
            .ThrowsAsync(new MailDnsLookupException("SERVFAIL"));

        var result = await _sut.PreviewMxAsync("sonytumen.com", TestContext.Current.CancellationToken);

        Assert.Equal(MailCheckVerdict.Unknown, result.Verdict);
        Assert.Empty(result.Hosts);
    }

    [Fact]
    public async Task InvalidDomain_DoesNotReachPublicDns()
    {
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.PreviewMxAsync("mail domain!", TestContext.Current.CancellationToken));

        Assert.Empty(_dns.ReceivedCalls());
    }

    [Fact]
    public async Task MoreMxHostsThanCanBeShown_DoesNotClaimTheRouteWasChecked()
    {
        var hosts = Enumerable.Range(1, 20).Select(index => $"mx{index}.example.net")
            .Append("mx1.ovh.net").ToArray();
        _dns.MxAsync("sonytumen.com", Arg.Any<CancellationToken>()).Returns(hosts);

        var result = await _sut.PreviewMxAsync("sonytumen.com", TestContext.Current.CancellationToken);

        Assert.Equal(MailCheckVerdict.Unknown, result.Verdict);
        Assert.Empty(result.Hosts);
    }
}
