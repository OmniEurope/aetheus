// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages;
using Bunit;

namespace Aetheus.Front.Tests.Pages;

public class NotFoundTests : BunitContext
{
    [Fact]
    public void Renders_NotFoundMessage()
    {
        BunitTestHelper.RegisterServices(this);
        var cut = Render<NotFound>();

        Assert.Contains("NotFound", cut.Markup);
        Assert.Contains("NotFoundMessage", cut.Markup);
    }
}

