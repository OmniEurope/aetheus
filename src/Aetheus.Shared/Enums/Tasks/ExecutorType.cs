// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Enums;

public enum ExecutorType
{
    Shell,
    Docker,
    /// <summary>
    /// F-32 / Hardening (#30): typed agent operation (no shell, no allow-list). The associated
    /// task carries an <see cref="OperationKind"/> that selects the executor on the agent side.
    /// </summary>
    Operation,
    /// <summary>
    /// Phase 2 isolation: the step's shell script runs inside an ephemeral, hardened container
    /// (<c>docker run --rm --cap-drop ALL --security-opt no-new-privileges --pids-limit …</c>). The
    /// task carries a container spec (image / runtime / network) and mounts the run workspace at
    /// <c>/w</c>. The agent destroys the container when the step finishes.
    /// </summary>
    Container
}
