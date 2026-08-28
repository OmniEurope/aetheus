// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;

namespace Aetheus.Front.Tests;

/// <summary>Covers ApiClient partials: Totp, Certbot, Portsentry, Rkhunter, Mail, ModuleLinks, Services, SystemLogs.</summary>
public class ApiClientPartialTests
{
    private readonly BunitTestHelper.TestHandler _handler = new();
    private readonly ApiClient _api;

    public ApiClientPartialTests()
    {
        var http = new HttpClient(_handler) { BaseAddress = new Uri("http://test/") };
        _api = new ApiClient(http);
        // Blanket stub: any URL → 200 + {}
    }

    // --- Totp ---
    [Fact]
    public async Task SetupTotpAsync_ReturnsResponse()
    {
        _handler.SetJsonResponse("api/auth/totp/setup", new TotpSetupResponse { SharedKey = "ABC123" });
        var r = await _api.Auth.SetupTotpAsync(Xunit.TestContext.Current.CancellationToken);
        Assert.Equal("ABC123", r!.SharedKey);
    }

    [Fact]
    public async Task VerifyTotpAsync_ReturnsTrue()
    {
        _handler.SetJsonResponse("api/auth/totp/verify", "{}");
        var r = await _api.Auth.VerifyTotpAsync("123456", Xunit.TestContext.Current.CancellationToken); Assert.True(r);
    }

    [Fact]
    public async Task DisableTotpAsync_ReturnsTrue()
    {
        _handler.SetJsonResponse("api/auth/totp/disable", "{}");
        var r = await _api.Auth.DisableTotpAsync("password", Xunit.TestContext.Current.CancellationToken); Assert.True(r);
    }

    // --- Certbot ---
    [Fact]
    public async Task GetCertbotCertificatesAsync_ReturnsEmpty()
    {
        _handler.SetJsonResponse("api/servers/1/certbot", new List<CertbotCertificateDto>());
        var r = await _api.ServerTools.GetCertbotCertificatesAsync(1); Assert.Empty(r);
    }

    [Fact]
    public async Task ExecuteCertbotActionAsync_ReturnsStatus()
    {
        _handler.SetJsonResponse("api/servers/1/certbot/action", "{}");
        var r = await _api.ServerTools.ExecuteCertbotActionAsync(1, new CertbotActionRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success);
    }

    [Fact]
    public async Task CreateCertbotCertificateAsync_ReturnsStatus()
    {
        _handler.SetJsonResponse("api/servers/1/certbot/create", "{}");
        var r = await _api.ServerTools.CreateCertbotCertificateAsync(1, new CertbotCreateRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success);
    }

    // --- Portsentry ---
    [Fact]
    public async Task GetPortsentryStateAsync_ReturnsData()
    {
        _handler.SetJsonResponse("api/servers/1/portsentry", new PortsentryDataDto { IsInstalled = true, Mode = "tcp" });
        var r = await _api.Security.GetPortsentryStateAsync(1, Xunit.TestContext.Current.CancellationToken);
        Assert.True(r.IsInstalled); Assert.Equal("tcp", r.Mode);
    }

    [Fact]
    public async Task ExecutePortsentryActionAsync_ReturnsStatus()
    {
        _handler.SetJsonResponse("api/servers/1/portsentry/action", "{}");
        var r = await _api.Security.ExecutePortsentryActionAsync(1, new PortsentryActionRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success);
    }

    [Fact]
    public async Task SetupPortsentryAsync_ReturnsStatus()
    {
        _handler.SetJsonResponse("api/servers/1/portsentry/setup", "{}");
        var r = await _api.Security.SetupPortsentryAsync(1, new PortsentrySetupRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success);
    }

    [Fact]
    public async Task GetPortsentryLogsAsync_ReturnsStatus()
    {
        _handler.SetJsonResponse("api/servers/1/portsentry/logs", "{}");
        var r = await _api.Security.GetPortsentryLogsAsync(1, new PortsentryLogRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success);
    }

