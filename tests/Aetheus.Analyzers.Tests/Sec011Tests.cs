// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Analyzers.Tests;

/// <summary>
/// SEC011 reports every construction of BinaryFormatter and every member used on it, and nothing
/// else, including the serializers that replace it.
/// </summary>
public sealed class Sec011Tests
{
    private static Task VerifyAsync(string source) =>
        MarkupVerifier<SEC011_NoBinaryFormatterAnalyzer>.VerifyAsync(source);

    [Fact]
    public async Task ConstructingAndUsingBinaryFormatterIsReported()
    {
        await VerifyAsync("""
            #pragma warning disable SYSLIB0011
            using System.IO;
            using System.Runtime.Serialization.Formatters.Binary;
            public static class Legacy
            {
                public static object Read(Stream stream)
                {
                    var formatter = {|SEC011:new BinaryFormatter()|};
                    return {|SEC011:formatter.Deserialize|}(stream);
                }
            }
            """);
    }

    [Fact]
    public async Task ASafeSerializerIsNotReported()
    {
        await VerifyAsync("""
            using System.IO;
            using System.Text.Json;
            public static class Modern
            {
                public static T? Read<T>(Stream stream) => JsonSerializer.Deserialize<T>(stream);
                // The type name in a string is not a use.
                public const string Banned = "System.Runtime.Serialization.Formatters.Binary.BinaryFormatter";
            }
            """);
    }

    [Fact]
    public async Task KnownFalsePositive_ASelfCheckProvingTheRuntimeRefusesItIsReported()
    {
        // Documented false positive: the construction exists only to prove the runtime throws
        // PlatformNotSupportedException, which is safe, but the rule cannot tell intent from use.
        await VerifyAsync("""
            #pragma warning disable SYSLIB0011
            using System;
            using System.IO;
            using System.Runtime.Serialization.Formatters.Binary;
            public static class StartupChecks
            {
                public static bool RuntimeRefusesBinaryFormatter()
                {
                    try { {|SEC011:{|SEC011:new BinaryFormatter()|}.Serialize|}(Stream.Null, 1); return false; }
                    catch (PlatformNotSupportedException) { return true; }
                }
            }
            """);
    }
}
