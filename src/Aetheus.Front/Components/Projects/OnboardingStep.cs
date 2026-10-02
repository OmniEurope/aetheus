// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Projects;

/// <summary>
/// One row of an onboarding checklist. Callers build the list from data they already hold, so a
/// state is never assumed: a caller that cannot prove a step's state must not render the list.
/// </summary>
public sealed record OnboardingStep
{
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Icon { get; init; }
    public required OnboardingStepState State { get; init; }

    /// <summary>Target of the row action. Ignored when the step is blocked.</summary>
    public string? Href { get; init; }

    /// <summary>Label of the row action button.</summary>
    public string? ActionLabel { get; init; }

    /// <summary>Why the step cannot be started yet. Required when <see cref="State"/> is Blocked.</summary>
    public string? BlockedReason { get; init; }
}
