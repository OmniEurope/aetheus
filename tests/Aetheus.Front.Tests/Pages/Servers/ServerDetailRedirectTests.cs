// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.Servers;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages.Servers;

/// <summary>
/// The retired monolithic <c>ServerDetail</c> page is now a thin redirector: the bare
/// <c>/servers/{id}</c> route forwards to <c>/servers/{id}/overview</c>, and a legacy
/// <c>?section=&lt;slug&gt;</c> maps onto <c>/servers/{id}/&lt;slug&gt;</c> (unknown slugs fall back
/// to overview).
/// </summary>
public class ServerDetailRedirectTests : BunitContext
{
    public ServerDetailRedirectTests() => BunitTestHelper.RegisterServices(this);

    private BunitNavigationManager Nav => Services.GetRequiredService<BunitNavigationManager>();

    /// <summary>Seeds the <c>?section=</c> query so the [SupplyParameterFromQuery] binds, then
    /// renders the redirector and returns the resulting Uri.</summary>
    private string RenderWithSection(int id, string? section)
    {
        Nav.NavigateTo($"/servers/{id}");
        if (section is not null)
            Nav.NavigateTo(Nav.GetUriWithQueryParameter("section", section));
        Render<ServerDetail>(p => p.Add(x => x.Id, id));
        return Nav.Uri;
    }

    [Fact]
    public void BareRoute_RedirectsToOverview()
    {
        Render<ServerDetail>(p => p.Add(x => x.Id, 42));
        Assert.EndsWith("/servers/42/overview", Nav.Uri);
    }

    [Fact]
    public void SectionQuery_RedirectsToMatchingSectionRoute()
        => Assert.EndsWith("/servers/42/apache", RenderWithSection(42, "apache"));

    [Fact]
    public void UnknownSectionQuery_FallsBackToOverview()
        => Assert.EndsWith("/servers/7/overview", RenderWithSection(7, "not-a-real-section"));

    [Fact]
    public void EmptySectionQuery_RedirectsToOverview()
        => Assert.EndsWith("/servers/3/overview", RenderWithSection(3, ""));

    [Fact]
    public void KnownSectionQuery_IsCaseInsensitive()
        // The known-section check is case-insensitive but preserves the supplied slug.
        => Assert.EndsWith("/servers/5/Docker", RenderWithSection(5, "Docker"));
}
