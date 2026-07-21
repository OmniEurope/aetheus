// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Helpers;

namespace Aetheus.Front.Tests;

public class OsIconHelperTests
{
    [Theory]
    [InlineData(null, "dns")]
    [InlineData("", "dns")]
    [InlineData("   ", "dns")]
    [InlineData("Windows 11 Pro", "desktop_windows")]
    [InlineData("Microsoft Windows NT 10.0", "desktop_windows")]
    [InlineData("win32", "desktop_windows")]
    [InlineData("win64", "desktop_windows")]
    [InlineData("Linux 5.15.0-100-generic", "terminal")]
    [InlineData("Ubuntu 22.04", "terminal")]
    [InlineData("Debian GNU/Linux 12", "terminal")]
    [InlineData("CentOS Stream 9", "terminal")]
    [InlineData("Fedora Linux 39", "terminal")]
    [InlineData("Red Hat Enterprise Linux 9", "terminal")]
    [InlineData("Alpine Linux", "terminal")]
    [InlineData("Arch Linux", "terminal")]
    [InlineData("openSUSE Tumbleweed", "terminal")]
    [InlineData("Unix", "terminal")]
    [InlineData("Darwin 23.1.0", "laptop_mac")]
    [InlineData("Mac OS X 14.0", "laptop_mac")]
    [InlineData("macOS Sonoma", "laptop_mac")]
    [InlineData("osx-arm64", "laptop_mac")]
    [InlineData("FreeBSD 14", "dns")]
    public void GetIcon_ReturnsExpected(string? osDescription, string expected)
    {
        Assert.Equal(expected, OsIconHelper.GetIcon(osDescription));
    }

    [Theory]
    [InlineData(null, "server-os-unknown")]
    [InlineData("Windows 11", "server-os-windows")]
    [InlineData("Ubuntu 22.04", "server-os-linux")]
    [InlineData("Darwin 23.1.0", "server-os-macos")]
    [InlineData("FreeBSD 14", "server-os-unknown")]
    public void GetColorClass_ReturnsExpected(string? osDescription, string expected)
    {
        Assert.Equal(expected, OsIconHelper.GetColorClass(osDescription));
    }

    [Theory]
    [InlineData(null, "Unknown")]
    [InlineData("Windows 11", "Windows")]
    [InlineData("Ubuntu 22.04", "Linux")]
    [InlineData("Darwin 23.1.0", "macOS")]
    [InlineData("FreeBSD 14", "Unknown")]
    public void GetFamilyLabel_ReturnsExpected(string? osDescription, string expected)
    {
        Assert.Equal(expected, OsIconHelper.GetFamilyLabel(osDescription));
    }
}
