// SPDX-License-Identifier: EUPL-1.2
using System.Net.Http;
using Aetheus.Front.Pages.Settings;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Radzen.Blazor;

namespace Aetheus.Front.Tests.Pages.Settings;

public class PersonalAccessTokensPageTests : BunitContext
{
    private static PersonalAccessTokenDto Active(int id = 1, string name = "ci") => new()
    {
        Id = id,
        Name = name,
        Scope = PatScope.ReadOnly,
        TokenPrefix = "aeth_pat_ab",
        CreatedAt = DateTime.Now.AddDays(-1),
        ExpiresAt = DateTime.Now.AddDays(30)
    };

    private static PersonalAccessTokenDto Revoked(int id = 2, string name = "old") => new()
    {
        Id = id,
        Name = name,
        Scope = PatScope.ReadWrite,
        TokenPrefix = "aeth_pat_cd",
        CreatedAt = DateTime.Now.AddDays(-5),
        ExpiresAt = DateTime.Now.AddDays(30),
        RevokedAt = DateTime.Now.AddDays(-1)
    };

    [Fact]
    public async Task Renders_TokenRows_WithStatusAndRevokeOnlyForActive()
    {
        var handler = BunitTestHelper.RegisterServices(this);
        handler.SetPaginatedJsonResponse(HttpMethod.Get, "personal-access-tokens",
            new[] { Active(1, "ci"), Revoked(2, "old") });

        var cut = Render<PersonalAccessTokens>();
        var grid = cut.FindComponent<RadzenDataGrid<PersonalAccessTokenDto>>();
        await cut.InvokeAsync(grid.Instance.Reload);

        Assert.Contains("ci", cut.Markup);
        Assert.Contains("old", cut.Markup);
        Assert.Contains("PatStatusActive", cut.Markup);
        Assert.Contains("PatStatusRevoked", cut.Markup);
        // Exactly one revoke button (only the active token is revocable).
        Assert.Single(cut.FindAll("button[title=\"PatRevoke\"]"));
    }

    [Fact]
    public async Task EmptyList_ShowsEmptyState()
    {
        var handler = BunitTestHelper.RegisterServices(this);
        handler.SetPaginatedJsonResponse(HttpMethod.Get, "personal-access-tokens", Array.Empty<PersonalAccessTokenDto>());

        var cut = Render<PersonalAccessTokens>();
        var grid = cut.FindComponent<RadzenDataGrid<PersonalAccessTokenDto>>();
        await cut.InvokeAsync(grid.Instance.Reload);

        Assert.Contains("PatEmptyTitle", cut.Markup);
    }

    [Fact]
    public void Create_RevealsPlaintextTokenOnce_AndPostsRequest()
    {
        var handler = BunitTestHelper.RegisterServices(this);
        handler.SetPaginatedJsonResponse(HttpMethod.Get, "personal-access-tokens", Array.Empty<PersonalAccessTokenDto>());
        handler.SetJsonResponse(HttpMethod.Post, "personal-access-tokens", new CreatedPersonalAccessTokenDto
        {
            Token = Active(3, "deploy"),
            PlaintextToken = "aeth_pat_SUPERSECRETVALUE1234567890"
        });

        var cut = Render<PersonalAccessTokens>();

        // Fill the name and submit the create form.
        cut.FindAll("input")[0].Change("deploy");
        cut.Find("button[type=\"submit\"]").Click();

        Assert.Contains("aeth_pat_SUPERSECRETVALUE1234567890", cut.Markup);
        Assert.Contains("PatRevealTitle", cut.Markup);
        Assert.Contains(handler.Requests, r => r.Method == "POST" && r.Url.Contains("personal-access-tokens"));
    }
}
