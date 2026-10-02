// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Front.Tests;

public class SharedDtoMailTeamspeakRkhunterTests
{
    // --- Mail DTOs ---

    [Fact]
    public void MailQueueItemDto_Defaults()
    {
        var dto = new MailQueueItemDto();
        Assert.Equal(string.Empty, dto.Id);
        Assert.Equal(string.Empty, dto.Sender);
        Assert.Equal(string.Empty, dto.Recipient);
        Assert.Equal(0, dto.SizeBytes);
        Assert.Equal(string.Empty, dto.Status);
    }

    [Fact]
    public void MailCertificateDto_Defaults()
    {
        var dto = new MailCertificateDto();
        Assert.Equal(string.Empty, dto.Hostname);
        Assert.Equal(string.Empty, dto.Issuer);
        Assert.Null(dto.ExpiresAt);
        Assert.False(dto.UsesLetsEncryptLineage);
    }

    [Fact]
    public void MailboxQuotaDto_Defaults()
    {
        var dto = new MailboxQuotaDto();
        Assert.Equal(0, dto.AccountId);
        Assert.Equal(string.Empty, dto.Email);
        Assert.Equal(0, dto.UsagePercent);
    }

    [Fact]
    public void MailboxQuotaDto_UsagePercent_Computed()
    {
        var dto = new MailboxQuotaDto { QuotaMb = 200, UsedMb = 50 };
        Assert.Equal(25.0, dto.UsagePercent);
    }

    [Fact]
    public void MailboxQuotaDto_UsagePercent_ZeroQuota()
    {
        var dto = new MailboxQuotaDto { QuotaMb = 0, UsedMb = 50 };
        Assert.Equal(0, dto.UsagePercent);
    }

    [Fact]
    public void SpamFilterConfigDto_Defaults()
    {
        var dto = new SpamFilterConfigDto();
        Assert.False(dto.IsInstalled);
        Assert.False(dto.IsRunning);
        Assert.Null(dto.RejectScore);
        Assert.Null(dto.AddHeaderScore);
        Assert.Null(dto.GreylistScore);
    }

    [Fact]
    public void UpdateSpamFilterRequest_Defaults()
    {
        var req = new UpdateSpamFilterRequest();
        Assert.Equal(15, req.RejectScore);
        Assert.Equal(6, req.AddHeaderScore);
        Assert.Equal(4, req.GreylistScore);
    }

    [Fact]
    public void MailAliasDto_Defaults()
    {
        var dto = new MailAliasDto();
        Assert.Equal(string.Empty, dto.SourceEmail);
        Assert.Equal(string.Empty, dto.DestinationEmail);
        Assert.True(dto.IsActive);
    }

    [Fact]
    public void UpdateMailAliasRequest_Defaults()
    {
        var req = new UpdateMailAliasRequest();
        Assert.Null(req.IsActive);
        Assert.Null(req.DestinationEmail);
    }

    [Fact]
    public void RequestMailCertificateRequest_Valid()
    {
        var req = new RequestMailCertificateRequest { Email = "a@example.com" };
        Assert.Empty(ValidateModel(req));
    }

    [Fact]
    public void MailDataDto_Defaults()
    {
        var dto = new MailDataDto();
        Assert.False(dto.IsInstalled);
        Assert.Empty(dto.Domains);
        Assert.Empty(dto.Accounts);
    }

    [Fact]
    public void MailDomainDto_Defaults()
    {
        var dto = new MailDomainDto();
        Assert.Equal(string.Empty, dto.Name);
        Assert.True(dto.IsActive);
        Assert.Equal("default", dto.DkimSelector);
    }

    [Fact]
    public void MailDnsRecordsDto_Defaults()
    {
        var dto = new MailDnsRecordsDto();
        Assert.Equal(string.Empty, dto.Domain);
        Assert.Equal(string.Empty, dto.SpfRecord);
        Assert.Equal(string.Empty, dto.MxRecord);
    }

    [Fact]
    public void DkimRotationResultDto_Defaults()
    {
        var dto = new DkimRotationResultDto();
        Assert.Equal(string.Empty, dto.Domain);
        Assert.Equal(string.Empty, dto.OldSelector);
    }

    // --- Teamspeak DTOs ---

    [Fact]
    public void TeamspeakClientDto_Defaults()
    {
        var dto = new TeamspeakClientDto();
        Assert.Equal(string.Empty, dto.UniqueId);
        Assert.Equal(string.Empty, dto.Nickname);
        Assert.Equal(0, dto.ClientId);
        Assert.False(dto.IsServerQuery);
    }

    [Fact]
    public void TeamspeakBanDto_Defaults()
    {
        var dto = new TeamspeakBanDto();
        Assert.Equal(string.Empty, dto.Ip);
        Assert.Equal(string.Empty, dto.Nickname);
        Assert.Equal(string.Empty, dto.Reason);
    }

    [Fact]
    public void TeamspeakDataDto_Defaults()
    {
        var dto = new TeamspeakDataDto();
        Assert.False(dto.IsInstalled);
        Assert.Empty(dto.Channels);
        Assert.Empty(dto.Clients);
    }

