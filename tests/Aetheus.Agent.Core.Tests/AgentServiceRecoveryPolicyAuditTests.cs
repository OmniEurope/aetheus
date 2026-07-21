// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Tests;

public sealed class AgentServiceRecoveryPolicyAuditTests
{
    [Fact]
    public void LinuxInstaller_RestartsAgentAfterWatchdogExit()
    {
        var script = ReadScript("install-agent-linux.sh").Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains(
            "ExecStart=$EXEC_START\nRestart=always\nRestartSec=10",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsInstaller_RestartsAgentAfterWatchdogExit()
    {
        var script = ReadScript("install-agent-windows.ps1");

        Assert.Contains(
            "sc.exe failure $ServiceName reset= 60 actions= restart/10000/restart/10000/restart/10000",
            script,
            StringComparison.Ordinal);
    }

    private static string ReadScript(string name) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "deploy", "scripts", name));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(AgentServiceRecoveryPolicyAuditTests).Assembly.Location)!);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Aetheus.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root (Aetheus.slnx).");
    }
}
