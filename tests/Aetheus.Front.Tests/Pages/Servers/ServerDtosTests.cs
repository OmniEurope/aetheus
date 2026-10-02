// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Tests.Pages.Servers;

/// <summary>
/// Instantiation tests for ServerDtos - exercises the init-property setters
/// on records that are otherwise uncovered.
/// </summary>
public class ServerDtosTests
{
    [Fact]
    public void ServerHeartbeatDto_AllProperties_Set()
    {
        var dto = new ServerHeartbeatDto
        {
            AgentVersion = "1.2.3",
            AgentInstalledAt = DateTime.UtcNow.AddDays(-7),
            CpuPercent = 45.5,
            MemoryUsedMb = 1024,
            MemoryTotalMb = 4096,
            Disks =
            [
                new DiskInfoDto { MountPoint = "/", UsedGb = 10, TotalGb = 50 }
            ],
            Services = [],
            Docker = new DockerDataDto(),
            Apache = new ApacheDataDto(),
            Certbot = new CertbotDataDto(),
            Cron = new CronDataDto(),
            Mail = new MailDataDto(),
            Teamspeak = new TeamspeakDataDto(),
            Portsentry = new PortsentryDataDto(),
            Rkhunter = new RkhunterDataDto(),
            SudoersHashes = new Dictionary<string, string> { ["aetheus-agent"] = "sha256:abc" }
        };
        Assert.Equal("1.2.3", dto.AgentVersion);
        Assert.Equal(45.5, dto.CpuPercent);
        Assert.Single(dto.Disks);
        Assert.Single(dto.SudoersHashes);
    }

    [Fact]
    public void DiskInfoDto_AllProperties_Set()
    {
        var dto = new DiskInfoDto
        {
            MountPoint = "/data",
            UsedGb = 200.5,
            TotalGb = 500.0
        };
        Assert.Equal("/data", dto.MountPoint);
        Assert.Equal(200.5, dto.UsedGb);
        Assert.Equal(500.0, dto.TotalGb);
    }

    [Fact]
    public void ServerRegistrationRequest_AllProperties_Set()
    {
        var req = new ServerRegistrationRequest
        {
            RegistrationToken = "tok-abc-123",
            Hostname = "web01.example.com",
            OsDescription = "Ubuntu 22.04",
            AgentVersion = "2.0.0",
            IpAddress = "192.168.1.100",
            AgentInstalledAt = DateTime.UtcNow.AddHours(-2)
        };
        Assert.Equal("tok-abc-123", req.RegistrationToken);
        Assert.Equal("web01.example.com", req.Hostname);
        Assert.Equal("Ubuntu 22.04", req.OsDescription);
        Assert.Equal("2.0.0", req.AgentVersion);
        Assert.Equal("192.168.1.100", req.IpAddress);
        Assert.NotNull(req.AgentInstalledAt);
    }

    [Fact]
    public void ServerRegistrationResponse_AllProperties_Set()
    {
        var resp = new ServerRegistrationResponse
        {
            ServerId = 42,
            BearerToken = string.Concat("eyJhbGciOi", "JIUzI1NiJ9", ".test")
        };
        Assert.Equal(42, resp.ServerId);
        Assert.Contains("eyJ", resp.BearerToken);
    }

    [Fact]
    public void ServerHeartbeatResponseDto_AllProperties_Set()
    {
        var dto = new ServerHeartbeatResponseDto
        {
            RenewedToken = "new-bearer-token",
            RenewedTokenExpiresAtUtc = DateTime.UtcNow.AddDays(30)
        };
        Assert.Equal("new-bearer-token", dto.RenewedToken);
        Assert.NotNull(dto.RenewedTokenExpiresAtUtc);
    }

    [Fact]
    public void ServerHeartbeatResponseDto_EmptyResponse_NullToken()
    {
        var dto = new ServerHeartbeatResponseDto();
        Assert.Null(dto.RenewedToken);
        Assert.Null(dto.RenewedTokenExpiresAtUtc);
    }

    [Fact]
    public void UpdateServerRequest_Properties_Accessible()
    {
        var req = new UpdateServerRequest
        {
            Name = "new-name",
            Tags = ["tag1", "tag2"],
            Status = Aetheus.Shared.Components.Servers.ServerStatus.Online,
            Type = Aetheus.Shared.Components.Servers.ServerType.Normal
        };
        Assert.Equal("new-name", req.Name);
        Assert.Equal(2, req.Tags!.Count);
        Assert.Equal(Aetheus.Shared.Components.Servers.ServerType.Normal, req.Type);
    }
}
