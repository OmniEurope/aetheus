// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Aetheus.Back.Tests.Security;

/// <summary>
/// PLAN-003 lot 14. The point of the endpoint is that a REAL browser can post to it, so it is
/// exercised through the whole pipeline with the exact content types browsers use. The default JSON
/// input formatter accepts neither <c>application/csp-report</c> (415) nor the Reporting API's array
/// body (400), which is why the controller reads and parses the body itself; these tests are what
/// keeps that from silently regressing to a model-bound DTO.
/// </summary>
public class CspReportEndpointIntegrationTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private const string LegacyBody = """
        {"csp-report":{"document-uri":"https://app.example/pipelines/runs/2281","blocked-uri":"inline","violated-directive":"script-src","source-file":"https://app.example/x.js","line-number":12,"script-sample":"alert(1)"}}
        """;

    private const string ReportingApiBody = """
        [{"type":"csp-violation","age":0,"url":"https://app.example/pipelines/runs/2281","body":{"documentURL":"https://app.example/pipelines/runs/2281","blockedURL":"inline","effectiveDirective":"script-src-elem","sourceFile":"https://app.example/x.js","lineNumber":12,"sample":"alert(1)"}}]
        """;

    [Theory]
    [InlineData("application/csp-report", LegacyBody)]      // report-uri, every browser
    [InlineData("application/reports+json", ReportingApiBody)] // report-to, Reporting API
    [InlineData("application/json", LegacyBody)]
    public async Task A_Report_Posted_The_Way_Browsers_Post_It_Is_Accepted(string contentType, string body)
    {
        using var response = await Post(contentType, body);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task A_Malformed_Report_Is_Still_Accepted_Silently()
    {
        using var response = await Post("application/csp-report", "{ not json");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task An_Oversized_Report_Is_Dropped_Unparsed()
    {
        // Only the first 8 KiB is ever read, and a read that fills the cap is dropped without being
        // parsed: an oversized body can neither be interpreted nor crash the endpoint.
        using var response = await Post("application/csp-report", new string('x', 64 * 1024));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task The_Response_Carries_The_Csp_Reporting_Directives()
    {
        using var response = await factory.CreateClient()
            .GetAsync("/health/live", TestContext.Current.CancellationToken);

        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("report-uri /api/security/csp-report", csp, StringComparison.Ordinal);
        Assert.Contains("report-to csp-endpoint", csp, StringComparison.Ordinal);

        var endpoints = Assert.Single(response.Headers.GetValues("Reporting-Endpoints"));
        Assert.Equal("csp-endpoint=\"/api/security/csp-report\"", endpoints);
    }

    private async Task<HttpResponseMessage> Post(string contentType, string body)
    {
        using var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return await factory.CreateClient().PostAsync(
            "/api/security/csp-report", content, TestContext.Current.CancellationToken);
    }
}
