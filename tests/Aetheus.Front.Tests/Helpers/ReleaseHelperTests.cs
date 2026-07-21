// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Helpers;
using Aetheus.Shared.Enums;
using Radzen;

namespace Aetheus.Front.Tests;

public class ReleaseHelperTests
{
    [Theory]
    [InlineData(ReleaseStatus.Detected, BadgeStyle.Info)]
    [InlineData(ReleaseStatus.Building, BadgeStyle.Warning)]
    [InlineData(ReleaseStatus.Published, BadgeStyle.Success)]
    [InlineData(ReleaseStatus.Failed, BadgeStyle.Danger)]
    [InlineData(ReleaseStatus.RolledBack, BadgeStyle.Secondary)]
    [InlineData(ReleaseStatus.Promoted, BadgeStyle.Primary)]
    public void GetReleaseBadge_ReturnsCorrectStyle(ReleaseStatus status, BadgeStyle expected)
    {
        Assert.Equal(expected, ReleaseHelper.GetReleaseBadge(status));
    }

    [Fact]
    public void GetReleaseBadge_UnknownStatus_ReturnsLight()
    {
        Assert.Equal(BadgeStyle.Light, ReleaseHelper.GetReleaseBadge((ReleaseStatus)999));
    }
}
