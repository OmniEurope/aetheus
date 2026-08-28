// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;
using Aetheus.Front.Layout;

namespace Aetheus.Front.Tests.Architecture;

public sealed class BreadcrumbRouteCoverageTests
{
    [Fact]
    public void Every_Razor_Route_Has_An_Immediate_Breadcrumb_Fallback()
    {
        var pages = Path.Combine(FindRepoRoot(), "src", "Aetheus.Front", "Pages");
        var failures = new List<string>();

        foreach (var file in RepositoryScan.Enumerate(pages, "*.razor"))
        {
            var source = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(source, "@page\\s+\"([^\"]+)\""))
            {
                var template = match.Groups[1].Value;
                var route = Regex.Replace(template, "\\{([^}:]+)(?::[^}]+)?\\}", parameter =>
                    parameter.Groups[1].Value.Equals("Sha", StringComparison.OrdinalIgnoreCase) ? "abc123" : "42");
                var items = BreadcrumbRouteResolver.Resolve(route, key => key);
                if (items.Count == 0 || items.Any(item => string.IsNullOrWhiteSpace(item.Text)))
                    failures.Add($"{Path.GetRelativePath(pages, file)}: {template}");
                else if (items[^1].Href is not null)
                    failures.Add($"{Path.GetRelativePath(pages, file)}: current item remains clickable for {template}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void Project_And_Server_Sections_Keep_Their_Owner_In_The_Path()
    {
        var project = BreadcrumbRouteResolver.Resolve("/projects/42/pipelines", key => key);
        var server = BreadcrumbRouteResolver.Resolve("/servers/7/docker", key => key);

        Assert.Equal(["Projects", "Project #42", "Pipelines"], project.Select(item => item.Text));
        Assert.Equal("/projects/42/overview", project[1].Href);
        Assert.True(project[1].IsLoading);
        Assert.Equal(["Servers", "Server #7", "Docker"], server.Select(item => item.Text));
        Assert.Equal("/servers/7/overview", server[1].Href);
        Assert.True(server[1].IsLoading);
    }

    [Fact]
    public void Pipeline_Run_Fallback_Identifies_The_Run()
    {
        var items = BreadcrumbRouteResolver.Resolve("/pipelines/runs/1312", key => key);

        Assert.Equal(["Pipelines", "PipelineRun #1312"], items.Select(item => item.Text));
        Assert.Equal("/pipelines", items[0].Href);
        Assert.Null(items[1].Href);
        Assert.True(items[1].IsLoading);
    }

    [Theory]
    [InlineData("/admin/users/new", "Users")]
    [InlineData("/admin/plugins", "Plugins")]
    [InlineData("/admin/dashboards", "Dashboards")]
    [InlineData("/api-reference", "ApiReference")]
    [InlineData("/users/new", "Users")]
    public void Administrative_Routes_Start_With_Administration(string route, string leaf)
    {
        var items = BreadcrumbRouteResolver.Resolve(route, key => key);

        Assert.Equal("Administration", items[0].Text);
        Assert.Equal("/admin", items[0].Href);
        Assert.Contains(items, item => item.Text.Contains(leaf, StringComparison.Ordinal));
    }

    [Fact]
    public void Global_Slot_Has_Fixed_Block_Geometry()
    {
        var css = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "Aetheus.Front", "wwwroot", "css", "app.css"));

        Assert.Contains(".app-breadcrumb {", css, StringComparison.Ordinal);
        Assert.Contains("flex: 0 0 1.75rem", css, StringComparison.Ordinal);
        Assert.Contains("height: 1.75rem", css, StringComparison.Ordinal);
        Assert.Contains("overflow-x: auto", css, StringComparison.Ordinal);
        Assert.Contains("width: max-content", css, StringComparison.Ordinal);
        Assert.Contains("min-width: 100%", css, StringComparison.Ordinal);
        Assert.Contains("white-space: nowrap", css, StringComparison.Ordinal);

        var itemContentRule = Regex.Match(
            css,
            @"\.app-breadcrumb\s*>\s*ol\s*>\s*li a,\s*\.app-breadcrumb\s*>\s*ol\s*>\s*li span\s*\{(?<body>[^}]*)\}");
        Assert.True(itemContentRule.Success);
        Assert.DoesNotContain("overflow: hidden", itemContentRule.Groups["body"].Value, StringComparison.Ordinal);
        Assert.DoesNotContain("text-overflow: ellipsis", itemContentRule.Groups["body"].Value, StringComparison.Ordinal);
    }

    private static string FindRepoRoot() => Aetheus.Front.Tests.Architecture.RepositoryScan.Root;
}
