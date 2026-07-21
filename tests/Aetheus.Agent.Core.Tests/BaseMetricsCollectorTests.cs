// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Collectors;
using Aetheus.Shared.DTOs;

namespace Aetheus.Agent.Core.Tests;

public class BaseMetricsCollectorTests
{
    private sealed class TestMetricsCollector : BaseMetricsCollector
    {
        public double CpuPercent { get; set; }
        public double MemoryUsedMb { get; set; }
        public double MemoryTotalMb { get; set; }

        protected override double GetCpuPercent() => CpuPercent;
        protected override double GetMemoryUsedMb() => MemoryUsedMb;
        protected override double GetMemoryTotalMb() => MemoryTotalMb;
    }

    [Fact]
    public void Collect_ReturnsCorrectCpuAndMemory()
    {
        var collector = new TestMetricsCollector
        {
            CpuPercent = 55.5,
            MemoryUsedMb = 4096,
            MemoryTotalMb = 16384
        };

        var result = collector.Collect();

        Assert.Equal(55.5, result.CpuPercent);
        Assert.Equal(4096, result.MemoryUsedMb);
        Assert.Equal(16384, result.MemoryTotalMb);
    }

    [Fact]
    public void Collect_ReturnsEmptyServicesList()
    {
        var collector = new TestMetricsCollector();

        var result = collector.Collect();

        Assert.Empty(result.Services);
    }

    [Fact]
    public void Collect_ReturnsDisks()
    {
        var collector = new TestMetricsCollector();

        var result = collector.Collect();

        Assert.NotEmpty(result.Disks);
        Assert.All(result.Disks, disk =>
        {
            Assert.False(string.IsNullOrWhiteSpace(disk.MountPoint));
            Assert.True(disk.TotalGb > 0);
            Assert.InRange(disk.UsedGb, 0, disk.TotalGb);
        });
    }
}
