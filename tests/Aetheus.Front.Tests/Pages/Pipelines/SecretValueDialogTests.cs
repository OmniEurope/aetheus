// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages;
using Aetheus.Shared.DTOs;
using Bunit;
using Radzen;

namespace Aetheus.Front.Tests.Pages;

public class SecretValueDialogTests : BunitContext
{
    public SecretValueDialogTests()
    {
        BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void Renders_PasswordField()
    {
        var cut = Render<SecretValueDialog>(p => p.Add(x => x.SecretKey, "DB_PASSWORD"));
        Assert.Contains("NewValue", cut.Markup);
    }

    [Fact]
    public void SaveButton_Disabled_WhenEmpty()
    {
        var cut = Render<SecretValueDialog>(p => p.Add(x => x.SecretKey, "API_KEY"));
        var buttons = cut.FindAll("button");
        var saveBtn = buttons.FirstOrDefault(b => b.TextContent.Contains("Save"));
        Assert.NotNull(saveBtn);
        Assert.True(saveBtn.HasAttribute("disabled"));
    }

    [Fact]
    public void Renders_CancelButton()
    {
        var cut = Render<SecretValueDialog>(p => p.Add(x => x.SecretKey, "KEY"));
        var buttons = cut.FindAll("button");
        var cancelBtn = buttons.FirstOrDefault(b => b.TextContent.Contains("Cancel"));
        Assert.NotNull(cancelBtn);
    }

    [Fact]
    public void Renders_WithSecretKey()
    {
        var cut = Render<SecretValueDialog>(p => p.Add(x => x.SecretKey, "MY_SECRET"));
        // The dialog renders the masked password input and its label.
        Assert.Contains("NewValue", cut.Markup);
        Assert.NotNull(cut.Find("#secret-value-input"));
    }

    [Fact]
    public void SecretInput_IsPasswordType_AndValueNeverRenderedAsPlaintext()
    {
        const string secret = "sup3r-s3cret-value";
        var cut = Render<SecretValueDialog>(p => p.Add(x => x.SecretKey, "MY_SECRET"));

        var input = cut.Find("#secret-value-input");
        // The value field must be a password input so the secret is masked on screen.
        Assert.Equal("password", input.GetAttribute("type"));

        // Type a secret, then confirm it never leaks into any visible text node (label, span, ...).
        // A password input masks its own value attribute in the browser, so the only safe surface is
        // the masked input itself - the secret must not appear as rendered text anywhere.
        input.Input(secret);
        foreach (var node in cut.FindAll("*"))
            Assert.DoesNotContain(secret, node.TextContent);
    }
}