    [Fact]
    public void TeamspeakChannelDto_Defaults()
    {
        var dto = new TeamspeakChannelDto();
        Assert.Equal(string.Empty, dto.Name);
        Assert.Equal(-1, dto.MaxClients);
    }

    // --- Cron DTOs ---

    [Fact]
    public void CronJobDto_Defaults()
    {
        var dto = new CronJobDto();
        Assert.Equal(string.Empty, dto.Id);
        Assert.Equal(string.Empty, dto.User);
        Assert.Equal(string.Empty, dto.Schedule);
        Assert.Equal(string.Empty, dto.Command);
        Assert.Equal(string.Empty, dto.Source);
        Assert.False(dto.IsSystem);
    }

    [Fact]
    public void CronDataDto_Defaults()
    {
        var dto = new CronDataDto();
        Assert.False(dto.IsInstalled);
        Assert.Empty(dto.Jobs);
    }

    [Fact]
    public void CronJobSaveRequest_Valid()
    {
        var req = new CronJobSaveRequest { User = "root", Schedule = "* * * * *", Command = "echo" };
        Assert.Empty(ValidateModel(req));
    }

    [Fact]
    public void CronJobDeleteRequest_Valid()
    {
        var req = new CronJobDeleteRequest { Id = "c1", User = "root" };
        Assert.Empty(ValidateModel(req));
    }

    // --- Rkhunter DTOs ---

    [Fact]
    public void RkhunterScanResultDto_Defaults()
    {
        var dto = new RkhunterScanResultDto();
        Assert.Equal(string.Empty, dto.Status);
        Assert.Equal(string.Empty, dto.Summary);
        Assert.Equal(0, dto.WarningCount);
    }

    [Fact]
    public void RkhunterDataDto_Defaults()
    {
        var dto = new RkhunterDataDto();
        Assert.False(dto.IsInstalled);
        Assert.Equal(string.Empty, dto.Version);
        Assert.Null(dto.ScanScheduleCron);
    }

    [Fact]
    public void RkhunterWarningDto_Defaults()
    {
        var dto = new RkhunterWarningDto();
        Assert.Equal(string.Empty, dto.Category);
        Assert.Equal(string.Empty, dto.Detail);
        Assert.False(dto.IsArchived);
    }

    // --- ProjectServer DTOs ---

    [Fact]
    public void CreateProjectServerRequest_Valid()
    {
        var req = new CreateProjectServerRequest
        {
            Type = ProjectServerType.AgentServer,
            DisplayName = "Web",
            Host = "web.com"
        };
        Assert.Empty(ValidateModel(req));
    }

    [Fact]
    public void UpdateProjectServerRequest_Valid()
    {
        var req = new UpdateProjectServerRequest { DisplayName = "Updated", Host = "h.com" };
        Assert.Empty(ValidateModel(req));
    }

    [Fact]
    public void ProjectServerDto_Defaults()
    {
        var dto = new ProjectServerDto();
        Assert.Equal(string.Empty, dto.DisplayName);
        Assert.Equal(string.Empty, dto.Host);
        Assert.Null(dto.ServerId);
        Assert.Null(dto.ServerName);
    }

    // --- Docker ---

    [Fact]
    public void DockerEnvVarDto_Defaults()
    {
        var dto = new DockerEnvVarDto();
        Assert.Equal(string.Empty, dto.Key);
        Assert.Equal(string.Empty, dto.Value);
    }

    // --- Vault ---

    [Fact]
    public void RotateVaultSecretRequest_Defaults()
    {
        var req = new RotateVaultSecretRequest { Value = "secret" };
        Assert.Null(req.ExpiresAt);
    }

    // --- Git ---

    [Fact]
    public void PipelineStatusReport_Defaults()
    {
        var dto = new PipelineStatusReport();
        Assert.Equal(string.Empty, dto.State);
        Assert.Equal("aetheus-ci", dto.Context);
    }

    [Fact]
    public void GitLightCommitDto_Defaults()
    {
        var dto = new GitLightCommitDto();
        Assert.Equal(string.Empty, dto.Sha);
        Assert.Equal(string.Empty, dto.AuthorName);
        Assert.Empty(dto.ParentShas);
    }

    [Fact]
    public void GitLightTreeEntryDto_Defaults()
    {
        var dto = new GitLightTreeEntryDto();
        Assert.Equal(string.Empty, dto.Name);
        Assert.Null(dto.Size);
    }

    // --- Service DTOs ---

    [Fact]
    public void ServiceLogsRequest_Defaults()
    {
        var req = new ServiceLogsRequest { ServiceName = "svc" };
        Assert.Equal(100, req.Lines);
        Assert.False(req.Follow);
    }

    [Fact]
    public void ServiceActionRequest_Valid()
    {
        var req = new ServiceActionRequest { Action = ServiceAction.Restart, ServiceName = "nginx" };
        Assert.Empty(ValidateModel(req));
    }

    [Fact]
    public void ServiceInstallRequest_Valid()
    {
        var req = new ServiceInstallRequest { ServiceName = "nginx" };
        Assert.Empty(ValidateModel(req));
    }

    private static List<ValidationResult> ValidateModel(object model)
    {
        var ctx = new ValidationContext(model);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, ctx, results, true);
        return results;
    }
}
