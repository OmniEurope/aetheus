// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class CertbotCertificate
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Domains { get; set; } = "[]"; // JSON array
    public DateTime ExpiryDate { get; set; }
    public string CertPath { get; set; } = string.Empty;
    public string KeyPath { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; }

    // Navigation
    public Server Server { get; set; } = null!;
}
