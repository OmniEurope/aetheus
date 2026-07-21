// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Dashboard;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Dashboard;

public class DashboardNameDialogTests : BunitContext
{
    public DashboardNameDialogTests() => BunitTestHelper.RegisterServices(this);

    [Fact]
    public void Renders_WithInitialName_BindsValueIntoInput()
    {
        var cut = Render<DashboardNameDialog>(p => p.Add(c => c.InitialName, "My Dashboard"));

        Assert.Contains("My Dashboard", cut.Markup);
        Assert.NotEmpty(cut.FindAll("input"));
    }

    [Fact]
    public void Renders_EmptyName_StillRendersInput()
    {
        var cut = Render<DashboardNameDialog>();

        Assert.NotEmpty(cut.FindAll("input"));
    }
}
