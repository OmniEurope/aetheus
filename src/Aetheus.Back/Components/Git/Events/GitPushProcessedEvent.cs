// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.Git.Events;

/// <summary>
/// A push finished being received and the repository state is committed.
///
/// Git used to react to its own pushes by driving the orchestrator directly: it read the project's
/// webhook-triggered pipelines, matched branch filters, cancelled superseded runs, launched
/// replacements and upserted pipeline definitions found under <c>.pipeline/</c>. All of that is
/// orchestration expressed inside the transport, and it is what made Git depend on Pipelines.
///
/// Git now states what happened. What a push means for pipelines is decided by the module that owns
/// pipelines.
///
/// Dispatched non-strictly, deliberately: the code it replaces caught and logged every failure so a
/// push could still succeed when a pipeline could not be triggered. A push that has already been
/// written to disk must not be reported as failed because a downstream trigger did not fire.
/// </summary>
/// <param name="ProjectId">Project the repository belongs to.</param>
/// <param name="GitRepoId">Internal repository id.</param>
/// <param name="Slug">Repository slug, for diagnostics.</param>
/// <param name="RepositoryName">Repository display name, for diagnostics.</param>
/// <param name="DefaultBranch">Default branch after the push.</param>
/// <param name="DiskPath">
/// Resolved, validated on-disk path of the bare repository. Carried rather than re-derived: the path
/// is subject to a traversal check that belongs to the Git module, and a subscriber must not be able
/// to reconstruct it from a slug.
/// </param>
/// <param name="RefUpdates">Reference updates this push carried.</param>
public sealed record GitPushProcessedEvent(
    int ProjectId,
    int GitRepoId,
    string Slug,
    string RepositoryName,
    string? DefaultBranch,
    string DiskPath,
    IReadOnlyList<GitRefUpdate> RefUpdates) : IDomainEvent;
