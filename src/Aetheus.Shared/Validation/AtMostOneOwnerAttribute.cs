// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Shared.Validation;

/// <summary>
/// Validates that an owned-resource DTO carries at most one scope owner among
/// <c>ProjectId</c>, <c>EnvironmentId</c> and <c>ProjectServerId</c>. Zero owners is valid and
/// denotes an organization/global-level resource; organization ownership is carried separately.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class AtMostOneOwnerAttribute : ValidationAttribute
{
    private static readonly string[] OwnerProperties = ["ProjectId", "EnvironmentId", "ProjectServerId"];

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null) return ValidationResult.Success;

        var type = value.GetType();
        var setCount = OwnerProperties.Count(propertyName =>
            type.GetProperty(propertyName)?.GetValue(value) is not null);

        return setCount <= 1
            ? ValidationResult.Success
            : new ValidationResult(
                "At most one owner can be set: ProjectId, EnvironmentId, or ProjectServerId.",
                OwnerProperties);
    }
}
