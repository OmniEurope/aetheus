// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Shared;
using Bunit;

namespace Aetheus.Front.Tests.Shared;

public class BrowserBackButtonTests : BunitContext
{
    [Fact]
    public void Click_UsesBrowserHistoryBack()
    {
        JSInterop.SetupVoid("Aetheus.goBack");
        var cut = Render<BrowserBackButton>(parameters => parameters
            .Add(component => component.Text, "Back")
            .Add(component => component.Title, "Go back"));

        cut.Find("button").Click();

        JSInterop.VerifyInvoke("Aetheus.goBack");
    }
}
