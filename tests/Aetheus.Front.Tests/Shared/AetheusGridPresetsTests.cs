// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.Shared;
using Bunit;

namespace Aetheus.Front.Tests.Shared;

/// <summary>
/// PLAN-012: a raw OmniDataGrid that writes none of the shared settings gets them from the default
/// preset registered at startup, and a setting written on the grid still wins.
/// </summary>
public sealed class AetheusGridPresetsTests : BunitContext
{
    public AetheusGridPresetsTests() => BunitTestHelper.RegisterServices(this);

    [Fact]
    public void RawGrid_WithoutSettings_TakesTheSharedOnes()
    {
        var grid = Render<OmniDataGrid<string>>(parameters => parameters.Add(g => g.Items, ["a"])).Instance;

        Assert.True(grid.AllowSorting);
        Assert.True(grid.Filterable);
        Assert.Equal(OmniDataGridFilterMode.Simple, grid.FilterMode);
        Assert.True(grid.ShowHeaderFilterMenu);
        Assert.True(grid.AllowColumnResize);
        Assert.True(grid.AllowAlternatingRows);
        Assert.Equal(OmniDensity.Compact, grid.Density);
        Assert.Equal(OmniDataGridHeaderWrap.Truncate, grid.HeaderWrap); // R-538: titles on one line
    }

    [Fact]
    public void RawGrid_WrittenSetting_WinsOverThePreset()
    {
        var grid = Render<OmniDataGrid<string>>(parameters => parameters
            .Add(g => g.Items, ["a"])
            .Add(g => g.Density, OmniDensity.Comfortable)).Instance;

        Assert.Equal(OmniDensity.Comfortable, grid.Density);
        Assert.True(grid.AllowSorting);
    }
}
