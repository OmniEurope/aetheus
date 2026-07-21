// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Shared.Validation;

public sealed class MaxItemStringLengthAttribute(int maxLength) : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not IEnumerable<string> items) return ValidationResult.Success;

        var index = 0;
        foreach (var item in items)
        {
            if (item is null)
            {
                index++;
                continue;
            }

            if (item.Length > maxLength)
                return new ValidationResult(
                    $"Item at index {index} exceeds maximum length of {maxLength} characters.",
                    [validationContext.MemberName!]);
            index++;
        }

        return ValidationResult.Success;
    }
}
