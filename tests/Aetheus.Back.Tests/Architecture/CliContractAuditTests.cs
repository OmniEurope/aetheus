// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Constants;

namespace Aetheus.Back.Tests.Architecture;

public class CliContractAuditTests
{
    [Fact]
    public void LocalCliEndpoint_UsesTheBackendHttpsPort()
    {
        Assert.Equal("https://localhost:5301", LocalDevelopmentEndpoints.CliApiBaseUrl);
    }

    [Fact]
    public void CliKeepsTokensOutOfArgvAndRestrictsInsecureTransport()
    {
        var root = FindRepoRoot();
        var program = File.ReadAllText(Path.Combine(root, "src", "Aetheus.Cli", "Program.cs"));
        var client = File.ReadAllText(Path.Combine(root, "src", "Aetheus.Cli", "AetheusApiClient.cs"));
        var runtime = File.ReadAllText(Path.Combine(root, "src", "Aetheus.Cli", "CliRuntime.cs"));

        Assert.DoesNotContain("new(\"--token\")", program, StringComparison.Ordinal);
        Assert.Contains("new(\"--token-stdin\")", program, StringComparison.Ordinal);
        Assert.Contains("AETHEUS_TOKEN", runtime, StringComparison.Ordinal);
        Assert.Contains("IsPrivateIpLiteral", client, StringComparison.Ordinal);
        Assert.Contains("AETHEUS_INSECURE=1 is sending a bearer token over plaintext HTTP", client, StringComparison.Ordinal);
    }

    [Fact]
    public void CliNetworkActionsAreCancellableTimedAndReturnNonZeroOnFailure()
    {
        var root = FindRepoRoot();
        var program = File.ReadAllText(Path.Combine(root, "src", "Aetheus.Cli", "Program.cs"));
        var client = File.ReadAllText(Path.Combine(root, "src", "Aetheus.Cli", "AetheusApiClient.cs"));
        var runtime = File.ReadAllText(Path.Combine(root, "src", "Aetheus.Cli", "CliRuntime.cs"));

        Assert.Equal(5, Count(program, "CliRuntime.ExecuteAsync"));
        Assert.Equal(5, Count(program, "}, cancellationToken));"));
        Assert.Contains("TimeSpan.FromSeconds(30)", client, StringComparison.Ordinal);
        Assert.Contains("return 130;", runtime, StringComparison.Ordinal);
        Assert.Contains("return 1;", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void CliUsesValidatedCentralInvocationAndStableOutputContracts()
    {
        var root = FindRepoRoot();
        var program = File.ReadAllText(Path.Combine(root, "src", "Aetheus.Cli", "Program.cs"));
        var invocation = File.ReadAllText(Path.Combine(root, "src", "Aetheus.Cli", "CliInvocation.cs"));
        var runtime = File.ReadAllText(Path.Combine(root, "src", "Aetheus.Cli", "CliRuntime.cs"));
        var client = File.ReadAllText(Path.Combine(root, "src", "Aetheus.Cli", "AetheusApiClient.cs"));

        Assert.Contains("ResolveInvocationAsync", program, StringComparison.Ordinal);
        Assert.Contains("Uri.TryCreate", runtime, StringComparison.Ordinal);
        Assert.Contains("uri.UserInfo", runtime, StringComparison.Ordinal);
        Assert.Contains("MaximumPage = 1_000_000", invocation, StringComparison.Ordinal);
        Assert.Contains("ToString(\"O\", CultureInfo.InvariantCulture)", program, StringComparison.Ordinal);
        Assert.Contains("GetAsync(\"health/ready\", ct)", program, StringComparison.Ordinal);
        Assert.Contains("internal sealed class AetheusApiClient", client, StringComparison.Ordinal);
    }

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string FindRepoRoot() => Aetheus.Back.Tests.Architecture.RepositoryScan.Root;
}
