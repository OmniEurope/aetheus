// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.PortRegistry;

/// <summary>
/// How a port reservation got into the registry. The registry is declarative today
/// (<see cref="Declared"/> / <see cref="Manual"/>); <see cref="Observed"/> exists so an agent-side
/// scan of listening ports can be added as a second source without reshaping the table or the
/// conflict rule, which reads every source alike.
/// </summary>
public enum PortReservationSource
{
    /// <summary>Declared by a deployment when its run was launched.</summary>
    Declared = 0,

    /// <summary>Entered by a human for a service Aetheus does not deploy.</summary>
    Manual = 1,

    /// <summary>Reported by an agent that saw the port actually listening. Not produced yet.</summary>
    Observed = 2
}
