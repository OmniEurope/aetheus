// SPDX-License-Identifier: EUPL-1.2

using Aetheus.Shared.Validation;

namespace Aetheus.Front.Helpers;

public sealed class PasswordChangeModel
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [StringLength(PasswordPolicy.MaximumLength, MinimumLength = PasswordPolicy.MinimumLength)]
    public string NewPassword { get; set; } = string.Empty;

    [Required]
    [Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; set; } = string.Empty;
}
