// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Git;

public sealed record GitLightOptions
{
    public string RepositoriesPath { get; init; } = "./data/git-repos";
    public int MaintenanceIntervalHours { get; init; } = 24;
}
