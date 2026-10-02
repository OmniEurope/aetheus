// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Analyzers.Tests;

/// <summary>
/// SEC007 asks a directly built HttpClient to state its timeout, in the initializer or by an
/// assignment to where it was stored.
/// </summary>
public sealed class Sec007Tests
{
    private static Task VerifyAsync(string source) =>
        MarkupVerifier<SEC007_HttpClientDeclaresATimeoutAnalyzer>.VerifyAsync(source);

    [Fact]
    public async Task AClientWithTheDefaultTimeoutIsReported()
    {
        await VerifyAsync("""
            using System;
            using System.Net.Http;
            public sealed class Poller
            {
                private readonly HttpClient _client = {|SEC007:new HttpClient { BaseAddress = new Uri("https://example.test") }|};
            }
            """);
    }

    [Fact]
    public async Task ATimeoutInTheInitializerOrAssignedLaterIsNotReported()
    {
        await VerifyAsync("""
            using System;
            using System.Net.Http;
            public sealed class Poller
            {
                private readonly HttpClient _field = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                private readonly HttpClient _assignedInConstructor;

                public Poller(bool pooled, HttpMessageHandler handler)
                {
                    _assignedInConstructor = new HttpClient(handler);
                    _assignedInConstructor.Timeout = TimeSpan.FromSeconds(5);

                    using var local = pooled ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
                    local.Timeout = TimeSpan.FromSeconds(5);
                }
            }
            """);
    }

    [Fact]
    public async Task KnownFalsePositive_ATimeoutSetByTheCallerIsNotSeen()
    {
        // Documented false positive: the factory method returns the client and its caller sets the
        // timeout. The rule reads one member, not the call graph.
        await VerifyAsync("""
            using System;
            using System.Net.Http;
            public static class Clients
            {
                public static HttpClient Create() => {|SEC007:new HttpClient()|};
                public static HttpClient Configured()
                {
                    var client = Create();
                    client.Timeout = TimeSpan.FromSeconds(5);
                    return client;
                }
            }
            """);
    }
}
