// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// Names what currently holds a colour's published ports, for a start that failed to bind one.
/// </summary>
/// <remarks>
/// Removing the idle colour's own containers is not always enough: nightly kept failing with "port is
/// already allocated" on a container Compose had just recreated, which means the holder is something
/// else. It turned out to be portfolio-prod-front, a different project on the same host, healthy and in
/// production - a neighbour that was there first, not a leftover. The reset cannot simply be widened,
/// because `--remove-orphans` under a profile would take down the live colour that carries traffic. So
/// this reports the holder instead of guessing at it. Read-only: it inspects and removes nothing.
/// </remarks>
internal sealed class BlueGreenPortHolders(IShellRunner shell)
{
    internal async Task ReportAsync(
        BlueGreenContext context, string colour, int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput, CancellationToken ct)
    {
        var (front, back) = context.PortsFor(colour);
        foreach (var port in new[] { front, back })
        {
            var containers = await shell.RunExecAsync(
                "docker",
                ["ps", "-a", "--filter", $"publish={port}", "--format", "{{.Names}} | {{.Status}} | {{.Ports}}"],
                context.ComposeEnvironment, string.Empty,
                inheritEnvironment: true, 64 * 1024, ct, TimeSpan.FromSeconds(timeoutSeconds))
                .ConfigureAwait(false);
            var holders = containers.StdOut?.Trim();
            await onOutput(
                string.IsNullOrEmpty(holders)
                    ? $"Port {port}: no container publishes it; the holder is a host process."
                    : $"Port {port} is published by: {holders}",
                TaskLogLevel.Warning).ConfigureAwait(false);
        }
    }
}
