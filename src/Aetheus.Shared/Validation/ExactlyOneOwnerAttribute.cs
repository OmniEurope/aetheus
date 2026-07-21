// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Shared.Validation;

/// <summary>
/// Validates that an owned-resource DTO carries <b>at most one</b> scope owner among
/// <c>ProjectId</c>, <c>EnvironmentId</c> and <c>ProjectServerId</c>. <b>Zero owners is valid
/// and intentional</b>: it denotes an organization/global-level resource (e.g. a global variable
/// library - see <c>VariableLibraryService</c>, which explicitly handles libraries with all three
/// owner ids null). Org ownership is carried separately by <c>OrganizationId</c>, not by this
/// attribute. The contract is therefore "at most one scope owner", never "exactly one".
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ExactlyOneOwnerAttribute : ValidationAttribute
{
    // Scope-owner properties checked on the validated DTO, in priority order. Kept as a string
    // list (reflection) so the attribute stays in Shared without referencing Back entities.
    private static readonly string[] OwnerProperties = ["ProjectId", "EnvironmentId", "ProjectServerId"];

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null) return ValidationResult.Success;

        var type = value.GetType();
        var setCount = 0;

        foreach (var propName in OwnerProperties)
        {
            var prop = type.GetProperty(propName);
            if (prop?.GetValue(value) is not null)
                setCount++;
        }

        return setCount <= 1
            ? ValidationResult.Success
            : new ValidationResult(
                "At most one owner can be set: ProjectId, EnvironmentId, or ProjectServerId.",
                OwnerProperties);
    }
}
