// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using System.Text.RegularExpressions;

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// HBSY contract guard. The heartbeat ring's countdown math (the <c>CadenceSeconds</c> constant in
/// <c>HeartbeatPulse.razor.cs</c>) and its visual fill animation (the <c>--heartbeat-cadence</c> CSS
/// variable in <c>app.css</c>) are two independent encodings of the SAME beat cadence. If one moves and
/// the other does not, the ring silently desyncs from the real beat - exactly the drift the challenge
/// review flagged. This test parses both out of source and fails the build the moment they disagree,
/// turning the comment-only "single source of truth" claim into an enforced contract.
/// </summary>
public class HeartbeatCadenceContractTests
{
    [Fact]
    public void FrontCadenceConstant_Matches_CssCadenceVariable()
    {
        var root = FindRepoRoot();
        var pulseSource = File.ReadAllText(
            Path.Combine(root, "src", "Aetheus.Front", "Components", "Servers", "HeartbeatPulse.razor.cs"));
        var cssSource = File.ReadAllText(
            Path.Combine(root, "src", "Aetheus.Front", "wwwroot", "css", "app.css"));

        var constMatch = Regex.Match(pulseSource, @"const\s+int\s+CadenceSeconds\s*=\s*(\d+)\s*;");
        Assert.True(constMatch.Success, "Could not find the CadenceSeconds constant in HeartbeatPulse.razor.cs.");
        var constSeconds = int.Parse(constMatch.Groups[1].Value, CultureInfo.InvariantCulture);

        var cssMatch = Regex.Match(cssSource, @"--heartbeat-cadence:\s*(\d+)s");
        Assert.True(cssMatch.Success, "Could not find the --heartbeat-cadence CSS variable in app.css.");
        var cssSeconds = int.Parse(cssMatch.Groups[1].Value, CultureInfo.InvariantCulture);

        Assert.True(constSeconds == cssSeconds,
            $"Heartbeat cadence drift: HeartbeatPulse.CadenceSeconds={constSeconds}s but app.css "
            + $"--heartbeat-cadence={cssSeconds}s. Keep both in lockstep (HBSY single source of truth).");
    }

    private static string FindRepoRoot() => Aetheus.Front.Tests.Architecture.RepositoryScan.Root;
}
