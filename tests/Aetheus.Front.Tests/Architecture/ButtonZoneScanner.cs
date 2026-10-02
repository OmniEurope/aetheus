// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// Reads a <c>.razor</c> file as markup for the STD-BTN zone rule: every <c>OmniButton</c> and
/// <c>OmniSplitButton</c> with the element that directly holds it (its zone: a header's actions, a
/// dialog footer, a toolbar, one grid row's actions...) and the <c>if</c>/<c>else</c> branches around
/// it, since two buttons in different branches of one <c>if</c> never render together.
/// </summary>
internal static class ButtonZoneScanner
{
    /// <param name="Ancestors">The names of the elements around the button, innermost first.</param>
    internal sealed record ZoneButton(
        string Name, string Tag, int Line, int Zone, IReadOnlyList<(int If, int Branch)> Branches,
        IReadOnlyList<string> Ancestors);

    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr"
    };

    private static readonly Regex Comment = new(@"@\*.*?\*@", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex Tag = new(
        @"\G<(?<close>/)?(?<name>[A-Za-z][\w.:-]*)(?<attrs>(?:""[^""]*""|[^>""])*)>",
        RegexOptions.CultureInvariant);

    private static readonly Regex Branch = new(
        @"\G(?:@?else\s+if\s*\(|@?if\s*\(|@?else\s*\{)", RegexOptions.CultureInvariant);

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
                if (!tag.Success) { i++; continue; }
                var name = tag.Groups["name"].Value;
                var selfClosing = tag.Groups["attrs"].Value.TrimEnd().EndsWith('/');
                if (tag.Groups["close"].Success)
                {
                    if (elements.Any(e => e.Name == name))
                        while (elements.Pop().Name != name) { }
                    i += tag.Length;
                    continue;
                }

                if (name is "OmniButton" or "OmniSplitButton")
                {
                    var button = ReadButton(source, i, tag.Length, name, selfClosing, elements, frames);
                    result.Add(button.Button);
                    i = button.End;
                    continue;
                }

                if (!selfClosing && !VoidElements.Contains(name)) elements.Push((name, nextElement++));
                i += tag.Length;
                continue;
            }

            if (StartsBranch(source, i, out var branch))
            {
                var open = source.IndexOf('{', i);
                if (open < 0) break;
                var isElse = branch.Value.Contains("else", StringComparison.Ordinal);
                var frame = isElse && lastClosed.TryGetValue(frames.Count, out var previous)
                    ? (previous.If, previous.Branch + 1)
                    : (nextIf++, 0);
                frames.Push(frame);
                i = open + 1;
                continue;
            }

            if (c == '{') frames.Push((-1, 0));
            else if (c == '}' && frames.Count > 0)
            {
                var closed = frames.Pop();
                if (closed.If >= 0) lastClosed[frames.Count] = closed;
            }
            i++;
        }
        return result;
    }

    /// <summary>The button whose opening tag starts at <paramref name="start"/>, up to its closing tag.</summary>
    private static (ZoneButton Button, int End) ReadButton(
        string source, int start, int tagLength, string name, bool selfClosing,
        Stack<(string Name, int Id)> elements, Stack<(int If, int Branch)> frames)
    {
        var end = start + tagLength;
        if (!selfClosing)
        {
            var close = source.IndexOf($"</{name}>", end, StringComparison.Ordinal);
            end = close < 0 ? end : close + name.Length + 3;
        }
        var line = source.AsSpan(0, start).Count('\n') + 1;
        var button = new ZoneButton(name, source[start..end], line,
            elements.Count > 0 ? elements.Peek().Id : 0,
            frames.Where(f => f.If >= 0).ToList(),
            elements.Select(e => e.Name).ToList());
        return (button, end);
    }

    /// <summary>True when an <c>if</c>, <c>else if</c> or <c>else</c> block starts at <paramref name="i"/>.</summary>
    private static bool StartsBranch(string source, int i, out Match branch)
    {
        branch = Match.Empty;
        var c = source[i];
        if (c is not ('@' or 'i' or 'e') || (i > 0 && char.IsLetterOrDigit(source[i - 1]))) return false;
        branch = Branch.Match(source, i);
        return branch.Success;
    }

    /// <summary>True when the two buttons sit in different branches of one <c>if</c>.</summary>
    internal static bool Exclusive(ZoneButton a, ZoneButton b) =>
        a.Branches.Any(x => b.Branches.Any(y => y.If == x.If && y.Branch != x.Branch));

    /// <summary>
    /// True when the button is an action of a grid row: it sits in the display <c>Template</c> of an
    /// <c>OmniDataGridColumn</c>, so it repeats on every row. The <c>EditTemplate</c> of a row being edited
    /// is a small form whose Save is its main action, and a header or filter template is not a row.
    /// </summary>
    internal static bool InGridRow(ZoneButton button)
    {
        var column = button.Ancestors.ToList().IndexOf("OmniDataGridColumn");
        return column > 0 && button.Ancestors[column - 1] == "Template";
    }

    /// <summary>
    /// True when the button renders blue whatever its state: an <c>OmniButton</c> whose variant is
    /// Primary or left to OE's default (Primary), or an <c>OmniSplitButton</c> set to Primary (OE's
    /// split button defaults to Secondary). A variant computed per state is a toggle, not a main action.
    /// </summary>
    internal static bool IsPrimary(ZoneButton button)
    {
        var variant = Regex.Match(button.Tag, @"^<\w+\b(?:""[^""]*""|[^>""])*?\bVariant\s*=\s*""(?<v>[^""]*)""");
        if (!variant.Success) return button.Name == "OmniButton";
        return variant.Groups["v"].Value == "OmniButtonVariant.Primary";
    }
}
