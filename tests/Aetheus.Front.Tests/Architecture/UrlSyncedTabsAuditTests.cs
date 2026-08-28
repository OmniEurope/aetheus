// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// Guards the UrlSyncedTabs convention per individual RadzenTabs container. Exceptions
/// identify one exact container and expected tab count, never an entire file.
/// </summary>
public class UrlSyncedTabsAuditTests
{
    private static readonly AllowedContainer[] Allowlist =
    [
        new(
            Path.Combine("Pages", "Settings", "Settings.razor"),
            0,
            6,
            "route-based tabs (/settings/{tab})"),
        new(
            Path.Combine("Pages", "Servers", "ServerDetailSections", "DockerProjectDialog.razor"),
            0,
            5,
            "transient project-details dialog with no route"),
        new(
            Path.Combine("Pages", "Ai", "AiResultDialog.razor"),
            0,
            2,
            "transient AI-result dialog with no route"),
        new(
            Path.Combine("Shared", "AppTelemetryPanel.razor"),
            0,
            5,
            "nested telemetry tabs under the route-level monitoring section"),
    ];

    private static readonly Regex TabsTokenRegex = new(
        @"</RadzenTabs>|<RadzenTabs(?=[\s>])|<RadzenTabsItem(?=[\s>])",
        RegexOptions.Compiled);

    [Fact]
    public void MultiTab_Containers_Use_UrlSyncedTabs_Or_An_Exact_Exception()
    {
        var frontDir = Path.Combine(FindRepoRoot(), "src", "Aetheus.Front");
        Assert.True(Directory.Exists(frontDir), $"Front dir not found: {frontDir}");

        var violations = new List<string>();
        var matchedExceptions = new HashSet<AllowedContainer>();
        var containersScanned = 0;

        foreach (var file in RepositoryScan.Enumerate(frontDir, "*.razor"))
        {
            var raw = StripRazorComments(File.ReadAllText(file));
            var rel = Path.GetRelativePath(frontDir, file);

            foreach (var container in EnumerateContainers(raw))
            {
                containersScanned++;
                var exception = Allowlist.SingleOrDefault(item =>
                    item.Path.Equals(rel, StringComparison.OrdinalIgnoreCase)
                    && item.Ordinal == container.Ordinal);

                if (exception is not null)
                {
                    matchedExceptions.Add(exception);
                    if (container.TabCount != exception.ExpectedTabCount)
                    {
                        violations.Add(
                            $"{rel}:{container.Line} exception #{container.Ordinal} expected "
                            + $"{exception.ExpectedTabCount} tabs but found {container.TabCount}");
                    }
                    continue;
                }

                if (container.TabCount >= 2)
                {
                    violations.Add(
                        $"{rel}:{container.Line} bare <RadzenTabs> #{container.Ordinal} has "
                        + $"{container.TabCount} tabs - use <UrlSyncedTabs> or add an exact exception");
                }
            }
        }

        foreach (var stale in Allowlist.Except(matchedExceptions))
            violations.Add($"{stale.Path}: stale exception for container #{stale.Ordinal} ({stale.Reason})");

        Assert.True(containersScanned >= Allowlist.Length,
            $"Scanner found only {containersScanned} bare RadzenTabs containers.");
        Assert.Empty(violations);
    }

    [Fact]
    public void Scanner_Counts_Each_Container_Independently()
    {
        const string markup = """
            <RadzenTabs><Tabs><RadzenTabsItem /></Tabs></RadzenTabs>
            <RadzenTabs>
                <Tabs>
                    <RadzenTabsItem />
                    <RadzenTabsItem />
                </Tabs>
            </RadzenTabs>
            """;

        var containers = EnumerateContainers(markup);

        Assert.Collection(
            containers,
            first =>
            {
                Assert.Equal(0, first.Ordinal);
                Assert.Equal(1, first.TabCount);
            },
            second =>
            {
                Assert.Equal(1, second.Ordinal);
                Assert.Equal(2, second.TabCount);
            });
    }

    [Fact]
    public void SharedTabs_ReserveStableGeometry()
    {
        var frontDir = Path.Combine(FindRepoRoot(), "src", "Aetheus.Front");
        var wrapper = File.ReadAllText(Path.Combine(frontDir, "Shared", "UrlSyncedTabs.razor"));
        var css = File.ReadAllText(Path.Combine(frontDir, "wwwroot", "css", "app.css"));

        Assert.Contains("class=\"@CssClass\"", wrapper, StringComparison.Ordinal);
        Assert.Contains("scrollbar-gutter: stable", css, StringComparison.Ordinal);
        Assert.Contains(".url-synced-tabs > .rz-tabview-nav", css, StringComparison.Ordinal);
        Assert.Contains("flex: 1 1 0", css, StringComparison.Ordinal);
        Assert.Contains(".rz-tabview > .rz-tabview-nav > li > button", css, StringComparison.Ordinal);
        Assert.Contains("width: 100%", css, StringComparison.Ordinal);
        Assert.Contains("scrollbar-width: none", css, StringComparison.Ordinal);
    }

    private static List<TabsContainer> EnumerateContainers(string source)
    {
        var result = new List<TabsContainer>();
        var stack = new Stack<OpenContainer>();
        var ordinal = 0;

        foreach (var token in TabsTokenRegex.Matches(source).Cast<Match>())
        {
            if (token.Value.StartsWith("<RadzenTabsItem", StringComparison.Ordinal))
            {
                if (stack.TryPeek(out var current)) current.TabCount++;
                continue;
            }

            if (token.Value.StartsWith("</", StringComparison.Ordinal))
            {
                if (stack.TryPop(out var closed))
                    result.Add(new TabsContainer(closed.Ordinal, closed.TabCount, closed.Line));
                continue;
            }

            var tagEnd = FindTagEnd(source, token.Index);
            if (tagEnd < 0) continue;
            if (source.AsSpan(token.Index, tagEnd - token.Index + 1).TrimEnd().EndsWith("/>"))
            {
                result.Add(new TabsContainer(ordinal++, 0, LineNumberAt(source, token.Index)));
                continue;
            }

            stack.Push(new OpenContainer(ordinal++, LineNumberAt(source, token.Index)));
        }

        result.Sort((left, right) => left.Ordinal.CompareTo(right.Ordinal));
        return result;
    }

    private static int FindTagEnd(string source, int start)
    {
        var inQuote = false;
        for (var i = start; i < source.Length; i++)
        {
            if (source[i] == '"') inQuote = !inQuote;
            else if (source[i] == '>' && !inQuote) return i;
        }
        return -1;
    }

    private static int LineNumberAt(string source, int index)
    {
        var line = 1;
        for (var i = 0; i < index; i++) if (source[i] == '\n') line++;
        return line;
    }

    private static string StripRazorComments(string source) =>
        Regex.Replace(source, @"@\*.*?\*@", string.Empty, RegexOptions.Singleline);

    private sealed record AllowedContainer(string Path, int Ordinal, int ExpectedTabCount, string Reason);
    private sealed record TabsContainer(int Ordinal, int TabCount, int Line);

    private sealed class OpenContainer(int ordinal, int line)
    {
        public int Ordinal { get; } = ordinal;
        public int Line { get; } = line;
        public int TabCount { get; set; }
    }

    private static string FindRepoRoot() => Aetheus.Front.Tests.Architecture.RepositoryScan.Root;
}
