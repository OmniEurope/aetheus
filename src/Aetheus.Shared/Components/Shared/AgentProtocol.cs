// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Shared;

/// <summary>Versioned control-plane contract shared by the backend and agents.</summary>
public static class AgentProtocol
{
    /// <summary>Current additive heartbeat and task contract.</summary>
    public const int CurrentVersion = 2;

    public const int MinimumSupportedVersion = CurrentVersion;
    public const int MaximumSupportedVersion = CurrentVersion;

    public static bool IsSupported(int version) =>
        version is >= MinimumSupportedVersion and <= MaximumSupportedVersion;
}
