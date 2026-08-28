// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Collectors;

public abstract class BaseMetricsCollector : IMetricsCollector
{
    public ServerHeartbeatDto Collect()
    {
        return new ServerHeartbeatDto
        {
            CpuPercent = GetCpuPercent(),
            MemoryUsedMb = GetMemoryUsedMb(),
            MemoryTotalMb = GetMemoryTotalMb(),
            Disks = GetDisks(),
            Services = []
        };
    }

    protected abstract double GetCpuPercent();
    protected abstract double GetMemoryUsedMb();
    protected abstract double GetMemoryTotalMb();

    protected List<DiskInfoDto> GetDisks()
    {
        var disks = new List<DiskInfoDto>();
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (IOException) { return disks; }
        catch (UnauthorizedAccessException) { return disks; }

        foreach (var drive in drives)
        {
            try
            {
                if (!drive.IsReady || !ShouldIncludeDrive(drive))
                    continue;

                var totalGb = Math.Round(drive.TotalSize / 1073741824.0, 1);
                if (totalGb <= 0)
                    continue;

                disks.Add(new DiskInfoDto
                {
                    MountPoint = drive.Name,
                    UsedGb = Math.Round((drive.TotalSize - drive.AvailableFreeSpace) / 1073741824.0, 1),
                    TotalGb = totalGb
                });
            }
            catch (IOException) { /* drive disconnected/locked - skip */ }
            catch (UnauthorizedAccessException) { /* permission denied - skip */ }
        }
        return disks;
    }

    protected virtual bool ShouldIncludeDrive(DriveInfo drive) => true;
}
