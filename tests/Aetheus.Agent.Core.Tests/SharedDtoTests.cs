// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Agent.Core.Tests;

// Exercises shared DTO defaults/construction (PendingTaskDto, ServerHeartbeatDto) - these
// are not ExecutorResult tests despite the former file name.
public class SharedDtoTests
{
    [Fact]
    public void PendingTaskDto_DefaultValues_AreCorrect()
    {
        var dto = new PendingTaskDto();
        Assert.Equal(string.Empty, dto.Name);
        Assert.Equal(string.Empty, dto.Command);
        Assert.Equal(ExecutorType.Shell, dto.Executor);
        Assert.Empty(dto.EnvironmentVariables);
    }

    [Fact]
    public void HeartbeatDto_CanBeCreated()
    {
        var dto = new ServerHeartbeatDto
        {
            CpuPercent = 42.5,
            MemoryUsedMb = 2048,
            MemoryTotalMb = 8192,
            Disks = [new DiskInfoDto { MountPoint = "/", UsedGb = 50, TotalGb = 100 }],
            Services = []
        };
        Assert.Equal(42.5, dto.CpuPercent);
        Assert.Single(dto.Disks);
    }
}
