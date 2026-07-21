// SPDX-License-Identifier: EUPL-1.2
using System.Runtime.Versioning;
using System.ServiceProcess;
using Aetheus.Agent.Core.Collectors;
using Aetheus.Shared.DTOs;

namespace Aetheus.Agent.Windows.Collectors;

[SupportedOSPlatform("windows")]
public sealed class WindowsServiceCollector(ILogger<WindowsServiceCollector> logger) : IServiceCollector
{
    public Task<List<ServiceInfoDto>> CollectAsync(CancellationToken ct = default)
    {
        var services = new List<ServiceInfoDto>();
        try
        {
            foreach (var sc in ServiceController.GetServices())
            {
                try
                {
                    services.Add(new ServiceInfoDto
                    {
                        Name = sc.ServiceName,
                        Type = Aetheus.Shared.Enums.ServiceType.WindowsService,
                        Status = sc.Status.ToString(),
                        IsRunning = sc.Status == ServiceControllerStatus.Running,
                        IsManageable = sc.StartType != ServiceStartMode.Disabled
                    });
                }
                finally
                {
                    sc.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to enumerate Windows services");
        }
        return Task.FromResult(services);
    }
}
