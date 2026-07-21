// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Collectors;

/// <summary>
/// Opens a TeamSpeak 3 ServerQuery session and runs a command script, returning the raw response.
/// Implemented over a native TCP socket (S-FEAT-11) so the agent no longer shells out to
/// <c>nc</c> - no external binary, no shell parser, and connect/read timeouts in-process.
/// </summary>
public interface ITeamspeakQueryClient
{
    /// <summary>
    /// Connects to the ServerQuery telnet endpoint on <paramref name="port"/> (localhost), writes the
    /// newline-terminated <paramref name="commands"/> script (which must end with <c>quit</c> so the
    /// server closes the connection), and returns everything the server sent. Returns <c>null</c> on
    /// any connection/timeout failure.
    /// </summary>
    Task<string?> ExecuteAsync(int port, string commands, CancellationToken ct = default);
}
