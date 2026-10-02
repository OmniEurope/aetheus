// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Microsoft.Extensions.Localization;
using NSubstitute;

namespace Aetheus.Front.Tests;

/// <summary>
/// Tests the regex-based Translate and LocalizeFieldName methods of
/// <see cref="LocalizedDataAnnotationsValidator"/>.
/// </summary>
public class LocalizedValidatorTests
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Type VType = typeof(LocalizedDataAnnotationsValidator);

    // === Translate ===

    [Theory]
    [InlineData("The Name field is required.", "Validation_Required")]
    [InlineData("The field Email must be a string with a maximum length of 255.", "Validation_MaxLength")]
    [InlineData("The field Password must be a string with a minimum length of 8 and a maximum length of 128.", "Validation_StringLength")]
    [InlineData("The field Age must be between 1 and 120.", "Validation_Range")]
    [InlineData("The Email field is not a valid e-mail address.", "Validation_Email")]
    [InlineData("The Url field is not a valid fully-qualified http, https, or ftp URL.", "Validation_Url")]
    public void Translate_RecognizesStandardPattern(string raw, string expectedKeyPrefix)
    {
        var instance = CreateInstance();
        var method = VType.GetMethod("Translate", Priv)!;
        var result = (string)method.Invoke(instance, [raw])!;
        Assert.Contains(expectedKeyPrefix, result);
    }

    [Fact]
    public void Translate_CustomMessage_PassesThrough()
    {
        var instance = CreateInstance();
        var method = VType.GetMethod("Translate", Priv)!;
        var result = (string)method.Invoke(instance, ["Custom error message"])!;
        Assert.Equal("Custom error message", result);
    }

    [Fact]
    public void Translate_NullOrEmpty_ReturnsEmpty()
    {
        var instance = CreateInstance();
        var method = VType.GetMethod("Translate", Priv)!;
        Assert.Equal(string.Empty, (string)method.Invoke(instance, [null])!);
        Assert.Equal(string.Empty, (string)method.Invoke(instance, [""])!);
    }

    // === LocalizeFieldName ===

    [Fact]
    public void LocalizeFieldName_Known_ReturnsLocalized()
    {
        var instance = CreateInstance("Name", "Nom");
        var method = VType.GetMethod("LocalizeFieldName", Priv)!;
        var result = (string)method.Invoke(instance, ["Name"])!;
        Assert.Equal("Nom", result);
    }

    [Fact]
    public void LocalizeFieldName_Unknown_ReturnsFallback()
    {
        var instance = CreateInstance();
        var method = VType.GetMethod("LocalizeFieldName", Priv)!;
        var result = (string)method.Invoke(instance, ["UnknownField"])!;
        Assert.Equal("UnknownField", result);
    }

    // === Helpers ===

    private static LocalizedDataAnnotationsValidator CreateInstance(string? knownKey = null, string? knownValue = null)
    {
        var instance = (LocalizedDataAnnotationsValidator)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(LocalizedDataAnnotationsValidator));

        var locMock = Substitute.For<IStringLocalizer<Aetheus.Front.Resources.AppStrings>>();

        // Default: return key as value with ResourceNotFound = true
        locMock[Arg.Any<string>()].Returns(ci =>
        {
            var key = (string)ci[0];
            if (knownKey is not null && key == knownKey)
                return new LocalizedString(key, knownValue!, false);
            return new LocalizedString(key, key, true);
        });
        locMock[Arg.Any<string>(), Arg.Any<object[]>()].Returns(ci =>
        {
            var key = (string)ci[0];
            var args = (object[])ci[1];
            return new LocalizedString(key, string.Format(key, args));
        });

        VType.GetProperty("L", Priv)!.SetValue(instance, locMock);
        return instance;
    }
}
