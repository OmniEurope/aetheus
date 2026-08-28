// SPDX-License-Identifier: EUPL-1.2
using System.Net.Sockets;
using System.Text;
using Aetheus.Agent.Core.Collectors;
using NSubstitute;

namespace Aetheus.Agent.Core.Tests;

public sealed class TcpTeamspeakQueryClientTests
{
    [Fact]
    public async Task ExecuteAsync_WritesExactAsciiCommandAndReturnsCompleteReply()
    {
        var transport = Substitute.For<ITeamspeakQueryTransport>();
        ReadOnlyMemory<byte> observedPayload = default;
        transport.ExchangeAsync(10011, Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                observedPayload = call.ArgAt<ReadOnlyMemory<byte>>(1);
                return "TS3\nerror id=0 msg=ok\n";
            });
        var client = new TcpTeamspeakQueryClient(transport);
        const string commands = "login user pass\nserverinfo\nquit\n";

        var result = await client.ExecuteAsync(
            10011,
            commands,
            TestContext.Current.CancellationToken);

        Assert.Equal("TS3\nerror id=0 msg=ok\n", result);
        Assert.Equal(commands, Encoding.ASCII.GetString(observedPayload.Span));
        await transport.Received(1).ExchangeAsync(
            10011,
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_EmptyProtocolReply_ReturnsNull()
    {
        var transport = Substitute.For<ITeamspeakQueryTransport>();
        transport.ExchangeAsync(Arg.Any<int>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(" \r\n");
        var client = new TcpTeamspeakQueryClient(transport);

        var result = await client.ExecuteAsync(
            10011,
            "quit\n",
            TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task ExecuteAsync_TransportFailure_ReturnsNull()
    {
        var transport = Substitute.For<ITeamspeakQueryTransport>();
        transport.ExchangeAsync(Arg.Any<int>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(new SocketException()));
        var client = new TcpTeamspeakQueryClient(transport);

        var result = await client.ExecuteAsync(
            10011,
            "quit\n",
            TestContext.Current.CancellationToken);

        Assert.Null(result);
    }
}
