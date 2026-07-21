// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Playwright;

namespace Aetheus.E2E;

[Category("E2E")]
[Category("Security")]
public class TotpTests : E2ETestBase
{
    [SetUp]
    public async Task SetUp() => await LoginAsync();

    [Test]
    public async Task Settings_SecurityTab_ShowsTotpSetup()
    {
        await Page.GotoAsync($"{FrontendUrl}/settings");
        await Page.WaitForSelectorAsync(".rz-tabview", new() { Timeout = 10000 });
        var securityTab = Page.Locator("text=Security").First;
        await Expect(securityTab).ToBeVisibleAsync();
        await securityTab.ClickAsync();
        // The Security tab renders the "Two-Factor Authentication" section (heading + Enable/Disable 2FA).
        // (The previous selector "text=Two-Factor, text=2FA, text=TOTP" was a malformed Playwright union
        // selector - it searched for that whole literal string and never matched.)
        await Expect(Page.GetByText("Two-Factor Authentication").First).ToBeVisibleAsync(new() { Timeout = 5000 });
    }
}
