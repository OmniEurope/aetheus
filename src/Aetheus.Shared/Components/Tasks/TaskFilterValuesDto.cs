// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Shared.Components.Tasks;

/// <summary>
/// Recette R-212: the values the task lists' checkable Server filter offers. The lists are loaded page by
/// page, so the names present across every task of the caller's scope come from the API.
/// </summary>
public sealed record TaskFilterValuesDto
{
    public List<string> ServerNames { get; init; } = [];
}
