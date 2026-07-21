// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Sockets;
using System.Text;
using Aetheus.Agent.Core.Collectors;

namespace Aetheus.Agent.Core.Tests;

/// <summary>
/// S-FEAT-11: the ServerQuery client talks TCP directly (no <c>nc</c>). Exercised against a loopback
/// <see cref="TcpListener"/> standing in for a ServerQuery telnet endpoint.
/// </summary>
public class TcpTeamspeakQueryClientTests
{
    [Fact]
    public async Task ExecuteAsync_WritesCommandsAndReturnsServerResponse()
    {
        using var listener = new TcpListener(IPAddress.IPv6Any, 0) { Server = { DualMode = true } };
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var receivedLines = new List<string>();

        var serverTask = Task.Run(async () =>
        {
            using var conn = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            await using var stream = conn.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            while (await reader.ReadLineAsync(TestContext.Current.CancellationToken) is { } line)
            {
                receivedLines.Add(line);
                if (line == "quit") break;
            }

            foreach (var fragment in new[] { "TS3\nvirtualserver_", "name=Test\\sServer ", "error id=0 msg=ok\n" })
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(fragment), TestContext.Current.CancellationToken);
                await stream.FlushAsync(TestContext.Current.CancellationToken);
                await Task.Yield();
            }
            conn.Client.Shutdown(SocketShutdown.Both);
        }, TestContext.Current.CancellationToken);

        var client = new TcpTeamspeakQueryClient();
        var result = await client.ExecuteAsync(
            port, "serverinfo\nquit\n", TestContext.Current.CancellationToken);

        await serverTask;

        Assert.NotNull(result);
        Assert.Contains("virtualserver_name=Test", result);
        Assert.Equal(new[] { "serverinfo", "quit" }, receivedLines);
    }

    [Fact]
    public async Task ExecuteAsync_ConnectionRefused_ReturnsNull()
    {
        using var reserved = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        reserved.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var closedPort = ((IPEndPoint)reserved.LocalEndPoint!).Port;

        var client = new TcpTeamspeakQueryClient();
        var result = await client.ExecuteAsync(
            closedPort, "quit\n", TestContext.Current.CancellationToken);

        Assert.Null(result);
    }
}
