// SPDX-License-Identifier: EUPL-1.2
using System.Text;
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// Guards the convention that every direct API call inside <c>OnInitializedAsync</c>
/// in page code-behind files is enclosed by a <c>try</c> whose associated catch chain
/// explicitly handles <see cref="HttpRequestException"/>.
/// </summary>
public class OnInitApiCallGuardTests
{
    private static readonly Regex OnInitRegex = new(
        @"\b(?:protected|private|public|internal)\s+(?:override\s+)?(?:async\s+)?Task\s+OnInitializedAsync\s*\(",
        RegexOptions.Compiled);
    private static readonly Regex ApiCallRegex = new(
        @"\b(?:Api|_api|Http|_http|ApiClient)\s*\.\s*\w+Async\s*(?:<|\()",
        RegexOptions.Compiled);
    private static readonly Regex TryRegex = new(@"\btry\b", RegexOptions.Compiled);
    private static readonly Regex HttpRequestCatchRegex = new(
        @"\bHttpRequestException\b",
        RegexOptions.Compiled);

    [Fact]
    public void OnInitializedAsync_ApiCalls_Are_GuardedByHttpRequestCatch()
    {
        var pagesDir = Path.Combine(FindRepoRoot(), "src", "Aetheus.Front", "Pages");
        Assert.True(Directory.Exists(pagesDir), $"Pages dir not found: {pagesDir}");

        var violations = new List<string>();
        var callsScanned = 0;

        foreach (var file in Directory.EnumerateFiles(pagesDir, "*.razor.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            var unguardedCalls = FindUnguardedApiCalls(source, out var scannedInFile);
            callsScanned += scannedInFile;
            foreach (var call in unguardedCalls)
            {
                violations.Add(
                    $"{Path.GetRelativePath(pagesDir, file)}:{call.Line} - {call.Expression} is not enclosed "
                    + "by try/catch (HttpRequestException)");
            }
        }

        Assert.True(callsScanned >= 5,
            $"Scanner found only {callsScanned} OnInitializedAsync API calls - likely a scanner regression.");
        Assert.Empty(violations);
    }

    [Fact]
    public void Scanner_Requires_The_Api_Call_To_Be_Inside_The_Protected_Try()
    {
        const string source = """
            protected override async Task OnInitializedAsync()
            {
                try { await Api.GetFirstAsync(); }
                catch (HttpRequestException) { }
                await Api.GetSecondAsync();
            }
            """;

        var violations = FindUnguardedApiCalls(source, out var callsScanned);

        Assert.Equal(2, callsScanned);
        Assert.Equal("Api.GetSecondAsync(", Assert.Single(violations).Expression);
    }

    [Fact]
    public void Scanner_Rejects_A_General_Catch_And_Ignores_Textual_Decoys()
    {
        const string source = """
            protected override async Task OnInitializedAsync()
            {
                var decoy = "try catch HttpRequestException Api.GetFakeAsync(";
                // try { await Api.GetCommentAsync(); } catch (HttpRequestException) { }
                try { await Api.GetRealAsync(); }
                catch (Exception) { }
            }
            """;

        var violations = FindUnguardedApiCalls(source, out var callsScanned);

        Assert.Equal(1, callsScanned);
        Assert.Equal("Api.GetRealAsync(", Assert.Single(violations).Expression);
    }

    private static List<UnguardedCall> FindUnguardedApiCalls(string source, out int callsScanned)
    {
        var masked = MaskCommentsAndStrings(source);
        var violations = new List<UnguardedCall>();
        callsScanned = 0;

        foreach (var methodMatch in OnInitRegex.Matches(masked).Cast<Match>())
        {
            var parameterOpen = masked.IndexOf('(', methodMatch.Index + methodMatch.Length - 1);
            var parameterClose = FindMatching(masked, parameterOpen, '(', ')');
            if (parameterClose < 0) continue;

            var bodyOpen = NextNonWhitespace(masked, parameterClose + 1);
            if (bodyOpen < 0 || masked[bodyOpen] != '{') continue;
            var bodyClose = FindMatching(masked, bodyOpen, '{', '}');
            if (bodyClose < 0) continue;

            var protectedRanges = FindHttpRequestProtectedTryRanges(masked, bodyOpen, bodyClose);
            for (var call = ApiCallRegex.Match(masked, bodyOpen);
                 call.Success && call.Index < bodyClose;
                 call = call.NextMatch())
            {
                callsScanned++;
                if (protectedRanges.Any(range => call.Index > range.Start && call.Index < range.End))
                    continue;

                violations.Add(new UnguardedCall(
                    LineNumberAt(source, call.Index),
                    source.Substring(call.Index, call.Length).Trim()));
            }
        }

        return violations;
    }

    private static List<SourceRange> FindHttpRequestProtectedTryRanges(
        string source,
        int methodBodyOpen,
        int methodBodyClose)
    {
        var ranges = new List<SourceRange>();
        for (var tryMatch = TryRegex.Match(source, methodBodyOpen);
             tryMatch.Success && tryMatch.Index < methodBodyClose;
             tryMatch = tryMatch.NextMatch())
        {
            var tryBodyOpen = NextNonWhitespace(source, tryMatch.Index + tryMatch.Length);
            if (tryBodyOpen < 0 || source[tryBodyOpen] != '{') continue;
            var tryBodyClose = FindMatching(source, tryBodyOpen, '{', '}');
            if (tryBodyClose < 0 || tryBodyClose > methodBodyClose) continue;

            var cursor = tryBodyClose + 1;
            var handlesHttpRequest = false;
            while (true)
            {
                cursor = NextNonWhitespace(source, cursor);
                if (!StartsWithKeyword(source, cursor, "catch")) break;

                var catchSignatureStart = cursor + "catch".Length;
                var catchBodyOpen = source.IndexOf('{', catchSignatureStart);
                if (catchBodyOpen < 0 || catchBodyOpen > methodBodyClose) break;
                handlesHttpRequest |= HttpRequestCatchRegex.IsMatch(
                    source[catchSignatureStart..catchBodyOpen]);

                var catchBodyClose = FindMatching(source, catchBodyOpen, '{', '}');
                if (catchBodyClose < 0) break;
                cursor = catchBodyClose + 1;
            }

            if (handlesHttpRequest)
                ranges.Add(new SourceRange(tryBodyOpen, tryBodyClose));
        }

        return ranges;
    }

    private static bool StartsWithKeyword(string source, int index, string keyword) =>
        index >= 0
        && index + keyword.Length <= source.Length
        && source.AsSpan(index, keyword.Length).SequenceEqual(keyword)
        && (index + keyword.Length == source.Length
            || !char.IsLetterOrDigit(source[index + keyword.Length]) && source[index + keyword.Length] != '_');

    private static int NextNonWhitespace(string source, int index)
    {
        while (index >= 0 && index < source.Length && char.IsWhiteSpace(source[index])) index++;
        return index < source.Length ? index : -1;
    }

    private static int FindMatching(string source, int openIndex, char open, char close)
    {
        if (openIndex < 0 || openIndex >= source.Length || source[openIndex] != open) return -1;
        var depth = 1;
        for (var i = openIndex + 1; i < source.Length; i++)
        {
            if (source[i] == open) depth++;
            else if (source[i] == close && --depth == 0) return i;
        }
        return -1;
    }

    private static int LineNumberAt(string source, int index) =>
        1 + source.AsSpan(0, index).Count('\n');

    private static string MaskCommentsAndStrings(string source)
    {
        var masked = new StringBuilder(source);
        for (var i = 0; i < source.Length; i++)
        {
            if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '/')
            {
                var end = source.IndexOf('\n', i + 2);
                Mask(masked, i, end < 0 ? source.Length : end);
                i = (end < 0 ? source.Length : end) - 1;
            }
            else if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '*')
            {
                var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var exclusiveEnd = end < 0 ? source.Length : end + 2;
                Mask(masked, i, exclusiveEnd);
                i = exclusiveEnd - 1;
            }
            else if (source[i] is '"' or '\'')
            {
                var delimiter = source[i];
                var verbatim = delimiter == '"' && i > 0 && source[i - 1] == '@';
                var end = i + 1;
                while (end < source.Length)
                {
                    if (source[end] == delimiter)
                    {
                        if (verbatim && end + 1 < source.Length && source[end + 1] == '"')
                        {
                            end += 2;
                            continue;
                        }
                        end++;
                        break;
                    }
                    if (!verbatim && source[end] == '\\' && end + 1 < source.Length) end += 2;
                    else end++;
                }
                Mask(masked, i, end);
                i = end - 1;
            }
        }
        return masked.ToString();
    }

    private static void Mask(StringBuilder source, int start, int exclusiveEnd)
    {
        for (var i = start; i < exclusiveEnd; i++)
            if (source[i] is not ('\r' or '\n')) source[i] = ' ';
    }

    private sealed record SourceRange(int Start, int End);
    private sealed record UnguardedCall(int Line, string Expression);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(OnInitApiCallGuardTests).Assembly.Location)!);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Aetheus.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root (Aetheus.slnx).");
    }
}
