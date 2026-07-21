// SPDX-License-Identifier: EUPL-1.2
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace Aetheus.Analyzers.Tests;

public sealed class Prm004Tests
{
    private const string EfStub = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;
        namespace Microsoft.EntityFrameworkCore
        {
            public static class EntityFrameworkQueryableExtensions
            {
                public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) =>
                    Task.FromResult(new List<T>());
                public static Task<T[]> ToArrayAsync<T>(this IQueryable<T> source) =>
                    Task.FromResult(Array.Empty<T>());
            }
        }
        public sealed class Row { public int Id { get; set; } }
        """;

    [Theory]
    [InlineData("ToListAsync")]
    [InlineData("ToArrayAsync")]
    [InlineData("ToList")]
    [InlineData("ToArray")]
    public async Task UnguardedEfMaterialization_InRepository_ReportsDiagnostic(string method)
    {
        var source = Query($".{method}()");

        await Verifier<PRM004_NoToListOnLargeTablesAnalyzer>.VerifyAsync(
            "FooRepository.cs", source, ExpectMethod(source, "FooRepository.cs", method));
    }

    [Theory]
    [InlineData("ToListAsync")]
    [InlineData("ToArrayAsync")]
    [InlineData("ToList")]
    [InlineData("ToArray")]
    public async Task TakeBeforeEfMaterialization_NoDiagnostic(string method)
    {
        var source = Query($".Take(100).{method}()");
        await Verifier<PRM004_NoToListOnLargeTablesAnalyzer>.VerifyAsync("FooRepository.cs", source);
    }

    [Fact]
    public async Task WhereBeforeToListAsync_RemainsUnboundedAndReportsDiagnostic()
    {
        var source = Query(".Where(row => row.Id > 0).ToListAsync()");
        await Verifier<PRM004_NoToListOnLargeTablesAnalyzer>.VerifyAsync(
            "FooRepository.cs", source, ExpectMethod(source, "FooRepository.cs", "ToListAsync"));
    }

    [Fact]
    public async Task StaticEfToListAsync_ReportsDiagnostic()
    {
        var source = EfStub + """

            public sealed class FooRepository
            {
                public object Get() => EntityFrameworkQueryableExtensions.ToListAsync(
                    new[] { new Row() }.AsQueryable());
            }
            """;
        await Verifier<PRM004_NoToListOnLargeTablesAnalyzer>.VerifyAsync(
            "FooRepository.cs", source, ExpectMethod(source, "FooRepository.cs", "ToListAsync"));
    }

    [Fact]
    public async Task HomonymousNonEfToListAsync_DoesNotReportDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            public sealed class Q
            {
                public Task<List<int>> ToListAsync() => Task.FromResult(new List<int>());
            }
            public sealed class FooRepository
            {
                public object Get() => new Q().ToListAsync();
            }
            """;
        await Verifier<PRM004_NoToListOnLargeTablesAnalyzer>.VerifyAsync("FooRepository.cs", source);
    }

    [Fact]
    public async Task UnguardedEfMaterialization_InPartialRepositoryFile_ReportsDiagnostic()
    {
        var source = Query(".ToListAsync()", "PipelineRepository");
        await Verifier<PRM004_NoToListOnLargeTablesAnalyzer>.VerifyAsync(
            "PipelineRepository.Coverage.cs", source,
            ExpectMethod(source, "PipelineRepository.Coverage.cs", "ToListAsync"));
    }

    [Fact]
    public async Task EfMaterialization_InService_NoDiagnostic()
    {
        var source = Query(".ToListAsync()", "FooService");
        await Verifier<PRM004_NoToListOnLargeTablesAnalyzer>.VerifyAsync("FooService.cs", source);
    }

    private static string Query(string chain, string className = "FooRepository") => EfStub + $$"""

        public sealed class {{className}}
        {
            public object Get() => new[] { new Row() }.AsQueryable(){{chain}};
        }
        """;

    private static DiagnosticResult ExpectMethod(string source, string filePath, string method)
    {
        var marker = "." + method;
        var dotIndex = source.LastIndexOf(marker, StringComparison.Ordinal);
        if (dotIndex < 0)
            dotIndex = source.LastIndexOf("Extensions." + method, StringComparison.Ordinal) + "Extensions".Length;
        var prefix = source[..(dotIndex + 1)];
        var line = prefix.Count(character => character == '\n') + 1;
        var lastLineBreak = source.LastIndexOf('\n', dotIndex);
        var column = dotIndex - lastLineBreak + 1;
        return Verifier<PRM004_NoToListOnLargeTablesAnalyzer>.Expect(
            "PRM004", DiagnosticSeverity.Info, filePath, line, column, line, column + method.Length, method);
    }
}
