// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Extensions;
using Aetheus.Agent.Core.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Core.Tests;

public class AgentCoreServiceCollectionExtensionsTests
{
    private static IConfiguration BuildConfig(string serverUrl, int max = 4, string? workDir = null)
    {
        var dict = new Dictionary<string, string?>
        {
            ["Aetheus:ServerUrl"] = serverUrl,
            ["Aetheus:MaxConcurrentTasks"] = max.ToString(),
            ["Aetheus:WorkDirectory"] = workDir ?? string.Empty
        };
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void AddAgentCore_ValidHttpsUrl_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfig("https://aetheus.example.com");

        services.AddAgentCore(config);

        Assert.Contains(services, d => d.ServiceType == typeof(AgentState));
        Assert.Contains(services, d => d.ServiceType == typeof(PluginLoader));
        Assert.Contains(services, d => d.ServiceType == typeof(IPluginLoader));
    }

    [Fact]
    public void AddAgentCore_LoopbackHttp_Allowed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfig("http://localhost:5300");

        var ex = Record.Exception(() => services.AddAgentCore(config));
        Assert.Null(ex);
    }

    [Fact]
    public void AddAgentCore_NonLoopbackHttp_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfig("http://example.com");

        Assert.Throws<InvalidOperationException>(() => services.AddAgentCore(config));
    }

    [Fact]
    public void AddAgentCore_InvalidUrl_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfig("not-a-url");

        Assert.Throws<InvalidOperationException>(() => services.AddAgentCore(config));
    }

    [Fact]
    public void AddAgentCore_MaxConcurrentTasksAboveMaximum_FailsOptionsValidation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfig("https://example.com", max: 100);

        services.AddAgentCore(config);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AetheusAgentOptions>>();
        var exception = Assert.Throws<OptionsValidationException>(() => options.Value);
        Assert.Contains(nameof(AetheusAgentOptions.MaxConcurrentTasks), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddAgentCore_WorkDirectory_DefaultsForPlatform()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfig("https://example.com");

        services.AddAgentCore(config);

        var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AetheusAgentOptions>>().Value;
        Assert.False(string.IsNullOrEmpty(opts.WorkDirectory));
        Assert.False(string.IsNullOrEmpty(opts.PluginDirectory));
    }

    [Theory]
    [InlineData("Aetheus:PollingIntervalSeconds", "0", "PollingIntervalSeconds")]
    [InlineData("Aetheus:HeartbeatIntervalSeconds", "3601", "HeartbeatIntervalSeconds")]
    [InlineData("Aetheus:MinTimeoutSeconds", "100", "MaxTimeoutSeconds")]
    public void AddAgentCore_InvalidRuntimeOption_FailsOptionsValidation(string key, string value, string expectedFailure)
    {
        var values = new Dictionary<string, string?>
        {
            ["Aetheus:ServerUrl"] = "https://example.com",
            ["Aetheus:WorkDirectory"] = "work",
            [key] = value
        };
        if (key == "Aetheus:MinTimeoutSeconds")
            values["Aetheus:MaxTimeoutSeconds"] = "50";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAgentCore(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<AetheusAgentOptions>>();
        var exception = Assert.Throws<OptionsValidationException>(() => options.Value);
        Assert.Contains(expectedFailure, exception.Message, StringComparison.Ordinal);
    }
}
