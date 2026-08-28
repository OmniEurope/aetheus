// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Helpers;

namespace Aetheus.Front.Tests;

public sealed class NavigationReturnPathTests
{
    [Theory]
    [InlineData("/releases/12")]
    [InlineData("/pipelines/runs/42?tab=summary")]
    [InlineData("/git/branches/7")]
    public void Resolve_AcceptsOnlyLocalAppPaths(string path) =>
        Assert.Equal(path, NavigationReturnPath.Resolve(path, "/projects/1"));

    [Theory]
    [InlineData("https://example.test/steal")]
    [InlineData("//example.test/steal")]
    [InlineData("/\\example.test/steal")]
    [InlineData("relative/path")]
    [InlineData("")]
    public void Resolve_RejectsExternalOrMalformedPaths(string path) =>
        Assert.Equal("/projects/1", NavigationReturnPath.Resolve(path, "/projects/1"));

    [Fact]
    public void AddTo_EncodesReturnPathAsSingleQueryValue()
    {
        var result = NavigationReturnPath.AddTo(
            "/git/commits/3",
            "/pipelines/runs/42?tab=summary");

        Assert.Equal(
            "/git/commits/3?from=%2Fpipelines%2Fruns%2F42%3Ftab%3Dsummary",
            result);
    }
}
