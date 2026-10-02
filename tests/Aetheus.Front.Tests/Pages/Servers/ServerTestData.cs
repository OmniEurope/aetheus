// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Tests.Pages.Servers;

/// <summary>
/// Shared <see cref="ServerDetailDto"/> factories for the Servers test group.
/// Centralizes data builders that were duplicated verbatim across multiple
/// per-section test files (audit 360 lot H - test debt).
/// </summary>
internal static class ServerTestData
{
    public static ServerDetailDto MakeMailServer(
        bool installed = true,
        bool postfixRunning = true,
        bool dovecotRunning = true) => new()
        {
            Id = 10,
            Name = "mail-srv",
            Hostname = "10.0.0.1",
            Type = ServerType.Normal,
            Status = ServerStatus.Online,
            CpuPercent = 25,
            MemoryUsedMb = 4096,
            MemoryTotalMb = 8192,
            DiskUsedGb = 40,
            DiskTotalGb = 100,
            Tags = ["mail"],
            Services = [],
            Mail = new MailDataDto
            {
                IsInstalled = installed,
                IsPostfixRunning = postfixRunning,
                IsDovecotRunning = dovecotRunning,
                PostfixVersion = installed ? "3.5.6" : string.Empty,
                DovecotVersion = installed ? "2.3.19" : string.Empty,
                QueueSize = 3,
                Domains =
            [
                new MailDomainDto { Id = 1, Name = "example.com", IsActive = true, DkimSelector = "default", HasSpf = true, HasDkim = true, HasDmarc = false },
                new MailDomainDto { Id = 2, Name = "test.org", IsActive = false, DkimSelector = "mail", HasSpf = false, HasDkim = false, HasDmarc = false }
            ],
                Accounts =
            [
                new MailAccountDto { Id = 1, Email = "admin@example.com", IsActive = true, QuotaMb = 1024, Domain = "example.com" },
                new MailAccountDto { Id = 2, Email = "user@test.org", IsActive = true, QuotaMb = 512, Domain = "test.org" }
            ]
            }
        };
}
