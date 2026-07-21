// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.E2E;

[Category("E2E")]
[Category("Pipelines")]
public class PipelinesTests : E2ETestBase
{
    [SetUp]
    public async Task SetUp()
    {
        await LoginAsync();
    }

    [Test]
    public async Task Pipelines_PageRendersExpectedControlsAndColumns()
    {
        await NavigateToAsync("pipelines");
        await Expect(Page.GetByText("Pipelines").First).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Expect(Page).ToHaveTitleAsync(new System.Text.RegularExpressions.Regex("Pipelines"));

        // Scope to the toolbar - the empty-state also renders a "New Pipeline" CTA when the grid is empty.
        var newButton = Page.Locator(".pipeline-list-toolbar").GetByText("New Pipeline");
        await Expect(newButton).ToBeVisibleAsync(new() { Timeout = 10000 });

        await WaitForNoSpinnerAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { NameRegex = new("Pipelines with children") })).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Expect(Page.GetByRole(AriaRole.Heading, new() { NameRegex = new("Pipelines without children") })).ToBeVisibleAsync(new() { Timeout = 10000 });

        var grids = Page.Locator(".pipeline-dependency-grid");
        await Expect(grids).ToHaveCountAsync(2, new() { Timeout = 10000 });
        var grid = grids.First;
        await Expect(grid).ToBeVisibleAsync(new() { Timeout = 10000 });

        var searchBox = Page.Locator(".pipeline-list-search");
        await Expect(searchBox).ToBeVisibleAsync();
        var dropdown = Page.Locator(".rz-dropdown").First;
        await Expect(dropdown).ToBeVisibleAsync(new() { Timeout = 10000 });
        // The Pipelines refresh button only carries an aria-label (no title attribute).
        var refreshButton = Page.GetByRole(AriaRole.Button, new() { Name = "Refresh" });
        await Expect(refreshButton.First).ToBeVisibleAsync(new() { Timeout = 10000 });

        await Expect(grid.Locator(".pipeline-name-link").First).ToBeVisibleAsync(new() { Timeout = 10000 });

        // The split view keeps the metadata columns in both grids and exposes dependency details
        // through the expandable parent row instead of the former flat "Referenced pipelines" column.
        await Expect(grid.GetByText("Project", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(grid.GetByText("Pipeline", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(grid.GetByText("Trigger", new() { Exact = true })).ToBeVisibleAsync();

        await grid.GetByRole(AriaRole.Button, new() { Name = "Expand child item" }).First.ClickAsync();
        await Expect(grid.Locator(".pipeline-relation-list-execution")).ToBeVisibleAsync();
        await Expect(grid.Locator(".pipeline-relation-card").First).ToBeVisibleAsync();

        var badge = Page.Locator(".rz-badge").Filter(new() { HasTextRegex = new System.Text.RegularExpressions.Regex(@"\d+\s*total", System.Text.RegularExpressions.RegexOptions.IgnoreCase) });
        await Expect(badge.First).ToBeVisibleAsync(new() { Timeout = 10000 });

        // Search is a real client-side filter over the loaded dependency graph.
        await searchBox.FillAsync("toto-ci");
        await searchBox.PressAsync("Tab");
        await Expect(Page.GetByText("toto-ci", new() { Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = 5000 });
        await Expect(grids.GetByText("API CI", new() { Exact = true })).ToHaveCountAsync(0);

        var clearFilters = Page.GetByRole(AriaRole.Button, new() { Name = "Clear filters" });
        await clearFilters.ClickAsync();
        await Expect(grids.GetByText("API CI", new() { Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = 5000 });

        // Select the seeded Webhook trigger and prove that manual pipelines disappear.
        await dropdown.ClickAsync();
        await Page.Locator(".rz-dropdown-panel:visible .rz-dropdown-item")
            .Filter(new() { HasText = "Webhook" }).ClickAsync();
        await Expect(Page.GetByText("toto-ci", new() { Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = 5000 });
        await Expect(grids.GetByText("API CI", new() { Exact = true })).ToHaveCountAsync(0);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Clear filters" }).ClickAsync();
        await Expect(grids.GetByText("API CI", new() { Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = 5000 });

        // Refresh must complete both HTTP branches successfully and keep the seeded content rendered.
        var dependenciesResponse = Page.WaitForResponseAsync(response =>
            response.Request.Method == "GET"
            && response.Url.EndsWith("/api/pipelines/dependencies", StringComparison.OrdinalIgnoreCase));
        var recentRunsResponse = Page.WaitForResponseAsync(response =>
            response.Request.Method == "GET"
            && response.Url.EndsWith("/api/pipelines/runs/recent", StringComparison.OrdinalIgnoreCase));
        await refreshButton.First.ClickAsync();
        var refreshResponses = await Task.WhenAll(
            dependenciesResponse.WaitAsync(TimeSpan.FromSeconds(10)),
            recentRunsResponse.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.That(refreshResponses.All(response => response.Ok), Is.True,
            $"Pipeline refresh returned HTTP {string.Join(", ", refreshResponses.Select(response => response.Status))}.");
        await Expect(Page.GetByText("toto-ci", new() { Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    [Test]
    public async Task Pipelines_NewPipeline_NavigatesToEditor()
    {
        await NavigateToAsync("pipelines");
        await Page.Locator(".pipeline-list-toolbar").GetByText("New Pipeline").ClickAsync();

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/pipelines/new"), new() { Timeout = 5000 });
        await Expect(Page.Locator("input[name='Name']")).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Expect(Page.GetByRole(AriaRole.Button, new()
        {
            NameRegex = new System.Text.RegularExpressions.Regex("Create$")
        }))
            .ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    [Test]
    public async Task Pipelines_NavigateFromSidebar_Works()
    {
        // "Pipelines" is a contextual sub-item of the Projects section, only
        // rendered while inside that section. Start from /projects.
        await NavigateToAsync("projects");
        await ClickSidebarNavItemAsync("Pipelines");

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/pipelines"), new() { Timeout = 5000 });
    }

}
