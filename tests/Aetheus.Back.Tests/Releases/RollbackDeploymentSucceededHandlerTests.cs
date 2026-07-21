// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Releases;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class RollbackDeploymentSucceededHandlerTests
{
    [Fact]
    public async Task HandleAsync_ForwardsRollbackIdToReleaseService()
    {
        var service = Substitute.For<IReleaseService>();
        var handler = new RollbackDeploymentSucceededHandler(service);

        await handler.HandleAsync(new RollbackDeploymentSucceededEvent(42), ct: TestContext.Current.CancellationToken);

        await service.Received(1).NotifyRollbackDeploymentSucceededAsync(42, Arg.Any<CancellationToken>());
    }
}
