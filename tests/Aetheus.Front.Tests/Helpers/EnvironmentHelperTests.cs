// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Tests.Helpers;

public class EnvironmentHelperTests
{
    [Theory]
    [InlineData(EnvironmentType.Production, OmniTone.Danger)]
    [InlineData(EnvironmentType.Staging, OmniTone.Warning)]
    [InlineData(EnvironmentType.Testing, OmniTone.Accent)]
    [InlineData(EnvironmentType.Development, OmniTone.Success)]
    public void TypeStyle_ReturnsExpected(EnvironmentType type, OmniTone expected)
    {
        Assert.Equal(expected, EnvironmentHelper.TypeStyle(type));
    }
}
