// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Services.DomainEvents;

namespace Aetheus.Back.Components.Pipelines.Events;

public sealed record PipelineRunCompletedEvent(int PipelineRunId, PipelineStatus Status) : IDomainEvent;
