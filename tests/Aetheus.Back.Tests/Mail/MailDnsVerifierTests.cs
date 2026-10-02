// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Mail;
using Aetheus.Back.Data.Entities;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Aetheus.Back.Tests;

/// <summary>PLAN-005 lot 5: verdicts per record and the persisted SPF / DKIM / DMARC flags.</summary>
public sealed class MailDnsVerifierTests
{
    private readonly IMailDnsResolver _dns = Substitute.For<IMailDnsResolver>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero));
    private readonly MailDnsVerifier _sut;

    public MailDnsVerifierTests() => _sut = new MailDnsVerifier(_dns, _clock);

    private static MailDomain Domain() => new()
    {
        Id = 3,
        Name = "example.com",
        DkimSelector = "s1",
        DkimPublicKey = "v=DKIM1; h=sha256; k=rsa; p=MIIBIjAN"
    };

    private void Publish(string name, params string[] txt) =>
        _dns.TxtAsync(name, Arg.Any<CancellationToken>()).Returns(txt);

    [Fact]
    public async Task FullyPublishedDomain_IsOkEverywhere()
    {
        _dns.MxAsync("example.com", Arg.Any<CancellationToken>()).Returns(["mail.example.com"]);
        Publish("example.com", "google-site-verification=abc", "v=spf1 mx a ~all");
        Publish("s1._domainkey.example.com", "v=DKIM1; k=rsa; p=MIIB IjAN");
        Publish("_dmarc.example.com", "v=DMARC1; p=quarantine");
        var domain = Domain();

        var result = await _sut.VerifyAsync(domain, "mail.example.com", TestContext.Current.CancellationToken);

        Assert.All(result.Records, r => Assert.Equal(MailCheckVerdict.Ok, r.Verdict));
        Assert.True(domain.HasSpf && domain.HasDkim && domain.HasDmarc);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, domain.DnsCheckedAt);
    }

    [Fact]
    public async Task MissingDmarc_OnlyDmarcIsMissing()
    {
        _dns.MxAsync("example.com", Arg.Any<CancellationToken>()).Returns(["mail.example.com"]);
        Publish("example.com", "v=spf1 mx ~all");
        Publish("s1._domainkey.example.com", "v=DKIM1; k=rsa; p=MIIBIjAN");
        Publish("_dmarc.example.com");
        var domain = Domain();

        var result = await _sut.VerifyAsync(domain, "mail.example.com", TestContext.Current.CancellationToken);

        Assert.Equal(MailCheckVerdict.Missing, result.Records.Single(r => r.Kind == MailDnsRecordKind.Dmarc).Verdict);
        Assert.True(domain.HasSpf);
        Assert.True(domain.HasDkim);
        Assert.False(domain.HasDmarc);
    }

    [Fact]
    public async Task RotatedKeyNotYetPublished_IsAKeyMismatch()
    {
        _dns.MxAsync("example.com", Arg.Any<CancellationToken>()).Returns(["mail.example.com"]);
        Publish("example.com", "v=spf1 mx ~all");
        Publish("s1._domainkey.example.com", "v=DKIM1; k=rsa; p=OLDKEY");
        Publish("_dmarc.example.com", "v=DMARC1; p=none");
        var domain = Domain();

        var result = await _sut.VerifyAsync(domain, "mail.example.com", TestContext.Current.CancellationToken);

        var dkim = result.Records.Single(r => r.Kind == MailDnsRecordKind.Dkim);
        Assert.Equal((MailCheckVerdict.Mismatch, "key-mismatch"), (dkim.Verdict, dkim.Detail));
        Assert.False(domain.HasDkim);
    }

    [Theory]
    [InlineData(new[] { "v=spf1 +all" }, MailCheckVerdict.Mismatch, "permissive-all")]
    [InlineData(new[] { "v=spf1 mx ~all", "v=spf1 a ~all" }, MailCheckVerdict.Mismatch, "multiple-records")]
    [InlineData(new[] { "v=spf1 -all" }, MailCheckVerdict.Mismatch, "no-sender-mechanism")]
    [InlineData(new[] { "v=spf1 include:_spf.example.net ~all" }, MailCheckVerdict.Ok, "")]
    [InlineData(new[] { "unrelated" }, MailCheckVerdict.Missing, "")]
    public void SpfEvaluation(string[] txt, MailCheckVerdict verdict, string detail)
    {
        var (actual, actualDetail, _) = MailDnsVerifier.EvaluateSpf(txt);
        Assert.Equal((verdict, detail), (actual, actualDetail));
    }

    [Fact]
    public async Task ResolverFailure_IsUnknownNotMissing()
    {
        _dns.MxAsync("example.com", Arg.Any<CancellationToken>()).ThrowsAsync(new MailDnsLookupException("SERVFAIL"));
        _dns.TxtAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new MailDnsLookupException("SERVFAIL"));
        var domain = Domain();
        domain.HasSpf = true;

        var result = await _sut.VerifyAsync(domain, "mail.example.com", TestContext.Current.CancellationToken);

        Assert.All(result.Records, r => Assert.Equal((MailCheckVerdict.Unknown, "resolver-error"), (r.Verdict, r.Detail)));
        Assert.False(domain.HasSpf);
    }

    [Fact]
    public async Task MxPointingElsewhere_IsAMismatch()
    {
        _dns.MxAsync("example.com", Arg.Any<CancellationToken>()).Returns(["mx.provider.net"]);
        Publish("example.com");
        Publish("s1._domainkey.example.com");
        Publish("_dmarc.example.com");

        var result = await _sut.VerifyAsync(Domain(), "mail.example.com", TestContext.Current.CancellationToken);

        var mx = result.Records.Single(r => r.Kind == MailDnsRecordKind.Mx);
        Assert.Equal((MailCheckVerdict.Mismatch, "wrong-target"), (mx.Verdict, mx.Detail));
        Assert.Equal(["mx.provider.net"], mx.Observed);
    }
}
