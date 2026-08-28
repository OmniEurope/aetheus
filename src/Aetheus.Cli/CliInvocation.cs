// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Cli;

internal sealed record CliInvocation(Uri Server, string? Token, int Page, int PageSize)
{
    internal const int MaximumPage = 1_000_000;

    internal static async Task<CliInvocation> CreateAsync(
        string server,
        bool readTokenFromStandardInput,
        int requestedPage,
        int requestedPageSize,
        CancellationToken ct)
    {
        var token = await CliRuntime.ResolveTokenAsync(readTokenFromStandardInput, ct);
        return new CliInvocation(
            CliRuntime.ParseServerUri(server),
            token,
            CliRuntime.ClampPage(requestedPage),
            CliRuntime.ClampPageSize(requestedPageSize));
    }

    internal AetheusApiClient CreateClient() => new(Server, Token);
}
