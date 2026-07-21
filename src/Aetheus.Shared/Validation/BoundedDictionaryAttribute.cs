// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Shared.Validation;

/// <summary>
/// Hardening (#25): bounds a Dictionary&lt;string,string&gt; (e.g. EnvironmentVariables) so a
/// caller cannot DoS the API with megabytes of K/V pairs. Caps the entry count and the length
/// of each key and value.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class BoundedDictionaryAttribute(int maxEntries, int maxKeyLength, int maxValueLength) : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not IDictionary<string, string> dict) return ValidationResult.Success;

        if (dict.Count > maxEntries)
            return new ValidationResult(
                $"At most {maxEntries} entries are allowed.",
                [validationContext.MemberName!]);

        foreach (var (k, v) in dict)
        {
            if (k.Length > maxKeyLength)
                return new ValidationResult(
                    $"Key '{k}' exceeds maximum length of {maxKeyLength}.",
                    [validationContext.MemberName!]);
            if (v is not null && v.Length > maxValueLength)
                return new ValidationResult(
                    $"Value for key '{k}' exceeds maximum length of {maxValueLength}.",
                    [validationContext.MemberName!]);
        }

        return ValidationResult.Success;
    }
}
