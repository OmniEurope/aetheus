// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using HelpArticlePage = Aetheus.Front.Pages.Help.HelpArticle;

namespace Aetheus.Front.Tests.Pages;

public class HelpArticleTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public HelpArticleTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void Renders_HelpArticlePage_WithKnownKey()
    {
        _handler.SetJsonResponse("help/help-", new List<HelpArticle>
        {
            new("servers", "Servers", "Manage servers", "rzi-server", [])
        });

        var cut = Render<HelpArticlePage>(p => p.Add(x => x.PageKey, "servers"));
        // The matched article's title and description render on the page.
        Assert.Contains("Servers", cut.Markup);
        Assert.Contains("Manage servers", cut.Markup);
    }

    [Fact]
    public void Renders_Markdown_Without_Enabling_Raw_Html()
    {
        _handler.SetJsonResponse("help/help-", new List<HelpArticle>
        {
            new("servers", "Servers", "Manage servers", "rzi-server",
            [
                new HelpSection("Operations", "**Important**\n\n- First step\n- Second step\n\n<script>alert('unsafe')</script>")
            ])
        });

        var cut = Render<HelpArticlePage>(p => p.Add(x => x.PageKey, "servers"));

        Assert.Contains("<strong>Important</strong>", cut.Markup);
        Assert.Contains("<li>First step</li>", cut.Markup);
        Assert.DoesNotContain("<script>", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Renders_HelpArticlePage_WithUnknownKey()
    {
        _handler.SetJsonResponse("help/help-", new List<HelpArticle>());

        var cut = Render<HelpArticlePage>(p => p.Add(x => x.PageKey, "nonexistent"));

        // No article matches → the page redirects back to the help center.
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        Assert.EndsWith("/help", nav.Uri);
    }

    [Fact]
    public void Renders_HelpArticlePage_WithEmptyKey()
    {
        _handler.SetJsonResponse("help/help-", new List<HelpArticle>());

        var cut = Render<HelpArticlePage>(p => p.Add(x => x.PageKey, ""));

        // An empty key matches no article → redirect back to the help center.
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        Assert.EndsWith("/help", nav.Uri);
    }
}
