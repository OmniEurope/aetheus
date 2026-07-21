// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Collectors;

/// <summary>
/// Common base for shell-driven collectors. Centralises the
/// <see cref="ILogger{T}"/> + <see cref="IShellRunner"/> dependency pair
/// and exposes them to derived collectors so they don't have to redeclare
/// the storage every time.
/// </summary>
public abstract class BaseShellCollector<T>(ILogger<T> logger, IShellRunner shell)
{
    protected ILogger<T> Logger { get; } = logger;
    protected IShellRunner Shell { get; } = shell;
}
