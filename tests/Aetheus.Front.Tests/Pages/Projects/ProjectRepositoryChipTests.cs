// SPDX-License-Identifier: EUPL-1.2
using ProjectsPage = Aetheus.Front.Pages.Projects.Projects;

namespace Aetheus.Front.Tests.Pages;

/// <summary>
/// B3KP: the projects-list "Repository" chip must recognise this control plane's own internal
/// smart-HTTP mirror clone URL (<c>/git/{id}/{slug}.git</c>) so it deep-links to the internal git
/// browser instead of opening a raw git-protocol endpoint in a new tab (the prod bug where the chip
/// pointed at <c>https://aetheus-api.../git/1/aetheus.git</c>).
/// </summary>
public class ProjectRepositoryChipTests
{
    [Theory]
    [InlineData("https://git.example.com/git/1/aetheus.git")]
    [InlineData("http://localhost:5300/git/42/my-repo.git")]
    [InlineData("/git/7/svc.git")]
    [InlineData("git/7/svc.git")]
    public void IsInternalMirrorUrl_TrueForMirrorClonePaths(string url)
    {
        Assert.True(ProjectsPage.IsInternalMirrorUrl(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://github.com/acme/widgets")]
    [InlineData("https://github.com/acme/widgets.git")]
    [InlineData("git@github.com:acme/widgets.git")]
    [InlineData("https://git.example.com/git/aetheus.git")] // no numeric project segment
    public void IsInternalMirrorUrl_FalseForExternalOrMalformed(string? url)
    {
        Assert.False(ProjectsPage.IsInternalMirrorUrl(url));
    }
}
