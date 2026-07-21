// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class MailState
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string PostfixVersion { get; set; } = string.Empty;
    public string DovecotVersion { get; set; } = string.Empty;
    public bool IsPostfixRunning { get; set; }
    public bool IsDovecotRunning { get; set; }
    public int QueueSize { get; set; }
    public DateTime LastUpdated { get; set; }

    // Navigation
    public Server Server { get; set; } = null!;
}
