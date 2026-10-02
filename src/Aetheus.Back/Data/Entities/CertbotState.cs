// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

/// <summary>
/// Server-level Certbot state (PLAN-007). Present while the agent reports certbot installed; carries the
/// outcome of the last renewal rehearsal so a failure is visible and notified before a certificate expires.
/// </summary>
public class CertbotState
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public DateTime? RenewalCheckedAt { get; set; }
    public bool? RenewalCheckSucceeded { get; set; }

    // Navigation
    public Server Server { get; set; } = null!;
}
