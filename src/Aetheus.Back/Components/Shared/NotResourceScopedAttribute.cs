// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.Shared;

/// <summary>
/// Says, where the next reader will see it, why an action that takes a resource id does not ask
/// <see cref="Aetheus.Back.Services.IResourceAuthorizationService"/>: typically a resource owned by
/// the calling user, filtered on their own id rather than on a tenant permission. SEC014 accepts it
/// only with a non-empty reason; it changes nothing at runtime.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
public sealed class NotResourceScopedAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}
