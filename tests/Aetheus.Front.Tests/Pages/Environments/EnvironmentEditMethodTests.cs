// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Components.Environments;

namespace Aetheus.Front.Tests.Pages;

public class EnvironmentEditMethodTests
{
    [Theory]
    [InlineData(EnvironmentType.Production, OmniTone.Danger)]
    [InlineData(EnvironmentType.Staging, OmniTone.Warning)]
    [InlineData(EnvironmentType.Testing, OmniTone.Accent)]
    [InlineData(EnvironmentType.Development, OmniTone.Success)]
    public void TypeStyle_ReturnsExpected(EnvironmentType type, OmniTone expected)
    {
        var method = typeof(EnvironmentEdit).GetMethod("TypeStyle", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (OmniTone)method.Invoke(null, [type])!;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TypeStyle_UnknownType_ReturnsSuccess()
    {
        var method = typeof(EnvironmentEdit).GetMethod("TypeStyle", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (OmniTone)method.Invoke(null, [(EnvironmentType)999])!;
        Assert.Equal(OmniTone.Success, result);
    }
}
