// SPDX-License-Identifier: EUPL-1.2
using System.Net.Sockets;
using System.Text;

namespace Aetheus.Agent.Core.Collectors;

/// <summary>
/// Native <see cref="TcpClient"/> implementation of the TeamSpeak ServerQuery client (S-FEAT-11).
/// Replaces the previous <c>echo … | nc localhost &lt;port&gt;</c> shell-out: no <c>nc</c> dependency,
/// no shell parsing of the credential/command stream, and an in-process 5-second connect+read
/// timeout. The command script is expected to end with <c>quit</c>, so the server closes the
/// connection and <see cref="StreamReader.ReadToEndAsync(CancellationToken)"/> returns the full reply.
/// </summary>
public sealed class TcpTeamspeakQueryClient : ITeamspeakQueryClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly ITeamspeakQueryTransport _transport;

    public TcpTeamspeakQueryClient(ITeamspeakQueryTransport transport) =>
        _transport = transport;

    public async Task<string?> ExecuteAsync(int port, string commands, CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(Timeout);
        var token = timeoutCts.Token;

        try
        {
            var payload = Encoding.ASCII.GetBytes(commands);
            var output = await _transport.ExchangeAsync(port, payload, token).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(output) ? null : output;
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or ObjectDisposedException)
        {
            return null;
        }
    }
}
