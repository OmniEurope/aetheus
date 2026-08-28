// SPDX-License-Identifier: EUPL-1.2

using System.Text.RegularExpressions;

namespace Aetheus.Back.Tests.Architecture;

public sealed partial class NoPartialClassAuditTests
{
    [GeneratedRegex(
        @"^\s*(?:(?:public|internal|private|protected|file|static|sealed|abstract|readonly|ref|unsafe|new)\s+)*partial\s+(?:class|struct|record(?:\s+(?:class|struct))?|interface|void|Task|ValueTask)\b",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex PartialDeclarationRegex();

    [Fact]
    public void ProductionSources_UsePartialOnlyForFrameworkOrGeneratorRequirements()
    {
        var root = FindRepoRoot();
        var sourceRoot = Path.Combine(root, "src");
        var files = RepositoryScan.Enumerate(sourceRoot, "*.cs")
            .Where(path => !HasGeneratedSegment(path))
            .ToList();
        Assert.True(files.Count > 500, "The production-source scan is unexpectedly small.");

        var violations = files
            .Where(path => PartialDeclarationRegex().IsMatch(File.ReadAllText(path)))
            .Where(path => !IsRequiredPartial(path))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(violations.Count == 0,
            "Application types must not use partial declarations. Allowed cases are Razor code-behind, "
            + "EF-generated files, source generators, and the integration host shim:\n  "
            + string.Join("\n  ", violations));
    }

    private static bool IsRequiredPartial(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.Contains("/Data/Migrations/", StringComparison.Ordinal)
            || normalized.EndsWith(".Designer.cs", StringComparison.Ordinal))
            return true;

        if (normalized.EndsWith(".razor.cs", StringComparison.Ordinal)
            && File.Exists(path[..^3]))
            return true;

        var source = File.ReadAllText(path);
        if (normalized.EndsWith("/Aetheus.Back/Program.cs", StringComparison.Ordinal)
            && source.Contains("public partial class Program { }", StringComparison.Ordinal))
            return true;

        return source.Contains("[GeneratedRegex", StringComparison.Ordinal)
               || source.Contains("[JsonSerializable", StringComparison.Ordinal);
    }

    private static bool HasGeneratedSegment(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot() => Aetheus.Back.Tests.Architecture.RepositoryScan.Root;
}
