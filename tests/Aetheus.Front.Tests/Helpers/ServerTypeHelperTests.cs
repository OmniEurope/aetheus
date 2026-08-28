// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Helpers;
using Aetheus.Shared.Enums;
using Radzen;

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
    [InlineData(ServerType.Normal, BadgeStyle.Info)]
    [InlineData(ServerType.Build, BadgeStyle.Warning)]
    [InlineData(ServerType.Docker, BadgeStyle.Light)]
    public void GetBadgeStyle_ReturnsExpectedStyle(ServerType type, BadgeStyle expected)
    {
        Assert.Equal(expected, ServerTypeHelper.GetBadgeStyle(type));
    }

    [Fact]
    public void GetBadgeStyle_UnknownType_ReturnsLight()
    {
        Assert.Equal(BadgeStyle.Light, ServerTypeHelper.GetBadgeStyle((ServerType)999));
    }
}
