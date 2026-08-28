// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Tests.Architecture;

public sealed class PersistedEnumValueTests
{
    [Fact]
    public void ExecutorType_ValuesRemainStorageCompatible()
    {
        Assert.Equal(0, (int)ExecutorType.Shell);
        Assert.Equal(1, (int)ExecutorType.Docker);
        Assert.Equal(2, (int)ExecutorType.Operation);
        Assert.Equal(3, (int)ExecutorType.Container);
    }

    [Fact]
    public void ComparisonOperator_ValuesRemainStorageCompatible()
    {
        Assert.Equal(0, (int)ComparisonOperator.GreaterThan);
        Assert.Equal(1, (int)ComparisonOperator.LessThan);
        Assert.Equal(2, (int)ComparisonOperator.GreaterThanOrEqual);
        Assert.Equal(3, (int)ComparisonOperator.LessThanOrEqual);
    }
}
