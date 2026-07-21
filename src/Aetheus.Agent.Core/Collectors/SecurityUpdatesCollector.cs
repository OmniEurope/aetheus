// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Operations;
using Aetheus.Shared.DTOs;

namespace Aetheus.Agent.Core.Collectors;

/// <summary>
/// PLAN-006 4.1: reports pending OS updates (total + security) at heartbeat by running
/// <c>apt-get -s upgrade</c> (a non-mutating simulation - no sudo, no changes). No-fake contract
/// (mirrors <c>RkhunterCollector</c>'s installed-vs-failed decoupling):
/// <list type="bullet">
/// <item>no <c>apt-get</c> binary =&gt; <c>PackageManagerPresent=false</c> (feature N/A), never "0 updates";</item>
/// <item>probe failed =&gt; <c>ProbeSucceeded=false</c> =&gt; state UNKNOWN, never "up to date";</item>
/// <item>only a successful probe yields trustworthy counts.</item>
/// </list>
/// Shell-free: argv via <see cref="IShellRunner"/>, file probe via <see cref="File.Exists(string)"/>.
/// </summary>
public sealed class SecurityUpdatesCollector(
    ILogger<SecurityUpdatesCollector> logger,
    IShellRunner shell,
    Func<string, bool>? fileExists = null)
    : BaseShellCollector<SecurityUpdatesCollector>(logger, shell), ISecurityUpdatesCollector
{
    private const string AptGetPath = "/usr/bin/apt-get";

    // Cap the reported list so a badly-behind box can't bloat the heartbeat; the COUNTS stay exact.
    private const int MaxReported = 200;

    private readonly Func<string, bool> _fileExists = fileExists ?? File.Exists;

    public async Task<SecurityUpdatesDataDto> CollectAsync(CancellationToken ct = default)
    {
        if (!_fileExists(AptGetPath))
            return new SecurityUpdatesDataDto(); // PackageManagerPresent = false => not applicable

        try
        {
            var res = await Shell.RunExecAsync("apt-get", ["-s", "upgrade"], ct).ConfigureAwait(false);
            if (res.ExitCode != 0)
            {
                Logger.LogDebug("[SecurityUpdatesCollector] `apt-get -s upgrade` exited {Code}", res.ExitCode);
                return new SecurityUpdatesDataDto { PackageManagerPresent = true, ProbeSucceeded = false };
            }

            var updates = AptSimulateParser.Parse(res.StdOut);
            var reported = updates.Count > MaxReported ? updates.Take(MaxReported).ToList() : [.. updates];
            return new SecurityUpdatesDataDto
            {
                PackageManagerPresent = true,
                ProbeSucceeded = true,
                PendingTotal = updates.Count,
                PendingSecurity = updates.Count(u => u.IsSecurity),
                Updates = reported
            };
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "[SecurityUpdatesCollector] probe failed");
            return new SecurityUpdatesDataDto { PackageManagerPresent = true, ProbeSucceeded = false };
        }
    }
}
