// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Validation;

namespace Aetheus.Front.Tests;

public class BoundedDictionaryAttributeTests
{
    private static List<ValidationResult> Validate(object model)
    {
        var ctx = new ValidationContext(model);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, ctx, results, true);
        return results;
    }

    private class TestModel
    {
        [BoundedDictionary(3, 10, 50)]
        public Dictionary<string, string>? Data { get; set; }
    }

    [Fact]
    public void NullValue_Passes()
    {
        var model = new TestModel { Data = null };
        Assert.Empty(Validate(model));
    }

    [Fact]
    public void EmptyDictionary_Passes()
    {
        var model = new TestModel { Data = new Dictionary<string, string>() };
        Assert.Empty(Validate(model));
    }

    [Fact]
    public void WithinLimits_Passes()
    {
        var model = new TestModel
        {
            Data = new Dictionary<string, string>
            {
                ["KEY1"] = "value1",
                ["KEY2"] = "value2"
            }
        };
        Assert.Empty(Validate(model));
    }

    [Fact]
    public void TooManyEntries_Fails()
    {
        var model = new TestModel
        {
            Data = new Dictionary<string, string>
            {
                ["A"] = "1",
                ["B"] = "2",
                ["C"] = "3",
                ["D"] = "4"
            }
        };
        var results = Validate(model);
        Assert.NotEmpty(results);
        Assert.Contains("3 entries", results[0].ErrorMessage!);
    }

    [Fact]
    public void KeyTooLong_Fails()
    {
        var model = new TestModel
        {
            Data = new Dictionary<string, string>
            {
                [new string('K', 11)] = "value"
            }
        };
        var results = Validate(model);
        Assert.NotEmpty(results);
        Assert.Contains("exceeds maximum length of 10", results[0].ErrorMessage!);
    }

    [Fact]
    public void ValueTooLong_Fails()
    {
        var model = new TestModel
        {
            Data = new Dictionary<string, string>
            {
                ["KEY"] = new string('V', 51)
            }
        };
        var results = Validate(model);
        Assert.NotEmpty(results);
        Assert.Contains("exceeds maximum length of 50", results[0].ErrorMessage!);
    }

    [Fact]
    public void EmptyValue_Passes()
    {
        var model = new TestModel
        {
            Data = new Dictionary<string, string> { ["KEY"] = "" }
        };
        Assert.Empty(Validate(model));
    }
}
