// SPDX-License-Identifier: EUPL-1.2
using System.Net;

namespace Aetheus.Front.Services;

/// <summary>
/// Outcome of an HTTP call that can succeed (2xx + a <typeparamref name="TValue"/> body),
/// be rejected with a typed payload (400 → <typeparamref name="TError"/>, e.g. a validation
/// result), or target a missing resource (404). Centralises the
/// "200 → value / 400 → typed error / 404 → not found" pattern so callers don't reinvent it.
/// </summary>
public sealed record ApiOutcome<TValue, TError>(TValue? Value, TError? Error, bool NotFound, HttpStatusCode? StatusCode = null)
    where TValue : class
    where TError : class
{
    public bool IsSuccess => Value is not null;

    /// <summary>
    /// A failure that is neither a 2xx success nor a typed 400 validation error: an auth/server
    /// failure (401/403/500/…) the caller cannot interpret from <see cref="Value"/>/<see cref="Error"/>
    /// alone. Lets a gate distinguish "empty but OK" from "the request was rejected" (S-UX-TRGE).
    /// </summary>
    public bool IsTransportFailure => Value is null && Error is null && !NotFound;
}

/// <summary>
/// Uniform result for status-only endpoints (DELETE / no-body POST &amp; PUT) - replaces the bare
/// <c>bool</c> so every mutation reports <see cref="NotFound"/> / <see cref="Forbidden"/>
/// consistently. Implicitly converts to <c>bool</c> (= <see cref="Success"/>) so existing
/// <c>if (await Api.X())</c> / <c>Assert.True(await Api.X())</c> call sites keep compiling.
/// </summary>
public readonly record struct ApiStatus(bool Success, bool NotFound, bool Forbidden)
{
    public static ApiStatus From(HttpResponseMessage response) => new(
        response.IsSuccessStatusCode,
        response.StatusCode == HttpStatusCode.NotFound,
        response.StatusCode == HttpStatusCode.Forbidden);

    public static implicit operator bool(ApiStatus status) => status.Success;
}
