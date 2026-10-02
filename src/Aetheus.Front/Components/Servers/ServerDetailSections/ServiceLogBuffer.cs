// SPDX-License-Identifier: EUPL-1.2
using System.Text;

namespace Aetheus.Front.Components.Servers.ServerDetailSections;

/// <summary>
/// The text of the service log viewer. Bounded to <see cref="MaxLines"/> lines: past it the buffer starts
/// over behind a truncation marker, so a long follow session never grows the pane without limit. A renewed
/// read can keep the current lines on screen until its own first line replaces them.
/// </summary>
internal sealed class ServiceLogBuffer
{
    public const int MaxLines = 2000;

    private readonly StringBuilder _text = new();
    private bool _replaceOnNextLine;

    public int LineCount { get; private set; }

    /// <summary>The lines held now stay until the next appended line, which replaces them all.</summary>
    public void ReplaceOnNextLine() => _replaceOnNextLine = true;

    public void Clear()
    {
        _text.Clear();
        LineCount = 0;
        _replaceOnNextLine = false;
    }

    /// <summary>Appends one line and returns the whole text to display.</summary>
    public string Append(string message)
    {
        if (_replaceOnNextLine)
        {
            _replaceOnNextLine = false;
            _text.Clear();
            LineCount = 0;
        }
        if (LineCount >= MaxLines)
        {
            _text.Clear();
            LineCount = 0;
            _text.AppendLine("[... truncated - max lines reached, refreshing ...]");
            LineCount++;
        }
        _text.AppendLine(message);
        LineCount++;
        return _text.ToString();
    }
}
