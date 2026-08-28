// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Auth;

/// <summary>
/// Agent/server enrollment - registering a new (or re-registering an existing) server against a
/// valid registration token and minting its bearer token. Split out of <see cref="IAuthService"/>
/// as a distinct responsibility (enrollment vs. user authentication).
/// </summary>
public interface IServerEnrollmentService
{
    Task<ServerRegistrationResponse?> RegisterServerAsync(ServerRegistrationRequest request, CancellationToken ct = default);
}
