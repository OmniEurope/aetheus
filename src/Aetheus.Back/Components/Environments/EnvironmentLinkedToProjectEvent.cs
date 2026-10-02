// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.Environments;

/// <summary>
/// An environment was saved while linked to a project. Pipelines (above this module) copies the
/// environment's pipelines into the project's repository; Environments used to call it directly,
/// reaching up six layers (layer guard, 2026-09-25).
/// </summary>
public sealed record EnvironmentLinkedToProjectEvent(int EnvironmentId, string EnvironmentName, int ProjectId) : IDomainEvent;
