// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Validation;

namespace Aetheus.Back.Tests;

public sealed class MailPasswordPolicyTests
{
    [Theory]
    [InlineData(11, false)]
    [InlineData(12, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void MailValidation_UsesInteractivePasswordLengthPolicy(int length, bool expected)
    {
        Assert.Equal(expected, MailValidation.IsValidPassword(new string('x', length)));
    }

    [Fact]
    public void MailValidation_RejectsControlCharactersWithinValidLength()
    {
        Assert.False(MailValidation.IsValidPassword("valid-prefix\nvalid-suffix"));
    }

    [Fact]
    public void MailRequestDataAnnotations_UseTheSameBounds()
    {
        var shortRequest = new CreateMailAccountRequest
        {
            Email = "user@example.com",
            Password = new string('x', PasswordPolicy.MinimumLength - 1)
        };
        var validRequest = shortRequest with { Password = new string('x', PasswordPolicy.MinimumLength) };

        Assert.NotEmpty(Validate(shortRequest));
        Assert.Empty(Validate(validRequest));
    }

    private static List<ValidationResult> Validate(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, validateAllProperties: true);
        return results;
    }
}
