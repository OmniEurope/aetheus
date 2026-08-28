// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Data.Entities;

public class AgentUpdateRequest
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string TargetVersion { get; set; } = string.Empty;
    public string ObservedVersion { get; set; } = string.Empty;
    public int? ObservedProtocolVersion { get; set; }
    public string? ObservedSessionId { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public AgentUpdateRequestStatus Status { get; set; }
    public bool IsActive { get; set; }
    public int? TaskId { get; set; }
    public string ExpectedCapabilitiesJson { get; set; } = "[]";
    public DateTime? StartedAt { get; set; }
    public DateTime? HandoffAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? ConfirmationDeadline { get; set; }
    public string? ConfirmedSessionId { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureDiagnostic { get; set; }

    public Server Server { get; set; } = null!;
    public ServerTask? Task { get; set; }
}
