// SPDX-License-Identifier: EUPL-1.2
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Aetheus.Agent.Core.Operations;

public sealed record ParsedCSharpSource(string Path, string Content, SyntaxTree SyntaxTree);

internal static class CSharpSourceParser
{
    private const long ParallelParsingMemoryFloor = 4L * 1024 * 1024 * 1024;

    internal static IReadOnlyList<ParsedCSharpSource> Parse(
        IReadOnlyList<(string Path, string Content)> sources,
        long? availableMemoryBytes = null,
        int? processorCount = null,
        CancellationToken ct = default)
    {
        var result = new ParsedCSharpSource[sources.Count];
        var memory = availableMemoryBytes ?? GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var processors = processorCount ?? Environment.ProcessorCount;
        var degree = memory < ParallelParsingMemoryFloor
            ? 1
            : Math.Clamp(processors / 2, 1, 4);
        if (degree == 1)
        {
            for (var index = 0; index < sources.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                result[index] = ParseOne(sources[index]);
            }
            return result;
        }

        Parallel.For(0, sources.Count, new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = degree
        }, index => result[index] = ParseOne(sources[index]));
        return result;
    }

    private static ParsedCSharpSource ParseOne((string Path, string Content) source) =>
        new(source.Path, source.Content, CSharpSyntaxTree.ParseText(source.Content, path: source.Path));
}
