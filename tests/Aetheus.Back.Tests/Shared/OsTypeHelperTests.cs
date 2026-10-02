// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Tests;

public class OsTypeHelperTests
{
    [Theory]
    [InlineData("Microsoft Windows Server 2022", OsType.Windows)]
    [InlineData("Windows 11 Pro", OsType.Windows)]
    [InlineData("Ubuntu 22.04 LTS", OsType.Linux)]
    [InlineData("Debian GNU/Linux 12", OsType.Linux)]
    [InlineData("Linux 6.1.0", OsType.Linux)]
    [InlineData("Alpine Linux v3.19", OsType.Linux)]
    [InlineData("", OsType.Unknown)]
    [InlineData(null, OsType.Unknown)]
    [InlineData("FreeBSD 14", OsType.Unknown)]
    public void FromDescription_MapsFamily(string? description, OsType expected)
    {
        Assert.Equal(expected, OsTypeHelper.FromDescription(description));
    }

    [Theory]
    [InlineData("linux", OsType.Linux)]
    [InlineData("Linux", OsType.Linux)]
    [InlineData(" windows ", OsType.Windows)]
    [InlineData("win", OsType.Windows)]
    [InlineData("", OsType.Unknown)]
    [InlineData(null, OsType.Unknown)]
    [InlineData("macos", OsType.Unknown)]
    public void Parse_MapsYamlToken(string? token, OsType expected)
    {
        Assert.Equal(expected, OsTypeHelper.Parse(token));
    }

    [Theory]
    [InlineData("windoze", true)]
    [InlineData("macos", true)]
    [InlineData("linux", false)]
    [InlineData("windows", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsUnrecognized_FlagsTyposButNotEmptyOrValid(string? token, bool expected)
    {
        Assert.Equal(expected, OsTypeHelper.IsUnrecognized(token));
    }
}
