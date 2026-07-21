// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.E2E;

[Category("E2E")]
[Category("Projects")]
public class ProjectsTests : E2ETestBase
{
    [SetUp]
    public async Task SetUp()
    {
        await LoginAsync();
    }

    [Test]
    public async Task Projects_PageRendersExpectedControlsAndContent()
    {
        await NavigateToAsync("projects");
        await Expect(Page.GetByText("Projects").First).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Expect(Page).ToHaveTitleAsync(new System.Text.RegularExpressions.Regex("Projects"));

        // Scope to the toolbar - the empty-state also renders a "New Project" CTA when the list is empty.
        var newButton = Page.Locator(".project-list-toolbar").GetByText("New Project");
        await Expect(newButton).ToBeVisibleAsync(new() { Timeout = 10000 });
        var searchBox = Page.Locator(".project-list-search");
        await Expect(searchBox).ToBeVisibleAsync(new() { Timeout = 10000 });

        await WaitForNoSpinnerAsync();

        // Either project cards exist or the EmptyState component renders.
        var cards = Page.Locator(".project-card");
        var emptyState = Page.Locator(".empty-state");

        var cardsCount = await cards.CountAsync();
        var emptyVisible = await emptyState.IsVisibleAsync();

        Assert.That(cardsCount > 0 || emptyVisible, Is.True, "Should show project cards or empty state");

        var dropdown = Page.Locator(".rz-dropdown").First;
        await Expect(dropdown).ToBeVisibleAsync();
        var refreshButton = Page.Locator("button[title='Refresh']");
        await Expect(refreshButton).ToBeVisibleAsync();
        var badge = Page.Locator(".rz-badge").Filter(new() { HasTextRegex = new System.Text.RegularExpressions.Regex(@"\d+\s*total", System.Text.RegularExpressions.RegexOptions.IgnoreCase) });
        await Expect(badge.First).ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    [Test]
    public async Task Projects_NewProject_PersistsAndIsDeletedThroughTheUi()
    {
        var projectName = $"e2e-project-{Guid.NewGuid():N}";
        await NavigateToAsync("projects");
        try
        {
            await Page.Locator(".project-list-toolbar").GetByText("New Project").ClickAsync();
            var createDialog = Page.Locator(".rz-dialog");
            await Expect(createDialog).ToBeVisibleAsync(new() { Timeout = 10000 });
            await createDialog.Locator("#project-form-name").FillAsync(projectName);
            await createDialog.Locator("textarea[name='Description']").FillAsync("E2E persistence probe");
            await createDialog.GetByRole(AriaRole.Button, new()
            {
                NameRegex = new System.Text.RegularExpressions.Regex("Create$")
            }).ClickAsync();

            var createdDialog = Page.Locator(".rz-dialog").Filter(new() { HasText = "Project created" });
            await Expect(createdDialog).ToBeVisibleAsync(new() { Timeout = 10000 });
            await createdDialog.GetByRole(AriaRole.Button, new()
            {
                NameRegex = new System.Text.RegularExpressions.Regex("Stay", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            }).ClickAsync();

            var projectCard = Page.Locator("a.project-card-link").Filter(new() { HasText = projectName });
            await Expect(projectCard).ToBeVisibleAsync(new() { Timeout = 10000 });
            var href = await projectCard.GetAttributeAsync("href");
            var idMatch = System.Text.RegularExpressions.Regex.Match(href ?? string.Empty, @"/projects/(\d+)");
            Assert.That(idMatch.Success, Is.True, "The persisted project card must expose its id.");
            var projectId = int.Parse(idMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

            await NavigateToAsync($"projects/{projectId}/edit");
            await Expect(Page.Locator("input[name='Name']")).ToHaveValueAsync(projectName, new() { Timeout = 10000 });
            await Page.GetByRole(AriaRole.Button, new()
            {
                NameRegex = new System.Text.RegularExpressions.Regex("Delete$")
            }).ClickAsync();
            var deleteDialog = Page.GetByRole(AriaRole.Alertdialog, new() { Name = "Delete", Exact = true });
            await Expect(deleteDialog).ToBeVisibleAsync(new() { Timeout = 5000 });
            await deleteDialog.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickAsync();

            await Expect(Page).ToHaveURLAsync(
                new System.Text.RegularExpressions.Regex(@"/projects/?$"),
                new() { Timeout = 10000 });
            await Expect(Page.Locator("a.project-card-link").Filter(new() { HasText = projectName }))
                .ToHaveCountAsync(0, new() { Timeout = 10000 });
        }
        finally
        {
            // Failure-safe cleanup: if an assertion above interrupted the UI path after persistence,
            // remove the uniquely named probe through the authenticated browser session.
            await Page.EvaluateAsync(
                """
                async ({ backendUrl, projectName }) => {
                    const token = localStorage.getItem('aetheus_auth_token');
                    const headers = { 'Authorization': `Bearer ${token}` };
                    const list = await fetch(
                        `${backendUrl}/api/projects?page=1&pageSize=200&search=${encodeURIComponent(projectName)}`,
                        { headers });
                    if (!list.ok) throw new Error(`Project cleanup lookup failed: HTTP ${list.status}`);
                    const page = await list.json();
                    for (const project of page.items.filter(item => item.name === projectName)) {
                        const response = await fetch(`${backendUrl}/api/projects/${project.id}`, {
                            method: 'DELETE',
                            headers
                        });
                        if (response.status !== 204 && response.status !== 404)
                            throw new Error(`Project cleanup failed: HTTP ${response.status}`);
                    }
                }
                """,
                new { backendUrl = BackendUrl, projectName });
        }
    }

    [Test]
    public async Task Projects_NavigateFromSidebar_Works()
    {
        await NavigateToAsync("");
        var nav = Page.Locator(".rz-panel-menu");
        await nav.GetByText("Projects").ClickAsync();

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/projects"), new() { Timeout = 5000 });
    }
}
