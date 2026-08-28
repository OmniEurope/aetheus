// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Analysis;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Tests.Analysis;

public sealed class ZapAnalysisParserTests
{
    [Fact]
    public void Parse_NormalizesAlertsWithoutPersistingAttackOrEvidence()
    {
        const string json = """
            {"site":[{"alerts":[{"pluginid":"10020","alert":"Missing header","riskcode":"3","confidence":"2","desc":"<p>Header is absent</p>","cweid":"693","reference":"https://www.zaproxy.org/docs/alerts/10020/","instances":[{"uri":"https://qa.example.test/orders/42","method":"GET","param":"id","attack":"sensitive-payload","evidence":"sensitive-response"}]}]}]}
            """;

        var finding = Assert.Single(ZapAnalysisParser.Parse(json));

        Assert.Equal(AnalysisSeverity.High, finding.Severity);
        Assert.Equal(AnalysisCategory.Dast, finding.Category);
        Assert.Equal("/orders/42", finding.FilePath);
        Assert.DoesNotContain("sensitive", finding.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_DropsInformationalNoiseButKeepsLowAndAbove()
    {
        const string json = """
            {"site":[{"alerts":[
              {"pluginid":"100000","alert":"Client error","riskcode":"0","instances":[{"uri":"https://qa.example.test/a","method":"GET"}]},
              {"pluginid":"10027","alert":"Suspicious comment","riskcode":"1","instances":[{"uri":"https://qa.example.test/app.js","method":"GET"}]}
            ]}]}
            """;

        var finding = Assert.Single(ZapAnalysisParser.Parse(json));

        Assert.Equal("10027", finding.RuleId);
        Assert.Equal(AnalysisSeverity.Low, finding.Severity);
    }

    [Fact]
    public void Parse_DropsWeakTransportWarningOnlyForLoopbackHttpQa()
    {
        const string json = """
            {"site":[{"alerts":[{"pluginid":"10105","alert":"Weak Authentication Method","riskcode":"2","instances":[
              {"uri":"http://127.0.0.1:24001/api/packages","method":"GET"},
              {"uri":"https://qa.example.test/api/packages","method":"GET"}
            ]}]}]}
            """;

        var finding = Assert.Single(ZapAnalysisParser.Parse(json));

        Assert.Equal("/api/packages", finding.FilePath);
        Assert.Equal(AnalysisSeverity.Medium, finding.Severity);
    }
}
