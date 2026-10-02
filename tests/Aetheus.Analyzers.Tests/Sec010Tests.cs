// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Analyzers.Tests;

/// <summary>
/// SEC010 reports every way of turning Newtonsoft's TypeNameHandling on, and leaves the guard that
/// refuses it alone.
/// </summary>
public sealed class Sec010Tests
{
    private const string NewtonsoftStub = """
        namespace Newtonsoft.Json
        {
            public enum TypeNameHandling { None = 0, Objects = 1, Arrays = 2, All = 3, Auto = 4 }
            public class JsonSerializerSettings { public TypeNameHandling TypeNameHandling { get; set; } }
            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class JsonPropertyAttribute : System.Attribute
            {
                public TypeNameHandling TypeNameHandling { get; set; }
            }
        }
        """;

    private static Task VerifyAsync(string source) =>
        MarkupVerifier<SEC010_NoNewtonsoftTypeNameHandlingAnalyzer>.VerifyAsync(source, NewtonsoftStub);

    [Fact]
    public async Task EnablingTypeNameHandlingIsReported()
    {
        await VerifyAsync("""
            using Newtonsoft.Json;
            public sealed class Payload
            {
                [JsonProperty(TypeNameHandling = {|SEC010:TypeNameHandling.Auto|})]
                public object? Body { get; set; }

                public static JsonSerializerSettings Settings() => new() { TypeNameHandling = {|SEC010:TypeNameHandling.All|} };

                public static void Mutate(JsonSerializerSettings settings) =>
                    settings.TypeNameHandling = {|SEC010:(TypeNameHandling)3|};
            }
            """);
    }

    [Fact]
    public async Task NoneAndAGuardThatRefusesItAreNotReported()
    {
        await VerifyAsync("""
            using System;
            using Newtonsoft.Json;
            public static class SafeSettings
            {
                public static JsonSerializerSettings Create() => new() { TypeNameHandling = TypeNameHandling.None };

                public static void Refuse(JsonSerializerSettings settings)
                {
                    if (settings.TypeNameHandling != TypeNameHandling.None && settings.TypeNameHandling == TypeNameHandling.Auto)
                        throw new InvalidOperationException();
                    if (settings.TypeNameHandling is TypeNameHandling.All) throw new InvalidOperationException();
                }
            }
            """);
    }

    [Fact]
    public async Task KnownFalsePositive_AValuePassedToAValidatorThatRejectsItIsReported()
    {
        // Documented false positive: an argument is also how the setting is applied, so a value
        // handed to a method that refuses it looks the same as one handed to a method that uses it.
        await VerifyAsync("""
            using System;
            using Newtonsoft.Json;
            public static class Policy
            {
                public static void EnsureRejected() => Reject({|SEC010:TypeNameHandling.Objects|});
                private static void Reject(TypeNameHandling value)
                {
                    if (value != TypeNameHandling.None) throw new InvalidOperationException();
                }
            }
            """);
    }
}
