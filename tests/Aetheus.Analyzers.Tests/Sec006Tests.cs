// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Analyzers.Tests;

/// <summary>
/// SEC006 reports an HttpClient built with <c>new</c> where it is used, and accepts the two shapes
/// that own the client's lifetime: a static initializer and a DI registration lambda.
/// </summary>
public sealed class Sec006Tests
{
    private const string DependencyInjectionStub = """
        namespace Microsoft.Extensions.DependencyInjection
        {
            public interface IServiceCollection { }
            public static class ServiceCollectionServiceExtensions
            {
                public static IServiceCollection AddSingleton<T>(
                    this IServiceCollection services, System.Func<System.IServiceProvider, T> factory) => services;
            }
        }
        """;

    private static Task VerifyAsync(string source) =>
        MarkupVerifier<SEC006_HttpClientComesFromTheFactoryAnalyzer>.VerifyAsync(source, DependencyInjectionStub);

    [Fact]
    public async Task AClientBuiltPerCallIsReported()
    {
        await VerifyAsync("""
            using System;
            using System.Net.Http;
            using System.Threading.Tasks;
            public sealed class StatusChecker
            {
                public async Task<bool> CheckAsync(Uri target)
                {
                    using var client = {|SEC006:new HttpClient { Timeout = TimeSpan.FromSeconds(5) }|};
                    using var response = await client.GetAsync(target);
                    return response.IsSuccessStatusCode;
                }
            }
            """);
    }

    [Fact]
    public async Task ATargetTypedConstructionIsReported()
    {
        await VerifyAsync("""
            using System.Net.Http;
            public sealed class Holder
            {
                private readonly HttpClient _client = {|SEC006:new()|};
            }
            """);
    }

    [Fact]
    public async Task AStaticInstanceAndADependencyInjectionRegistrationAreNotReported()
    {
        await VerifyAsync("""
            using System;
            using System.Net.Http;
            using Microsoft.Extensions.DependencyInjection;
            public static class Registration
            {
                private static readonly HttpClient Shared = new(new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
                }) { Timeout = TimeSpan.FromSeconds(10) };

                public static void Register(IServiceCollection services) =>
                    services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
            }
            """);
    }

    [Fact]
    public async Task KnownFalsePositive_AOneShotProcessBuildingItsOnlyClientIsStillReported()
    {
        // Documented false positive: a CLI builds one client for its whole lifetime and has no DI
        // container. It cannot exhaust anything, but the rule cannot tell a process's only client
        // from a per-request one; the code carries a SuppressMessage that says so.
        await VerifyAsync("""
            using System;
            using System.Net.Http;
            public static class Program
            {
                public static int Main()
                {
                    using var client = {|SEC006:new HttpClient { Timeout = TimeSpan.FromSeconds(30) }|};
                    return 0;
                }
            }
            """);
    }
}
