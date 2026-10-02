// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Tests;

public class ServerTypeHelperTests
{
    [Theory]
    [InlineData(ServerType.Normal, "dns")]
    [InlineData(ServerType.Build, "build")]
    [InlineData(ServerType.Docker, "inventory_2")]
    public void GetIcon_ReturnsExpectedIcon(ServerType type, string expectedIcon)
    {
        Assert.Equal(expectedIcon, ServerTypeHelper.GetIcon(type));
    }

    [Fact]
    public void GetIcon_UnknownType_ReturnsDns()
    {
        Assert.Equal("dns", ServerTypeHelper.GetIcon((ServerType)999));
    }

    [Theory]
    [InlineData(ServerType.Normal, OmniTone.Accent)]
    [InlineData(ServerType.Build, OmniTone.Warning)]
    [InlineData(ServerType.Docker, OmniTone.Neutral)]
    public void GetBadgeStyle_ReturnsExpectedStyle(ServerType type, OmniTone expected)
    {
        Assert.Equal(expected, ServerTypeHelper.GetBadgeStyle(type));
    }

    [Fact]
    public void GetBadgeStyle_UnknownType_ReturnsLight()
    {
        Assert.Equal(OmniTone.Neutral, ServerTypeHelper.GetBadgeStyle((ServerType)999));
    }
}
