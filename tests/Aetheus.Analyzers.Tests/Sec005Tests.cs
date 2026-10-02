// SPDX-License-Identifier: EUPL-1.2
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace Aetheus.Analyzers.Tests;

/// <summary>
/// SEC005 follows the short, certain case: a request-bound parameter used in a sink in the same
/// method. It must fire there and stay quiet everywhere else, including on the parameterised APIs
/// that are the correct answer.
/// </summary>
public sealed class Sec005Tests
{
    private const string Stub = """
        using System;
        using System.Threading.Tasks;
        namespace Microsoft.AspNetCore.Mvc
        {
            public abstract class ControllerBase { }
            public sealed class FromQueryAttribute : Attribute { }
            public sealed class FromRouteAttribute : Attribute { }
            public sealed class FromBodyAttribute : Attribute { }
        }
        namespace System.Diagnostics
        {
            public sealed class Process { public static Process Start(string fileName) => null!; }
        }
        namespace Microsoft.EntityFrameworkCore
        {
            public class DbSet<T> { }
            public static class RelationalQueryableExtensions
            {
                public static DbSet<T> FromSqlRaw<T>(this DbSet<T> source, string sql) => source;
                public static DbSet<T> FromSqlInterpolated<T>(this DbSet<T> source, FormattableString sql) => source;
            }
        }
        public sealed class Row { }
        """;

    private static string Source(string body) => $$"""
        using System;
        using System.IO;
        using System.Diagnostics;
        using Microsoft.AspNetCore.Mvc;
        using Microsoft.EntityFrameworkCore;
        {{Stub}}
        public sealed class ArtifactsController : ControllerBase
        {
            private readonly DbSet<Row> _rows = new();
        {{body}}
        }
        """;

    private static Task VerifyAsync(string body, params DiagnosticResult[] expected) =>
        Verifier<SEC005_NoRequestDataInDangerousSinkAnalyzer>.VerifyAsync("Controller.cs", Source(body), expected);

    private static DiagnosticResult At(int line, int column, int endColumn, string name, string sink) =>
        Verifier<SEC005_NoRequestDataInDangerousSinkAnalyzer>.Expect(
            "SEC005", DiagnosticSeverity.Warning, "Controller.cs", line, column, line, endColumn, name, sink);

    [Fact]
    public async Task ARouteValueReachingAPathIsReported()
    {
        await VerifyAsync(
            """    public string Read([FromRoute] string slug) => Path.Combine("/data", slug);""",
            At(32, 74, 78, "slug", "a file path"));
    }

    [Fact]
    public async Task AQueryValueReachingRawSqlIsReported()
    {
        await VerifyAsync(
            """    public object Search([FromQuery] string filter) => _rows.FromSqlRaw(filter);""",
            At(32, 73, 79, "filter", "raw SQL"));
    }

    [Fact]
    public async Task ARequestValueReachingAProcessIsReported()
    {
        await VerifyAsync(
            """    public object Run([FromQuery] string tool) => Process.Start(tool);""",
            At(32, 65, 69, "tool", "a process"));
    }

    [Fact]
    public async Task AnUnattributedStringOnAnActionIsStillRequestData()
    {
        // ASP.NET binds a plain string parameter from the route or the query with no attribute at
        // all, which is exactly the case a reader forgets.
        await VerifyAsync(
            """    public string Read(string slug) => Path.Combine("/data", slug);""",
            At(32, 62, 66, "slug", "a file path"));
    }

    [Fact]
    public async Task TheParameterisedApiIsNotASink()
    {
        // FromSqlInterpolated is parameterised by the compiler and is the correct answer.
        await VerifyAsync(
            """    public object Search([FromQuery] string filter) => _rows.FromSqlInterpolated($"a {filter}");""");
    }

    [Fact]
    public async Task AConstantReachingASinkIsNotReported()
    {
        await VerifyAsync(
            """    public string Read([FromRoute] int id) => Path.Combine("/data", "fixed");""");
    }

    [Fact]
    public async Task AMethodWithNoRequestParameterIsNotReported()
    {
        await VerifyAsync(
            """
                private string _root = "/data";
                private string Read() => Path.Combine(_root, "x");
            """);
    }
}
