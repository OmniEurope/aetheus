// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Environments;
using Aetheus.Shared.Enums;
using Radzen;

namespace Aetheus.Front.Tests.Pages;

public class EnvironmentEditMethodTests
{
    [Theory]
    [InlineData(EnvironmentType.Production, BadgeStyle.Danger)]
    [InlineData(EnvironmentType.Staging, BadgeStyle.Warning)]
    [InlineData(EnvironmentType.Testing, BadgeStyle.Info)]
    [InlineData(EnvironmentType.Development, BadgeStyle.Success)]
    public void TypeStyle_ReturnsExpected(EnvironmentType type, BadgeStyle expected)
    {
        var method = typeof(EnvironmentEdit).GetMethod("TypeStyle", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (BadgeStyle)method.Invoke(null, [type])!;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TypeStyle_UnknownType_ReturnsSuccess()
    {
        var method = typeof(EnvironmentEdit).GetMethod("TypeStyle", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (BadgeStyle)method.Invoke(null, [(EnvironmentType)999])!;
        Assert.Equal(BadgeStyle.Success, result);
    }
}
