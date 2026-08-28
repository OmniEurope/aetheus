// SPDX-License-Identifier: EUPL-1.2
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace Aetheus.Analyzers.Tests;

public sealed class Prm001Tests
{
    // The DateTime.UtcNow member access sits on line 7, starting at column 24
    // ("        var x = System.DateTime.UtcNow;" -> wait, we use plain DateTime).
    // Layout is fixed below; the access spans "DateTime.UtcNow".
    private const string UtcNowSource = """
using System;
namespace Demo;
public class Foo
{
    public DateTime Get()
    {
        return DateTime.UtcNow;
    }
}
""";

    private const string TimeProviderSource = """
using System;
public class Foo
{
    private readonly TimeProvider _tp = TimeProvider.System;
    public DateTime Get()
    {
        return _tp.GetUtcNow().UtcDateTime;
    }
}
""";

    [Fact]
    public async Task NormalPath_DateTimeUtcNow_ReportsDiagnostic()
    {
        // "        return DateTime.UtcNow;" - DateTime.UtcNow spans columns 16-31 on line 7.
        var expected = Verifier<PRM001_NoDateTimeUtcNowAnalyzer>.Expect(
            "PRM001", DiagnosticSeverity.Warning, "Foo.cs", 7, 16, 7, 31, "DateTime.UtcNow");

        await Verifier<PRM001_NoDateTimeUtcNowAnalyzer>.VerifyAsync("Foo.cs", UtcNowSource, expected);
    }

    [Fact]
    public async Task UnrelatedClassInMigrationsFolder_DateTimeUtcNow_ReportsDiagnostic()
    {
        var expected = Verifier<PRM001_NoDateTimeUtcNowAnalyzer>.Expect(
            "PRM001", DiagnosticSeverity.Warning, "Data/Migrations/20240101_Init.cs",
            7, 16, 7, 31, "DateTime.UtcNow");
        await Verifier<PRM001_NoDateTimeUtcNowAnalyzer>.VerifyAsync(
            "Data/Migrations/20240101_Init.cs", UtcNowSource, expected);
    }

    [Fact]
    public async Task EfMigrationClass_DateTimeUtcNow_NoDiagnostic()
    {
        const string source = """
using System;
namespace Microsoft.EntityFrameworkCore.Migrations { public abstract class Migration { } }
namespace Demo
{
    public sealed class RealMigration : Microsoft.EntityFrameworkCore.Migrations.Migration
    {
        public DateTime Get() => DateTime.UtcNow;
    }
}
""";

        await Verifier<PRM001_NoDateTimeUtcNowAnalyzer>.VerifyAsync(
            "Data/Migrations/20240101_Real.cs", source);
    }

    [Fact]
    public async Task MigrationsHelperFile_NotInMigrationsFolder_ReportsDiagnostic()
    {
        // Filename contains "Migrations" but the file is NOT inside a Migrations directory.
        var expected = Verifier<PRM001_NoDateTimeUtcNowAnalyzer>.Expect(
            "PRM001", DiagnosticSeverity.Warning, "Services/MigrationsHelper.cs",
            7, 16, 7, 31, "DateTime.UtcNow");

        await Verifier<PRM001_NoDateTimeUtcNowAnalyzer>.VerifyAsync(
            "Services/MigrationsHelper.cs", UtcNowSource, expected);
    }

    [Fact]
    public async Task TimeProviderUsage_NoDiagnostic()
    {
        await Verifier<PRM001_NoDateTimeUtcNowAnalyzer>.VerifyAsync("Foo.cs", TimeProviderSource);
    }

    [Fact]
    public async Task Alias_DateTimeUtcNow_ReportsDiagnostic()
    {
        const string source = """
using Clock = System.DateTime;
namespace Demo;
public class Foo
{
    public Clock Get()
    {
        return Clock.UtcNow;
    }
}
""";
        var expected = Verifier<PRM001_NoDateTimeUtcNowAnalyzer>.Expect(
            "PRM001", DiagnosticSeverity.Warning, "Alias.cs", 7, 16, 7, 28, "DateTime.UtcNow");

        await Verifier<PRM001_NoDateTimeUtcNowAnalyzer>.VerifyAsync("Alias.cs", source, expected);
    }

    [Fact]
    public async Task GlobalQualified_DateTimeNow_ReportsDiagnostic()
    {
        const string source = """
using System;
namespace Demo;
public class Foo
{
    public DateTime Get()
    {
        return global::System.DateTime.Now;
    }
}
""";
        var expected = Verifier<PRM001_NoDateTimeUtcNowAnalyzer>.Expect(
            "PRM001", DiagnosticSeverity.Warning, "Global.cs", 7, 16, 7, 43, "DateTime.Now");

        await Verifier<PRM001_NoDateTimeUtcNowAnalyzer>.VerifyAsync("Global.cs", source, expected);
    }

    [Theory]
    [InlineData("DateTime.Now", "DateTime.Now")]
    [InlineData("System.DateTime.UtcNow", "DateTime.UtcNow")]
    [InlineData("global::System.DateTime.UtcNow", "DateTime.UtcNow")]
    public async Task DateTimeClockVariants_ReportDiagnostic(string expression, string diagnosticArgument)
    {
        var source = $$"""
            using System;
            public class Foo
            {
                public DateTime Get() => {{expression}};
            }
            """;
        var startColumn = source.Split('\n')[3].IndexOf(expression, StringComparison.Ordinal) + 1;
        var expected = Verifier<PRM001_NoDateTimeUtcNowAnalyzer>.Expect(
            "PRM001", DiagnosticSeverity.Warning, "Variants.cs",
            4, startColumn, 4, startColumn + expression.Length, diagnosticArgument);

        await Verifier<PRM001_NoDateTimeUtcNowAnalyzer>.VerifyAsync("Variants.cs", source, expected);
    }

    [Theory]
    [InlineData("DateTime.Today", "DateTime.Today")]
    [InlineData("DateTimeOffset.Now", "DateTimeOffset.Now")]
    [InlineData("DateTimeOffset.UtcNow", "DateTimeOffset.UtcNow")]
    public async Task AdditionalAmbientClockVariants_ReportDiagnostic(
        string expression,
        string diagnosticArgument)
    {
        var source = $$"""
            using System;
            public class Foo
            {
                public object Get() => {{expression}};
            }
            """;
        var startColumn = source.Split('\n')[3].IndexOf(expression, StringComparison.Ordinal) + 1;
        var expected = Verifier<PRM001_NoDateTimeUtcNowAnalyzer>.Expect(
            "PRM001", DiagnosticSeverity.Warning, "AdditionalVariants.cs",
            4, startColumn, 4, startColumn + expression.Length, diagnosticArgument);

        await Verifier<PRM001_NoDateTimeUtcNowAnalyzer>.VerifyAsync(
            "AdditionalVariants.cs", source, expected);
    }
}
