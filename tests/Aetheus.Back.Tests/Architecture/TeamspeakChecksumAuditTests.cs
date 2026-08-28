// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Tests.Architecture;

public sealed class TeamspeakChecksumAuditTests
{
    [Fact]
    public void LinuxInstaller_FailsClosedWhenTeamspeakChecksumIsMissing()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "deploy", "scripts", "install-agent-linux.sh"));

        Assert.Contains("TS_CHECKSUM_FILE=\"/etc/aetheus-agent/teamspeak.sha256\"", script, StringComparison.Ordinal);
        Assert.Contains("TS_SHA256=\"$(tr -d '[:space:]' < \"$TS_CHECKSUM_FILE\")\"", script, StringComparison.Ordinal);
        Assert.Contains("[ \"${#TS_SHA256}\" -eq 64 ] || die", script, StringComparison.Ordinal);
        Assert.Contains("sha256sum -c - || die", script, StringComparison.Ordinal);
        Assert.DoesNotContain("installing TeamSpeak $TS_VERSION without checksum verification", script, StringComparison.Ordinal);
    }

    private static string FindRepoRoot() => Aetheus.Back.Tests.Architecture.RepositoryScan.Root;
}
