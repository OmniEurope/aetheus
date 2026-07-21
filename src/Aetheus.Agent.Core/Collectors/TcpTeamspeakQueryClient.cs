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

    public async Task<string?> ExecuteAsync(int port, string commands, CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(Timeout);
        var token = timeoutCts.Token;

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("localhost", port, token).ConfigureAwait(false);

            await using var stream = client.GetStream();
            var payload = Encoding.ASCII.GetBytes(commands);
            await stream.WriteAsync(payload, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);

            using var reader = new StreamReader(stream, Encoding.UTF8);
            var output = await reader.ReadToEndAsync(token).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(output) ? null : output;
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or ObjectDisposedException)
        {
            return null;
        }
    }
}
