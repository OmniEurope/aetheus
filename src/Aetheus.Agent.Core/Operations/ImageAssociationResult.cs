// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// Outcome of proving that the image archive a built-artifact scanner reads was produced by this
/// run's source commit. It carries the refusal reason because the check has several distinct failure
/// modes (archive absent, archive ambiguous, provenance marker missing, revision genuinely different)
/// and reporting all of them as one message names a cause that is often not the real one.
/// </summary>
internal readonly record struct ImageAssociationResult(bool IsValid, string? Reason)
{
    public static ImageAssociationResult Valid => new(true, null);

    public static ImageAssociationResult Refused(string reason) => new(false, reason);
}