    [Fact]
    public async Task UnblockPortsentryIpAsync_ReturnsStatus()
    {
        _handler.SetJsonResponse("api/servers/1/portsentry/unblock", "{}");
        var r = await _api.Security.UnblockPortsentryIpAsync(1, new PortsentryUnblockRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success);
    }

    [Fact]
    public async Task GetPortsentryStatusAsync_ReturnsStatus()
    {
        _handler.SetJsonResponse("api/servers/1/portsentry/status", "{}");
        var r = await _api.Security.GetPortsentryStatusAsync(1, Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success);
    }

    [Fact]
    public async Task GetPortsentryWhitelistAsync_ReturnsEmpty()
    {
        _handler.SetJsonResponse("api/servers/1/portsentry/whitelist", new PaginatedResult<PortsentryWhitelistIpDto>());
        var r = await _api.Security.GetPortsentryWhitelistAsync(1); Assert.Empty(r);
    }

    [Fact]
    public async Task AddPortsentryWhitelistIpAsync_ReturnsDto()
    {
        _handler.SetJsonResponse("api/servers/1/portsentry/whitelist", new PortsentryWhitelistIpDto { Id = 1, IpAddress = "10.0.0.1" });
        var r = await _api.Security.AddPortsentryWhitelistIpAsync(1, new AddPortsentryWhitelistRequest(), Xunit.TestContext.Current.CancellationToken);
        Assert.Equal(1, r!.Id); Assert.Equal("10.0.0.1", r.IpAddress);
    }

    [Fact]
    public async Task RemovePortsentryWhitelistIpAsync_ReturnsStatus()
    {
        _handler.SetJsonResponse("api/servers/1/portsentry/whitelist/42", "{}");
        var r = await _api.Security.RemovePortsentryWhitelistIpAsync(1, 42, Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success);
    }

    // --- Rkhunter ---
    [Fact]
    public async Task GetRkhunterStateAsync_ReturnsData()
    {
        _handler.SetJsonResponse("api/servers/1/rkhunter", new RkhunterDataDto { IsInstalled = true, Version = "1.4.6" });
        var r = await _api.Security.GetRkhunterStateAsync(1, Xunit.TestContext.Current.CancellationToken);
        Assert.True(r.IsInstalled); Assert.Equal("1.4.6", r.Version);
    }

    [Fact]
    public async Task ExecuteRkhunterActionAsync_ReturnsStatus()
    {
        _handler.SetJsonResponse("api/servers/1/rkhunter/action", "{}");
        var r = await _api.Security.ExecuteRkhunterActionAsync(1, new RkhunterActionRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success);
    }

    [Fact]
    public async Task SetupRkhunterAsync_ReturnsStatus()
    {
        _handler.SetJsonResponse("api/servers/1/rkhunter/setup", "{}");
        var r = await _api.Security.SetupRkhunterAsync(1, new RkhunterSetupRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success);
    }

    [Fact]
    public async Task GetRkhunterLogsAsync_ReturnsStatus()
    {
        _handler.SetJsonResponse("api/servers/1/rkhunter/logs", "{}");
        var r = await _api.Security.GetRkhunterLogsAsync(1, new RkhunterLogRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success);
    }

    [Fact]
    public async Task GetRkhunterWarningsAsync_ReturnsEmpty()
    {
        _handler.SetJsonResponse("api/servers/1/rkhunter/warnings", new List<RkhunterWarningDto>());
        var r = await _api.Security.GetRkhunterWarningsAsync(1); Assert.Empty(r);
    }

    [Fact]
    public async Task GetRkhunterScanHistoryAsync_ReturnsEmpty()
    {
        _handler.SetJsonResponse("api/servers/1/rkhunter/history", new List<RkhunterScanResultDto>());
        var r = await _api.Security.GetRkhunterScanHistoryAsync(1); Assert.Empty(r);
    }

    [Fact]
    public async Task SetRkhunterScheduleAsync_ReturnsStatus()
    {
        _handler.SetJsonResponse("api/servers/1/rkhunter/schedule", "{}");
        var r = await _api.Security.SetRkhunterScheduleAsync(1, new RkhunterScheduleRequest(), Xunit.TestContext.Current.CancellationToken); Assert.True(r.Success);
    }
}
