// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Services;
using Microsoft.Extensions.Configuration;

namespace Aetheus.Agent.Core.Tests;

public sealed class AgentBackendProbeEntrypointTests
{
    [Fact]
    public async Task TryRunAsync_WithoutProbeArgument_DoesNotInvokeProbe()
    {
        var invoked = false;
        var result = await AgentBackendProbeEntrypoint.TryRunAsync(
            [], BuildConfiguration(), (_, _, _, _) =>
            {
                invoked = true;
                return Task.FromResult(0);
            }, TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.False(invoked);
    }

    [Fact]
    public async Task TryRunAsync_WithProbeArgument_UsesBoundConfigurationAndReturnsProbeExitCode()
    {
        string? observedUrl = null;
        string[]? observedArgs = null;
        bool? observedAllowInsecure = null;
        var args = new[] { "--probe-backend", "--timeout", "7" };

        var result = await AgentBackendProbeEntrypoint.TryRunAsync(
            args, BuildConfiguration(), (url, receivedArgs, allowInsecure, _) =>
            {
                observedUrl = url;
                observedArgs = receivedArgs;
                observedAllowInsecure = allowInsecure;
                return Task.FromResult(17);
            }, TestContext.Current.CancellationToken);

        Assert.Equal(17, result);
        Assert.Equal("https://backend.example", observedUrl);
        Assert.Same(args, observedArgs);
        Assert.True(observedAllowInsecure);
    }

    [Fact]
    public void LinuxAndWindowsPrograms_DelegateProbeModeToSharedEntrypoint()
    {
        var root = FindRepoRoot();
        foreach (var project in new[] { "Aetheus.Agent.Linux", "Aetheus.Agent.Windows" })
        {
            var program = File.ReadAllText(Path.Combine(root, "src", project, "Program.cs"));
            Assert.Contains("AgentBackendProbeEntrypoint.TryRunAsync(args, builder.Configuration)", program, StringComparison.Ordinal);
            Assert.DoesNotContain("BackendProbe.RunAsync", program, StringComparison.Ordinal);
        }
    }

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Aetheus:ServerUrl"] = "https://backend.example",
            ["Aetheus:AllowInsecureCerts"] = "true"
        }).Build();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(AgentBackendProbeEntrypointTests).Assembly.Location)!);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Aetheus.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root (Aetheus.slnx).");
    }
}
