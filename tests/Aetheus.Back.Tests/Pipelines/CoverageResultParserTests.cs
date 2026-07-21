// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Back.Components.Pipelines;
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Tests.Pipelines;

public class CoverageResultParserTests
{
    [Fact]
    public void Parse_WithClasses_AggregatesPerFileCoverageWorstFirst()
    {
        // A.cs spans two classes (partial/nested) - each physical line must count only once.
        var xml = """
            <coverage line-rate="0.6" lines-covered="3" lines-valid="5">
              <packages><package name="P"><classes>
                <class filename="src/A.cs" name="A">
                  <lines><line number="1" hits="1" /><line number="2" hits="0" /></lines>
                </class>
                <class filename="src/A.cs" name="A.Nested">
                  <lines><line number="1" hits="1" /><line number="3" hits="1" /></lines>
                </class>
                <class filename="src/B.cs" name="B">
                  <lines><line number="1" hits="0" /><line number="2" hits="0" /></lines>
                </class>
              </classes></package></packages>
            </coverage>
            """;

        var result = CoverageResultParser.Parse(xml, 1, null, null);

        Assert.NotNull(result);
        Assert.NotNull(result!.FilesJson);
        var files = JsonSerializer.Deserialize<List<CoverageFileDto>>(result.FilesJson!)!;
        Assert.Equal(2, files.Count);
        // Worst coverage first.
        Assert.Equal("src/B.cs", files[0].File);
        Assert.Equal("P", files[0].Assembly);
        Assert.Equal(0, files[0].LinesCovered);
        Assert.Equal(2, files[0].LinesValid);
        Assert.Equal(0.0, files[0].LineRate, 3);
        Assert.Equal("src/A.cs", files[1].File);
        Assert.Equal("P", files[1].Assembly);
        Assert.Equal(2, files[1].LinesCovered);
        Assert.Equal(3, files[1].LinesValid);
        Assert.Equal(2.0 / 3.0, files[1].LineRate, 3);
    }

    [Fact]
    public void Parse_NoClasses_LeavesFilesJsonNull()
    {
        var xml = """<coverage line-rate="0.5"><packages /></coverage>""";

        var result = CoverageResultParser.Parse(xml, 1, null, null);

        Assert.NotNull(result);
        Assert.Null(result!.FilesJson);
    }

    [Fact]
    public void Parse_ValidCobertura_ExtractsRates()
    {
        var xml = """
            <coverage version="2.0" line-rate="0.852" branch-rate="0.721"
                      lines-covered="340" lines-valid="400"
                      branches-covered="78" branches-valid="108">
              <packages />
            </coverage>
            """;

        var result = CoverageResultParser.Parse(xml, 1, "build", "coverage");

        Assert.NotNull(result);
        Assert.Equal(1, result!.PipelineRunId);
        Assert.Equal("build", result.StageName);
        Assert.Equal("coverage", result.StepName);
        Assert.Equal(0.852, result.LineRate, 3);
        Assert.Equal(0.721, result.BranchRate, 3);
        Assert.Equal(340, result.LinesCovered);
        Assert.Equal(400, result.LinesValid);
        Assert.Equal(78, result.BranchesCovered);
        Assert.Equal(108, result.BranchesValid);
    }

    [Fact]
    public void Parse_ReportGeneratorCoberturaWithStandardDoctype_ExtractsRates()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE coverage SYSTEM "http://cobertura.sourceforge.net/xml/coverage-04.dtd">
            <coverage line-rate="0.75" branch-rate="0.5"
                      lines-covered="75" lines-valid="100"
                      branches-covered="5" branches-valid="10">
              <packages />
            </coverage>
            """;

        var result = CoverageResultParser.Parse(xml, 1, "Coverage", "Publish Coverage");

        Assert.NotNull(result);
        Assert.Equal(0.75, result!.LineRate, 3);
        Assert.Equal(100, result.LinesValid);
    }

    [Fact]
    public void Parse_DoctypeEntityReference_DoesNotResolveOrExpandEntity()
    {
        var xml = """
            <!DOCTYPE coverage [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
            <coverage line-rate="1"><packages><package name="&xxe;" /></packages></coverage>
            """;

        var result = CoverageResultParser.Parse(xml, 1, null, null);

        Assert.Null(result);
    }

    [Fact]
    public void Parse_MissingAttributes_DefaultsToZero()
    {
        var xml = """<coverage line-rate="0.5"><packages /></coverage>""";

        var result = CoverageResultParser.Parse(xml, 1, null, null);

        Assert.NotNull(result);
        Assert.Equal(0.5, result!.LineRate, 3);
        Assert.Equal(0, result.BranchRate);
        Assert.Equal(0, result.LinesCovered);
    }

    [Fact]
    public void Parse_NotCoverageRoot_ReturnsNull()
    {
        var xml = """<testsuites><testsuite name="foo" /></testsuites>""";

        var result = CoverageResultParser.Parse(xml, 1, null, null);

        Assert.Null(result);
    }

    [Fact]
    public void Parse_MalformedXml_ReturnsNull()
    {
        var result = CoverageResultParser.Parse("not xml at all {{}", 1, null, null);

        Assert.Null(result);
    }

    [Fact]
    public void Parse_ReportGeneratorCoberturaDocumentType_IsIgnoredWithoutResolution()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE coverage SYSTEM "http://cobertura.sourceforge.net/xml/coverage-04.dtd">
            <coverage line-rate="0.75" lines-covered="3" lines-valid="4"><packages /></coverage>
            """;

        var result = CoverageResultParser.Parse(xml, 1, "Coverage", "Cobertura.xml");

        Assert.NotNull(result);
        Assert.Equal(0.75, result!.LineRate, 3);
    }

    [Fact]
    public void Parse_DocumentTypeWithExternalEntity_RemainsRejected()
    {
        var xml = """
            <!DOCTYPE coverage [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
            <coverage line-rate="0.75" lines-covered="3" lines-valid="4">&xxe;</coverage>
            """;

        Assert.Null(CoverageResultParser.Parse(xml, 1, null, null));
    }
}
