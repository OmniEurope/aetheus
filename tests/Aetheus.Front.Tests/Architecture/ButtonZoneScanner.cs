// kit-model ButtonZoneScanner 1
// SPDX-License-Identifier: EUPL-1.2
//
// Razor zone reader behind the STD-BTN guard "at most one blue button per zone" (docs/code-rules.md).
// Copy it into a test project next to ButtonConventionGuardTests.cs and adapt only the namespace;
// keep the kit-model line so verify-rules.ps1 (STD-KITCOPY) reports the copy when this model moves.
//
// Aetheus copy: the model reads the button of the kit's example app, from a component library Aetheus
// no longer uses. Aetheus renders OE buttons, so the three button-library points are adapted: the
// button tags (OmniButton and OmniSplitButton), the colour attribute (Variant) and the default colour
// in IsMain (an OmniButton defaults to Primary, an OmniSplitButton to Secondary). The branch test of
// Buttons is moved into StartsBranch, unchanged, to keep Buttons under the cyclomatic budget of
// ComplexityAnalyzerTests (25; the model's Buttons is at 26).

using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// Reads a <c>.razor</c> source as markup for the <c>STD-BTN</c> zone rule (docs/code-rules.md): every
/// button with the element that directly holds it (its zone: a header's actions, a dialog footer, a
/// form's submit row, a toolbar...) and the <c>if</c>/<c>else</c> branches around it, since two buttons
/// in different branches of one <c>if</c> never render together.
/// </summary>
internal static class ButtonZoneScanner
{
    /// <summary>The button tags of this app: OE's button and split button.</summary>
    private static readonly HashSet<string> ButtonTags = new(StringComparer.Ordinal) { "OmniButton", "OmniSplitButton" };

    internal sealed record ZoneButton(string Markup, int Line, int Zone, IReadOnlyList<(int If, int Branch)> Branches);

    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr",
    };

    private static readonly Regex Comment = new(@"@\*.*?\*@", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex Tag = new(
        @"\G<(?<close>/)?(?<name>[A-Za-z][\w.:-]*)(?<attrs>(?:""[^""]*""|[^>""])*)>",
        RegexOptions.CultureInvariant);

    private static readonly Regex Branch = new(
        @"\G(?:@?else\s+if\s*\(|@?if\s*\(|@?else\s*\{)", RegexOptions.CultureInvariant);

    private static readonly Regex StyleAttr = new(
        @"^<\w+\b(?:""[^""]*""|[^>""])*?\bVariant\s*=\s*""(?<v>[^""]*)""", RegexOptions.CultureInvariant);

    /// <summary>The buttons of <paramref name="source"/>, in document order.</summary>
    internal static List<ZoneButton> Buttons(string source)
    {
        // Comments become spaces, their line breaks kept, so indices and line numbers stay true.
        source = Comment.Replace(source, m => Regex.Replace(m.Value, @"[^\n]", " "));
        var result = new List<ZoneButton>();
        var elements = new Stack<(string Name, int Id)>();
        var frames = new Stack<(int If, int Branch)>();
        var lastClosed = new Dictionary<int, (int If, int Branch)>();
        var nextElement = 1;
        var nextIf = 0;
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (c == '<')
            {
                var tag = Tag.Match(source, i);
                if (!tag.Success)
                {
                    i++;
                    continue;
                }

                var name = tag.Groups["name"].Value;
                var selfClosing = tag.Groups["attrs"].Value.TrimEnd().EndsWith('/');
                if (tag.Groups["close"].Success)
                {
                    if (elements.Any(e => e.Name == name))
                    {
                        while (elements.Pop().Name != name)
                        {
                        }
                    }

                    i += tag.Length;
                    continue;
                }

                if (ButtonTags.Contains(name))
                {
                    var end = i + tag.Length;
                    if (!selfClosing)
                    {
                        var close = source.IndexOf($"</{name}>", end, StringComparison.Ordinal);
                        end = close < 0 ? end : close + name.Length + 3;
                    }

                    var line = source.AsSpan(0, i).Count('\n') + 1;
                    result.Add(new ZoneButton(source[i..end], line,
                        elements.Count > 0 ? elements.Peek().Id : 0,
                        frames.Where(f => f.If >= 0).ToList()));
                    i = end;
                    continue;
                }

                if (!selfClosing && !VoidElements.Contains(name))
                {
                    elements.Push((name, nextElement++));
                }

                i += tag.Length;
                continue;
            }

            if (StartsBranch(source, i, out var branch))
            {
                var open = source.IndexOf('{', i);
                if (open < 0)
                {
                    break;
                }

                var isElse = branch.Value.Contains("else", StringComparison.Ordinal);
                var depth = frames.Count;
                var frame = isElse && lastClosed.TryGetValue(depth, out var previous)
                    ? (previous.If, previous.Branch + 1)
                    : (nextIf++, 0);
                frames.Push(frame);
                i = open + 1;
                continue;
            }

            if (c == '{')
            {
                frames.Push((-1, 0));
            }
            else if (c == '}' && frames.Count > 0)
            {
                var closed = frames.Pop();
                if (closed.If >= 0)
                {
                    lastClosed[frames.Count] = closed;
                }
            }

            i++;
        }

        return result;
    }

    /// <summary>True when an <c>if</c>, <c>else if</c> or <c>else</c> block starts at <paramref name="i"/>.</summary>
    private static bool StartsBranch(string source, int i, out Match branch)
    {
        var c = source[i];
        branch = (c == '@' || c == 'i' || c == 'e') && (i == 0 || !char.IsLetterOrDigit(source[i - 1]))
            ? Branch.Match(source, i)
            : Match.Empty;
        return branch.Success;
    }

    /// <summary>True when the two buttons sit in different branches of one <c>if</c>.</summary>
    internal static bool Exclusive(ZoneButton a, ZoneButton b) =>
        a.Branches.Any(x => b.Branches.Any(y => y.If == x.If && y.Branch != x.Branch));

    /// <summary>
    /// True when the button renders blue (the main action) whatever its state: <c>OmniButtonVariant.Primary</c>,
    /// or no <c>Variant</c> at all on an <c>OmniButton</c> (OE's default is Primary; an <c>OmniSplitButton</c>
    /// defaults to Secondary). A variant computed per state is a toggle, not a main action.
    /// </summary>
    internal static bool IsMain(ZoneButton button)
    {
        var style = StyleAttr.Match(button.Markup);
        return style.Success
            ? style.Groups["v"].Value == "OmniButtonVariant.Primary"
            : !button.Markup.StartsWith("<OmniSplitButton", StringComparison.Ordinal);
    }

    /// <summary>Every pair of main (blue) buttons of one zone that can render together.</summary>
    internal static IEnumerable<(ZoneButton First, ZoneButton Second)> SecondMainActions(string source)
    {
        foreach (var zone in Buttons(source).Where(IsMain).GroupBy(b => b.Zone))
        {
            var blue = zone.ToList();
            for (var a = 0; a < blue.Count; a++)
            {
                for (var b = a + 1; b < blue.Count; b++)
                {
                    if (!Exclusive(blue[a], blue[b]))
                    {
                        yield return (blue[a], blue[b]);
                    }
                }
            }
        }
    }
}
