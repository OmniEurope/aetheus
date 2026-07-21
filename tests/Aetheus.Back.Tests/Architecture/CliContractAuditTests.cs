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
    public void CliWarnsAboutCommandLineTokensAndInsecureTransport()
    {
        var root = FindRepoRoot();
        var program = File.ReadAllText(Path.Combine(root, "src", "Aetheus.Cli", "Program.cs"));
        var client = File.ReadAllText(Path.Combine(root, "src", "Aetheus.Cli", "AetheusApiClient.cs"));

        Assert.Contains("--token can be exposed", program, StringComparison.Ordinal);
        Assert.Contains("AETHEUS_INSECURE=1 is sending a bearer token over plaintext HTTP", client, StringComparison.Ordinal);
    }

    [Fact]
    public void CliMachineCommands_ReturnNonZeroOnHttpFailure()
    {
        var program = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Aetheus.Cli", "Program.cs"));
        var pipelineRun = program[program.IndexOf("pipelineRunCommand.SetAction", StringComparison.Ordinal)..program.IndexOf("// --- task list ---", StringComparison.Ordinal)];
        var health = program[program.IndexOf("healthCommand.SetAction", StringComparison.Ordinal)..program.IndexOf("return await rootCommand", StringComparison.Ordinal)];

        Assert.Contains("Console.Error.WriteLine($\"Error: {response.StatusCode}", pipelineRun, StringComparison.Ordinal);
        Assert.Contains("return 1;", pipelineRun, StringComparison.Ordinal);
        Assert.Contains("return response.IsSuccessStatusCode ? 0 : 1;", health, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(CliContractAuditTests).Assembly.Location)!);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Aetheus.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root (Aetheus.slnx).");
    }
}
