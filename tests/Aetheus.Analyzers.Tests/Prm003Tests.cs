// SPDX-License-Identifier: EUPL-1.2
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace Aetheus.Analyzers.Tests;

public sealed class Prm003Tests
{
    private const string EfStub = """
        using System;
        using System.Linq;
        using System.Linq.Expressions;
        using Microsoft.EntityFrameworkCore;
        namespace Microsoft.EntityFrameworkCore
        {
            public static class EntityFrameworkQueryableExtensions
            {
                public static IQueryable<T> Include<T, TProperty>(
                    this IQueryable<T> source, Expression<Func<T, TProperty>> path) => source;
            }
        }
        public sealed class Row { public int Id { get; set; } }
        """;

    [Theory]
    [InlineData("OrderBy(x => x.Id)", "OrderBy")]
    [InlineData("OrderByDescending(x => x.Id)", "OrderByDescending")]
    [InlineData("OrderBy(x => x.Id).ThenBy(x => x.Id)", "ThenBy")]
    [InlineData("OrderBy(x => x.Id).ThenByDescending(x => x.Id)", "ThenByDescending")]
    [InlineData("Skip(10)", "Skip")]
    [InlineData("Take(10)", "Take")]
    public async Task IncludeAfterLinqOrderingOrPagination_ReportsDiagnostic(string chain, string method)
    {
        var source = Query($".{chain}.Include(x => x.Id)");

        await Verifier<PRM003_NoIncludeAfterOrderByAnalyzer>.VerifyAsync(
            "FooRepository.cs", source, ExpectInclude(source, "FooRepository.cs", method));
    }

    [Fact]
    public async Task IncludeBeforeOrderBy_NoDiagnostic()
    {
        var source = Query(".Include(x => x.Id).OrderBy(x => x.Id)");
        await Verifier<PRM003_NoIncludeAfterOrderByAnalyzer>.VerifyAsync("FooRepository.cs", source);
    }

    [Fact]
    public async Task IncludeBeforeWhere_NoDiagnostic()
    {
        var source = Query(".Include(x => x.Id).Where(x => x.Id > 0)");
        await Verifier<PRM003_NoIncludeAfterOrderByAnalyzer>.VerifyAsync("FooRepository.cs", source);
    }

    [Fact]
    public async Task OrderingSplitAcrossLocalDeclaration_ReportsDiagnostic()
    {
        var source = EfStub + """

            public sealed class FooRepository
            {
                public IQueryable<Row> Get()
                {
                    var query = new[] { new Row() }.AsQueryable().OrderBy(row => row.Id);
                    var filtered = query.Where(row => row.Id > 0);
                    return filtered.Include(row => row.Id);
                }
            }
            """;

        await Verifier<PRM003_NoIncludeAfterOrderByAnalyzer>.VerifyAsync(
            "FooRepository.cs", source, ExpectInclude(source, "FooRepository.cs", "OrderBy"));
    }

    [Fact]
    public async Task OrderingSplitAcrossLocalAssignment_ReportsDiagnostic()
    {
        var source = EfStub + """

            public sealed class FooRepository
            {
                public IQueryable<Row> Get()
                {
                    var query = new[] { new Row() }.AsQueryable();
                    query = query.OrderBy(row => row.Id);
                    return query.Include(row => row.Id);
                }
            }
            """;

        await Verifier<PRM003_NoIncludeAfterOrderByAnalyzer>.VerifyAsync(
            "FooRepository.cs", source, ExpectInclude(source, "FooRepository.cs", "OrderBy"));
    }

    [Fact]
    public async Task StaticEfIncludeAfterTake_ReportsDiagnostic()
    {
        var source = EfStub + """

            public sealed class FooRepository
            {
                public IQueryable<Row> Get() => EntityFrameworkQueryableExtensions.Include(
                    new[] { new Row() }.AsQueryable().Take(10), x => x.Id);
            }
            """;

        await Verifier<PRM003_NoIncludeAfterOrderByAnalyzer>.VerifyAsync(
            "StaticRepository.cs", source, ExpectInclude(source, "StaticRepository.cs", "Take"));
    }

    [Fact]
    public async Task UnrelatedIncludeMethod_DoesNotReportDiagnostic()
    {
        const string source = """
            using System;
            public sealed class Q
            {
                public Q OrderBy(Func<int, int> f) => this;
                public Q Include(Func<int, int> f) => this;
            }
            public sealed class Foo
            {
                public Q Get() => new Q().OrderBy(x => x).Include(x => x);
            }
            """;

        await Verifier<PRM003_NoIncludeAfterOrderByAnalyzer>.VerifyAsync("Unrelated.cs", source);
    }

    [Fact]
    public async Task HomonymousNonLinqOrderByBeforeEfInclude_DoesNotReportDiagnostic()
    {
        var source = EfStub + """

            public sealed class FakeQuery
            {
                public IQueryable<Row> OrderBy(Func<Row, int> selector) => Array.Empty<Row>().AsQueryable();
            }
            public sealed class FooRepository
            {
                public IQueryable<Row> Get() => new FakeQuery().OrderBy(x => x.Id).Include(x => x.Id);
            }
            """;

        await Verifier<PRM003_NoIncludeAfterOrderByAnalyzer>.VerifyAsync("FooRepository.cs", source);
    }

    private static string Query(string chain) => EfStub + $$"""

        public sealed class FooRepository
        {
            public IQueryable<Row> Get() => new[] { new Row() }.AsQueryable(){{chain}};
        }
        """;

    private static DiagnosticResult ExpectInclude(string source, string filePath, string method)
    {
        var dotIndex = source.LastIndexOf(".Include", StringComparison.Ordinal);
        if (dotIndex < 0)
            dotIndex = source.LastIndexOf("Extensions.Include", StringComparison.Ordinal) + "Extensions".Length;
        var prefix = source[..(dotIndex + 1)];
        var line = prefix.Count(character => character == '\n') + 1;
        var lastLineBreak = source.LastIndexOf('\n', dotIndex);
        var column = dotIndex - lastLineBreak + 1;
        return Verifier<PRM003_NoIncludeAfterOrderByAnalyzer>.Expect(
            "PRM003", DiagnosticSeverity.Warning, filePath, line, column, line, column + "Include".Length, method);
    }
}
