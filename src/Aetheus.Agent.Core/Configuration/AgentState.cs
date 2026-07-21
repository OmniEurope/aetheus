// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Configuration;

public sealed class AgentState
{
    public int? ServerId { get; set; }
    public string? BearerToken { get; set; }
    public DateTime? TokenExpiresAt { get; set; }
    public bool IsEnrolled => ServerId.HasValue && !string.IsNullOrEmpty(BearerToken);
    public bool IsTokenExpired => TokenExpiresAt.HasValue && DateTime.UtcNow >= TokenExpiresAt.Value;

    public void ClearSensitiveData()
    {
        BearerToken = null;
        TokenExpiresAt = null;
    }
}
