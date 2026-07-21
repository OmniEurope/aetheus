// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Helpers;
using Aetheus.Shared.Enums;
using Radzen;

namespace Aetheus.Front.Tests.Helpers;

public class EnvironmentHelperTests
{
    [Theory]
    [InlineData(EnvironmentType.Production, BadgeStyle.Danger)]
    [InlineData(EnvironmentType.Staging, BadgeStyle.Warning)]
    [InlineData(EnvironmentType.Testing, BadgeStyle.Info)]
    [InlineData(EnvironmentType.Development, BadgeStyle.Success)]
    public void TypeStyle_ReturnsExpected(EnvironmentType type, BadgeStyle expected)
    {
        Assert.Equal(expected, EnvironmentHelper.TypeStyle(type));
    }
}
