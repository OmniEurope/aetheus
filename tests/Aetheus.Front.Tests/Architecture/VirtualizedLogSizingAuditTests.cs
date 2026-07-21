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

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(VirtualizedLogSizingAuditTests).Assembly.Location)!);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Aetheus.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root (Aetheus.slnx).");
    }
}
