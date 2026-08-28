// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

public class VirtualizedLogSizingAuditTests
{
    [Fact]
    public void VirtualizedLogRows_MatchTheirDeclaredItemSize()
    {
        var root = FindRepoRoot();
        var markup = File.ReadAllText(Path.Combine(root, "src", "Aetheus.Front", "Pages", "Pipelines", "PipelineRun.razor"));
        var css = File.ReadAllText(Path.Combine(root, "src", "Aetheus.Front", "wwwroot", "css", "app.css"));

        var itemSize = Regex.Match(markup, @"<Virtualize\b[^>]*\bItemSize=""(?<size>\d+)""")
            .Groups["size"].Value;
        var rowRule = Regex.Match(css, @"\.run-log-line\s*\{(?<body>.*?)\}", RegexOptions.Singleline)
            .Groups["body"].Value;

        Assert.Equal("21", itemSize);
        Assert.Contains("height: 1.3125rem", rowRule);
        Assert.Contains("white-space: pre", rowRule);
        Assert.DoesNotContain("pre-wrap", rowRule);
    }

    [Fact]
    public void RunTimeline_WithManySteps_ScrollsInItsPanelInsteadOfClippingTheTree()
    {
        var root = FindRepoRoot();
        var css = File.ReadAllText(Path.Combine(root, "src", "Aetheus.Front", "wwwroot", "css", "app.css"));
        var timelineRule = Regex.Match(
            css,
            @"\.run-split-timeline\s*\{(?<body>.*?)\}",
            RegexOptions.Singleline).Groups["body"].Value;
        var treeRule = Regex.Match(
            css,
            @"\.run-tree:not\(\.run-tree-nested\)\s*\{(?<body>.*?)\}",
            RegexOptions.Singleline).Groups["body"].Value;

        Assert.Contains("min-height: 0", timelineRule);
        Assert.Contains("overflow-y: auto", timelineRule);
        Assert.Contains("overscroll-behavior: contain", timelineRule);
        Assert.Contains("scrollbar-gutter: stable", timelineRule);
        Assert.Contains("flex: 0 0 auto", treeRule);
    }

    private static string FindRepoRoot() => Aetheus.Front.Tests.Architecture.RepositoryScan.Root;
}
