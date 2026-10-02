// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Tests;

public class SharedDtoTests
{
    [Fact]
    public void DashboardDto_DefaultValues_AreCorrect()
    {
        var dto = new DashboardOverviewDto();
        Assert.Equal(0, dto.TotalServers);
        Assert.Equal(0, dto.OnlineServers);
        Assert.Equal(0, dto.PendingTasks);
        Assert.Empty(dto.Servers);
        Assert.Empty(dto.RecentRuns);
    }
}
