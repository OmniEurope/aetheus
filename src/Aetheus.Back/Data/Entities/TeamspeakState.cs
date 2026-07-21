// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class TeamspeakState
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public bool IsRunning { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public int VoicePort { get; set; }
    public int QueryPort { get; set; }
    public int MaxClients { get; set; }
    public int OnlineClients { get; set; }
    public int ChannelCount { get; set; }
    public long UptimeSeconds { get; set; }
    public string InstallPath { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; }

    // Navigation
    public Server Server { get; set; } = null!;
}
