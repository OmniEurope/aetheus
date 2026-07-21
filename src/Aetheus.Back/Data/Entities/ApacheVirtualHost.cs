// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class ApacheVirtualHost
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public int Port { get; set; }
    public string DocumentRoot { get; set; } = string.Empty;
    public string ConfigFile { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }

    // Navigation
    public Server Server { get; set; } = null!;
}
