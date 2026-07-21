// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Components.ServiceConnections;

public interface IServiceConnectionTester
{
    /// <summary>
    /// Performs a real connectivity/credential probe for the given connection. Never fabricates a
    /// success: provider types with no probe return <see cref="ServiceConnectionTestStatus.Unsupported"/>,
    /// and any transport failure returns <see cref="ServiceConnectionTestStatus.Error"/>.
    /// </summary>
    Task<ServiceConnectionTestResultDto> TestAsync(ServiceConnectionType type, string? url, string configurationJson, CancellationToken ct = default);
}
