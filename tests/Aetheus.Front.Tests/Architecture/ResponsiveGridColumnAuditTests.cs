// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>Guards the product-wide grid contract: every column remains rendered at every viewport
/// width, and compact screens use horizontal scrolling instead of responsive column collapse.</summary>
public sealed class ResponsiveGridColumnAuditTests
{
    [Fact]
    public void EveryGrid_DisablesResponsiveColumnCollapse()
    {
        var frontDir = Path.Combine(FindRepoRoot(), "src", "Aetheus.Front");
        var razorFiles = RepositoryScan.Enumerate(frontDir, "*.razor");
        var gridCount = 0;

        foreach (var file in razorFiles)
        {
            var source = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(
                         source,
                         @"<RadzenDataGrid\b(?:(?:""[^""]*"")|[^>])*>",
                         RegexOptions.Singleline))
            {
                gridCount++;
                Assert.Contains("Responsive=\"false\"", match.Value, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("col-hide-", source, StringComparison.Ordinal);
            Assert.DoesNotContain("HiddenColumnsHint", source, StringComparison.Ordinal);
        }

        Assert.True(gridCount > 0, "No Radzen data grids were found to audit.");
    }

    /// <summary>
    /// Every data grid is sortable and advanced-filterable. All 81 grids already satisfied this when
    /// the guard was written on 2026-08-19 - it exists to keep it that way, since a new grid silently
    /// missing the attributes is invisible until a user needs to find a row.
    /// </summary>
    [Fact]
    public void EveryGrid_AllowsSortingAndAdvancedFiltering()
    {
        var frontDir = Path.Combine(FindRepoRoot(), "src", "Aetheus.Front");
        var violations = new List<string>();
        var gridCount = 0;

        foreach (var file in RepositoryScan.Enumerate(frontDir, "*.razor"))
        {
            var source = File.ReadAllText(file);
            var relative = Path.GetRelativePath(frontDir, file).Replace('\\', '/');

            foreach (Match match in Regex.Matches(
                         source,
                         @"<RadzenDataGrid\b(?:(?:""[^""]*"")|[^>])*>",
                         RegexOptions.Singleline))
            {
                gridCount++;
                var missing = new List<string>();
                if (!match.Value.Contains("AllowSorting", StringComparison.Ordinal)) missing.Add("AllowSorting");
                if (!match.Value.Contains("AllowFiltering", StringComparison.Ordinal)) missing.Add("AllowFiltering");
                if (missing.Count > 0) violations.Add($"{relative}: missing {string.Join(" + ", missing)}");
            }
        }

        Assert.True(gridCount > 0, "No Radzen data grids were found to audit.");
        Assert.True(violations.Count == 0,
            "Every data grid sets AllowSorting and AllowFiltering (claude-ui-patterns.md):\n  "
            + string.Join("\n  ", violations.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void EveryGrid_HasAHorizontalOverflowAndDefaultWidthContract()
    {
        var css = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "Aetheus.Front", "wwwroot", "css", "app.css"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains(".rz-data-grid .rz-data-grid-data {\n    overflow-x: auto;", css);
        Assert.Contains(
            ".rz-data-grid .rz-grid-table > colgroup > col:not([style^=\"width:\"]):not([style*=\";width:\"]):not([style*=\"; width:\"]) {\n    width: 10rem;",
            css);
        Assert.DoesNotContain(".rz-data-grid .col-hide-", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".hidden-columns-hint", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".pipeline-project-column {\n        display: none", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".pipeline-secondary-column {\n        display: none", css, StringComparison.Ordinal);
    }

    [Fact]
    public void DenseProjectAndPipelineTrees_ScrollWithoutHidingColumns()
    {
        var css = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "Aetheus.Front", "wwwroot", "css", "app.css"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains(".project-dense-list {\n    border:", css, StringComparison.Ordinal);
        Assert.Contains("overflow-x: auto;", css, StringComparison.Ordinal);
        Assert.Contains(".project-dense-header,\n.project-dense-row", css, StringComparison.Ordinal);
        Assert.Contains("min-width: 58rem;", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".project-dense-users {\n        display: none", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".project-dense-header {\n        display: none", css, StringComparison.Ordinal);

        Assert.Contains(".pipeline-linked-runs {", css, StringComparison.Ordinal);
        Assert.Contains(".pipeline-run-tree-row {", css, StringComparison.Ordinal);
        Assert.Contains("min-width: 52rem;", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".pipeline-run-tree-step,\n    .pipeline-run-tree-duration {\n        display: none", css, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactSidebar_CssAndJavascriptShareThe1024PixelBoundary()
    {
        var root = FindRepoRoot();
        var css = File.ReadAllText(Path.Combine(
            root, "src", "Aetheus.Front", "wwwroot", "css", "app.css"));
        var javascript = File.ReadAllText(Path.Combine(
            root, "src", "Aetheus.Front", "wwwroot", "js", "layout.js"));

        Assert.Contains("@media (max-width: 1024px)", css, StringComparison.Ordinal);
        Assert.Contains("matchMedia('(max-width: 1024px)')", javascript, StringComparison.Ordinal);
        Assert.Contains(
            "return dotNetRef.invokeMethodAsync('OnViewportChanged'",
            javascript,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EveryGrid_HeaderTitleUsesBoundedEllipsis()
    {
        var css = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "Aetheus.Front", "wwwroot", "css", "app.css"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains(".rz-data-grid .rz-grid-table-fixed {\n    table-layout: fixed;", css);
        Assert.Contains(".rz-data-grid thead .rz-column-title-content", css);
        Assert.Contains("min-width: 0;", css);
        Assert.Contains("overflow: hidden;", css);
        Assert.Contains("text-overflow: ellipsis;", css);
        var titleRule = Regex.Match(
            css,
            @"\.rz-data-grid thead \.rz-column-title-content\s*\{(?<body>[^}]*)\}",
            RegexOptions.Singleline);
        Assert.True(titleRule.Success, "The bounded grid-header title rule is missing.");
        Assert.DoesNotContain("min-width: max-content;", titleRule.Groups["body"].Value);
    }

    [Fact]
    public void PipelineActionsColumn_IsNotCompressedBelowItsDeclaredWidthOnMobile()
    {
        var css = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "Aetheus.Front", "wwwroot", "css", "app.css"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        var expectedRule = string.Join('\n',
            ".pipeline-dependency-grid .pipeline-actions-column {",
            "        width: 16rem !important;",
            "        min-width: 16rem !important;",
            "        max-width: 16rem !important;",
            "    }");

        Assert.Contains(expectedRule, css, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailHeader_ActionsCompactFromAvailableContentWidth()
    {
        var css = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "Aetheus.Front", "wwwroot", "css", "app.css"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains(".detail-page-header {\n    container-name: detail-header;\n    container-type: inline-size;", css);
        Assert.Contains("@container detail-header (max-width: 52rem)", css);
        Assert.Contains(".detail-page-header > .rz-button .rz-button-text", css);
        Assert.Contains("@media (max-width: 48rem)", css);
        Assert.Contains("flex-wrap: wrap !important;", css);
        Assert.Contains(".detail-page-header > .detail-page-title", css);
        Assert.Contains(".detail-page-header > .flex-spacer", css);
    }

    private static string FindRepoRoot() => Aetheus.Front.Tests.Architecture.RepositoryScan.Root;
}
