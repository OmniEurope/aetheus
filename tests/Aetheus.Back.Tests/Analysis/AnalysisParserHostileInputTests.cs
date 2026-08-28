// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Analysis;
using Aetheus.Back.Exceptions;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Tests.Analysis;

public sealed class AnalysisParserHostileInputTests
{
    [Fact]
    public void EveryJsonAdapterRejectsExcessiveDepth()
    {
        var hostile = DeepJson(70);

        Assert.Throws<BadRequestException>(() => SarifAnalysisParser.Parse(hostile, AnalysisCategory.Sast));
        Assert.Throws<BadRequestException>(() => CycloneDxParser.Parse(hostile, AnalysisReportFormat.CycloneDxJson));
        Assert.Throws<BadRequestException>(() => SpdxJsonParser.Parse(hostile));
        Assert.Throws<BadRequestException>(() => ZapAnalysisParser.Parse(hostile));
        Assert.Throws<BadRequestException>(() => EslintAnalysisParser.Parse(hostile));
        Assert.Throws<BadRequestException>(() => JscpdAnalysisParser.Parse(hostile));
        Assert.Throws<BadRequestException>(() => AnalysisMetricParser.Parse(hostile));
    }

    [Fact]
    public void SarifRejectsMoreThanMaximumFindingCount()
    {
        const string result = "{\"ruleId\":\"r\",\"message\":{\"text\":\"m\"}}";
        var json = "{\"runs\":[{\"tool\":{\"driver\":{\"name\":\"tool\"}},\"results\":["
            + string.Join(',', Enumerable.Repeat(result, 100_001)) + "]}]}";

        var exception = Assert.Throws<BadRequestException>(() =>
            SarifAnalysisParser.Parse(json, AnalysisCategory.Sast));

        Assert.Contains("maximum", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SbomAdaptersRejectMoreThanMaximumComponentCount()
    {
        const string component = "{\"name\":\"component\"}";
        var entries = string.Join(',', Enumerable.Repeat(component, 200_001));

        Assert.Throws<BadRequestException>(() => CycloneDxParser.Parse(
            "{\"components\":[" + entries + "]}", AnalysisReportFormat.CycloneDxJson));
        Assert.Throws<BadRequestException>(() => SpdxJsonParser.Parse(
            "{\"packages\":[" + entries + "]}"));
    }

    [Fact]
    public void MetricAdapterRejectsMoreThanMaximumMetricCount()
    {
        const string metric = "{\"key\":\"k\",\"value\":1,\"toolName\":\"tool\"}";
        var json = "{\"metrics\":[" + string.Join(',', Enumerable.Repeat(metric, 100_001)) + "]}";

        Assert.Throws<BadRequestException>(() => AnalysisMetricParser.Parse(json));
    }

    private static string DeepJson(int depth) =>
        string.Concat(Enumerable.Repeat("{\"nested\":", depth)) + "0" + new string('}', depth);
}
