// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Shared;

/// <summary>Compatibility alias for consumers compiled against the former misleading name.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
[Obsolete("Use AtMostOneOwnerAttribute. The validation contract allows zero or one scope owner.")]
public sealed class ExactlyOneOwnerAttribute : AtMostOneOwnerAttribute;
