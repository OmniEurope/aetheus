// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// Architectural guard: the dev-only TLS bypass literal <c>GIT_SSL_NO_VERIFY</c> must never appear
/// in backend source. The bypass belongs exclusively agent-side, gated on the agent's
/// <c>AllowInsecureCerts</c> option (F-001 lesson). A backend script/command generator emitting it
/// would push the bypass into every cloned repo regardless of the agent's security posture.
/// </summary>
public class InsecureGitFlagAuditTests
{
    [Fact]
    public void Backend_DoesNotReference_GitSslNoVerify()
    {
        var backDir = Path.Combine(FindRepoRoot(), "src", "Aetheus.Back");
        Assert.True(Directory.Exists(backDir), $"Backend dir not found: {backDir}");

        var offenders = new List<string>();
        var scanned = 0;
        foreach (var file in Directory.EnumerateFiles(backDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;
            scanned++;
            var text = File.ReadAllText(file);
            if (text.Contains("GIT_SSL_NO_VERIFY", StringComparison.Ordinal))
                offenders.Add(Path.GetFileName(file));
        }

        Assert.True(scanned >= 50, $"Scanner found only {scanned} backend files - likely a path bug.");
        Assert.True(offenders.Count == 0,
            "GIT_SSL_NO_VERIFY must not appear in backend source - keep the TLS bypass agent-side, " +
            "gated on AllowInsecureCerts. Offenders: " + string.Join(", ", offenders));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(InsecureGitFlagAuditTests).Assembly.Location)!);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Aetheus.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root (Aetheus.slnx).");
    }
}
