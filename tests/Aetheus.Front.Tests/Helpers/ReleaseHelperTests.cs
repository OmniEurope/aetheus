// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Tests;

public class ReleaseHelperTests
{
    [Theory]
    [InlineData(ReleaseStatus.Detected, OmniTone.Accent)]
    [InlineData(ReleaseStatus.Building, OmniTone.Warning)]
    [InlineData(ReleaseStatus.Published, OmniTone.Success)]
    [InlineData(ReleaseStatus.Failed, OmniTone.Danger)]
    [InlineData(ReleaseStatus.RolledBack, OmniTone.Neutral)]
    [InlineData(ReleaseStatus.Promoted, OmniTone.Accent)]
    [InlineData(ReleaseStatus.Superseded, OmniTone.Neutral)]
    public void GetReleaseBadge_ReturnsCorrectStyle(ReleaseStatus status, OmniTone expected)
    {
        Assert.Equal(expected, ReleaseHelper.GetReleaseBadge(status));
    }

    [Fact]
    public void GetReleaseBadge_UnknownStatus_ReturnsLight()
    {
        Assert.Equal(OmniTone.Neutral, ReleaseHelper.GetReleaseBadge((ReleaseStatus)999));
    }
}
