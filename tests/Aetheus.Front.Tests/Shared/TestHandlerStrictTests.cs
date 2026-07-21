// SPDX-License-Identifier: EUPL-1.2
using System.Net;

namespace Aetheus.Front.Tests.Shared;

// S-TECH-H2VN: every unconfigured request is a test bug. The handler is strict by default so an
// unexpected API call cannot silently return an empty success and manufacture a false green.
public class TestHandlerStrictTests
{
    [Fact]
    public async Task Default_UnmatchedUrl_Throws()
    {
        var handler = new BunitTestHelper.TestHandler();
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("https://test/api/unmocked", Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Default_MatchedUrl_ReturnsConfiguredResponse()
    {
        var handler = new BunitTestHelper.TestHandler();
        handler.SetJsonResponse("api/widgets", new { count = 1 });
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://test/api/widgets", Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("count", await response.Content.ReadAsStringAsync(Xunit.TestContext.Current.CancellationToken));
    }
}
