// SPDX-License-Identifier: EUPL-1.2
using System.Net.Sockets;
using System.Text;

namespace Aetheus.Agent.Core.Collectors;

public sealed class TcpTeamspeakQueryTransport : ITeamspeakQueryTransport
{
    public async Task<string> ExchangeAsync(
        int port,
        ReadOnlyMemory<byte> payload,
        CancellationToken ct)
    {
        using var client = new TcpClient();
        await client.ConnectAsync("localhost", port, ct).ConfigureAwait(false);

        await using var stream = client.GetStream();
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    }
}
