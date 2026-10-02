// SPDX-License-Identifier: EUPL-1.2
using Bunit;

namespace Aetheus.Front.Tests.Pages;

public class ImportJsonDialogTests : BunitContext
{
    public ImportJsonDialogTests()
    {
        BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void Renders_TextArea()
    {
        var cut = Render<ImportJsonDialog>();
        Assert.Contains("PasteJsonBelow", cut.Markup);
    }

    [Fact]
    public void Renders_ImportButton_Disabled_WhenEmpty()
    {
        var cut = Render<ImportJsonDialog>();
        var buttons = cut.FindAll("button");
        var importBtn = buttons.FirstOrDefault(b => b.TextContent.Contains("Import"));
        Assert.NotNull(importBtn);
        Assert.True(importBtn.HasAttribute("disabled"));
    }

    [Fact]
    public void Renders_CancelButton()
    {
        var cut = Render<ImportJsonDialog>();
        var buttons = cut.FindAll("button");
        var cancelBtn = buttons.FirstOrDefault(b => b.TextContent.Contains("GoBack"));
        Assert.NotNull(cancelBtn);
    }

    [Fact]
    public void ImportButton_EnabledAfterInput()
    {
        var cut = Render<ImportJsonDialog>();
        var textarea = cut.Find("textarea");
        textarea.Input("{\"key\": \"value\"}");

        var buttons = cut.FindAll("button");
        var importBtn = buttons.FirstOrDefault(b => b.TextContent.Contains("Import"));
        Assert.NotNull(importBtn);
        // After non-empty input the Import button must no longer be disabled.
        Assert.False(importBtn.HasAttribute("disabled"));
    }
}
