// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class MailAccount
{
    public int Id { get; set; }
    public int MailDomainId { get; set; }
    public string Email { get; set; } = string.Empty;
    public int QuotaMb { get; set; } = 1024;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    // Navigation
    public MailDomain MailDomain { get; set; } = null!;
}
