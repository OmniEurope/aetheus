// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Analysis;
using Aetheus.Back.Exceptions;

namespace Aetheus.Back.Tests.Analysis;

public sealed class SarifAnalysisParserTests
{
    [Theory]
    // A container scanner reports the repository under its /src mount.
    [InlineData("file:///src/tests/security-rules/aetheus-security.py", "tests/security-rules/aetheus-security.py")]
    [InlineData("/src/src/Aetheus.Back/Program.cs", "src/Aetheus.Back/Program.cs")]
    // A path relative to the repository is already right, its own "src" folder included.
    [InlineData("src/Aetheus.Back/Program.cs", "src/Aetheus.Back/Program.cs")]
    [InlineData("deploy/scripts/run.sh", "deploy/scripts/run.sh")]
    public void R504_TheReportedPath_IsRelativeToTheRepository(string uri, string expected)
    {
        var sarif = $$"""
            {
              "version": "2.1.0",
              "runs": [{
                "tool": { "driver": { "name": "Ruff" } },
                "results": [{
                  "ruleId": "F821",
                  "message": { "text": "Undefined name" },
                  "locations": [{ "physicalLocation": { "artifactLocation": { "uri": "{{uri}}" }, "region": { "startLine": 8 } } }]
                }]
              }]
            }
            """;

        var finding = Assert.Single(SarifAnalysisParser.Parse(sarif, AnalysisCategory.CodeQuality));

        Assert.Equal(expected, finding.FilePath);
    }

    [Fact]
    public void Parse_ExtractsRuleLocationSeverityAndCwe()
    {
        var sarif = """
            {
              "version": "2.1.0",
              "runs": [{
                "tool": { "driver": {
                  "name": "OpenGrep",
                  "rules": [{
                    "id": "cs.sql-injection",
                    "shortDescription": { "text": "SQL injection" },
                    "helpUri": "https://example.test/rules/sql-injection",
                    "properties": { "tags": ["security", "CWE-89"], "security-severity": "9.2" }
                  }]
                }},
                "results": [{
                  "ruleId": "cs.sql-injection",
                  "message": { "text": "Untrusted SQL input" },
                  "locations": [{ "physicalLocation": {
                    "artifactLocation": { "uri": "file:///workspace/src/Query.cs" },
                    "region": { "startLine": 42, "endLine": 43 }
                  }}]
                }]
              }]
            }
            """;

        var finding = Assert.Single(SarifAnalysisParser.Parse(sarif, AnalysisCategory.Sast));

        Assert.Equal("OpenGrep", finding.ToolName);
        Assert.Equal("cs.sql-injection", finding.RuleId);
        Assert.Equal("SQL injection", finding.Title);
        Assert.Equal("CWE-89", finding.Cwe);
        Assert.Equal(AnalysisSeverity.Critical, finding.Severity);
        Assert.Equal("workspace/src/Query.cs", finding.FilePath);
        Assert.Equal(42, finding.StartLine);
        Assert.Equal(43, finding.EndLine);
        Assert.Equal(64, finding.Fingerprint.Length);
        Assert.Equal(64, finding.LocationHash.Length);
    }

    [Fact]
    public void Parse_SecretCategoryNeverPersistsReportedSecret()
    {
        const string secret = "super-secret-token-value";
        var sarif = $$"""
            { "runs": [{ "tool": { "driver": { "name": "Gitleaks" } }, "results": [{
              "ruleId": "generic-api-key",
              "level": "error",
              "message": { "text": "Leaked value {{secret}}" },
              "locations": [{ "physicalLocation": { "artifactLocation": { "uri": "config/app.env" }, "region": { "startLine": 3 } } }]
            }] }] }
            """;

        var finding = Assert.Single(SarifAnalysisParser.Parse(sarif, AnalysisCategory.Secrets));

        Assert.DoesNotContain(secret, finding.Message, StringComparison.Ordinal);
        Assert.Equal("Potential secret detected by rule generic-api-key.", finding.Message);
    }

    [Fact]
    public void Parse_SameRulePathAndSymbolKeepsFingerprintAcrossLineChanges()
    {
        var first = Sarif(10);
        var second = Sarif(120);

        var firstFinding = Assert.Single(SarifAnalysisParser.Parse(first, AnalysisCategory.CodeQuality));
        var secondFinding = Assert.Single(SarifAnalysisParser.Parse(second, AnalysisCategory.CodeQuality));

        Assert.Equal(firstFinding.Fingerprint, secondFinding.Fingerprint);
        Assert.NotEqual(firstFinding.LocationHash, secondFinding.LocationHash);
    }

    [Fact]
    public void Parse_SameSemanticFindingKeepsFingerprintAcrossFileMoves()
    {
        var first = Sarif(10, "src/old/app.ts");
        var moved = Sarif(10, "src/new/app.ts");

        var firstFinding = Assert.Single(SarifAnalysisParser.Parse(first, AnalysisCategory.CodeQuality));
        var movedFinding = Assert.Single(SarifAnalysisParser.Parse(moved, AnalysisCategory.CodeQuality));

        Assert.Equal(firstFinding.Fingerprint, movedFinding.Fingerprint);
        Assert.NotEqual(firstFinding.LocationHash, movedFinding.LocationHash);
    }

    [Fact]
    public void Parse_MalformedSarifFailsClosed()
    {
        var exception = Assert.Throws<BadRequestException>(() =>
            SarifAnalysisParser.Parse("not-json", AnalysisCategory.Sast));

        Assert.Contains("Invalid SARIF", exception.Message, StringComparison.Ordinal);
    }

    private static string Sarif(int line, string path = "src/app.ts") => $$"""
        { "runs": [{ "tool": { "driver": { "name": "ESLint" } }, "results": [{
          "ruleId": "complexity",
          "message": { "text": "Function is too complex" },
          "locations": [{
            "physicalLocation": { "artifactLocation": { "uri": "{{path}}" }, "region": { "startLine": {{line}} } },
            "logicalLocations": [{ "fullyQualifiedName": "renderPage" }]
          }]
        }] }] }
        """;
}
