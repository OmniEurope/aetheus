// SPDX-License-Identifier: EUPL-1.2
using System.Xml.Linq;
using Aetheus.Agent.Core.Operations;

namespace Aetheus.Agent.Core.Tests;

public sealed class PipelineCoveragePublisherTests
{
    [Fact]
    public void TryNormalizeCoverage_ConvertsJacocoToCobertura()
    {
        const string jacoco = """
            <report name="demo">
              <package name="com/example">
                <sourcefile name="Demo.java">
                  <line nr="10" mi="0" ci="2" />
                  <line nr="11" mi="3" ci="0" />
                </sourcefile>
              </package>
              <counter type="LINE" missed="1" covered="1" />
            </report>
            """;

        var valid = PipelineCoveragePublisher.TryNormalizeCoverage(jacoco, out var cobertura,
            out var covered, out var lines);

        Assert.True(valid);
        Assert.Equal(1, covered);
        Assert.Equal(2, lines);
        var root = XDocument.Parse(cobertura).Root;
        Assert.Equal("coverage", root?.Name.LocalName);
        Assert.Contains(root!.Descendants("class"), item =>
            item.Attribute("filename")?.Value == "com/example/Demo.java");
        Assert.Equal(["1", "0"], root.Descendants("line").Select(line => line.Attribute("hits")?.Value));
    }

    [Fact]
    public void TryNormalizeCoverage_RejectsExternalEntityDeclaration()
    {
        const string hostile = """<!DOCTYPE report [<!ENTITY xxe SYSTEM "file:///etc/passwd">]><report name="&xxe;"><counter type="LINE" missed="0" covered="1" /></report>""";

        Assert.False(PipelineCoveragePublisher.TryNormalizeCoverage(hostile, out _, out _, out _));
    }

    [Fact]
    public void TryNormalizeCoverage_AcceptsStandardNycCoberturaDoctypeWithoutResolvingIt()
    {
        const string nyc = """
            <?xml version="1.0" ?>
            <!DOCTYPE coverage SYSTEM "http://cobertura.sourceforge.net/xml/coverage-04.dtd">
            <coverage line-rate="0.2692" lines-covered="49" lines-valid="182">
              <packages />
            </coverage>
            """;

        var valid = PipelineCoveragePublisher.TryNormalizeCoverage(
            nyc, out _, out var covered, out var lines);

        Assert.True(valid);
        Assert.Equal(49, covered);
        Assert.Equal(182, lines);
    }
}
