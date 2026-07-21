// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class MailDomain
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string DkimSelector { get; set; } = "default";
    public bool HasSpf { get; set; }
    public bool HasDkim { get; set; }
    public bool HasDmarc { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public Server Server { get; set; } = null!;
    public List<MailAccount> Accounts { get; set; } = [];
    public List<MailAlias> Aliases { get; set; } = [];
}
