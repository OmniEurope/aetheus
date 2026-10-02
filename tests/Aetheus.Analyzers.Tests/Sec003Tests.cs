// SPDX-License-Identifier: EUPL-1.2
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace Aetheus.Analyzers.Tests;

/// <summary>
/// SEC003 has to be right in both directions. A missed password in a log is a durable disclosure; a
/// false positive on a key NAME is how a security rule gets switched off.
/// </summary>
public sealed class Sec003Tests
{
    private const string LoggingStub = """
        using System;
        namespace Microsoft.Extensions.Logging
        {
            public interface ILogger { }
            public static class LoggerExtensions
            {
                public static void LogInformation(this ILogger logger, string message, params object[] args) { }
                public static void LogWarning(this ILogger logger, string message, params object[] args) { }
                public static void LogError(this ILogger logger, string message, params object[] args) { }
            }
        }
        """;

    private static string Source(string body) => $$"""
        using System;
        using Microsoft.Extensions.Logging;
        {{LoggingStub}}
        public sealed class Subject
        {
            private readonly ILogger _logger = null!;
            public void Run(string dbPassword, string userName, string apiKeyName, int tokenCount, string sessionToken)
            {
        {{body}}
            }
        }
        """;

    private static Task VerifyAsync(string body, params DiagnosticResult[] expected) =>
        Verifier<SEC003_NoSecretInDiagnosticOutputAnalyzer>.VerifyAsync("Subject.cs", Source(body), expected);

    private static DiagnosticResult At(int line, int column, int endColumn, string name, string sink) =>
        Verifier<SEC003_NoSecretInDiagnosticOutputAnalyzer>.Expect(
            "SEC003", DiagnosticSeverity.Warning, "Subject.cs", line, column, line, endColumn, name, sink);

    [Fact]
    public async Task APasswordInterpolatedIntoALogIsReported()
    {
        await VerifyAsync(
            """        _logger.LogWarning($"connect failed for {dbPassword}");""",
            At(19, 50, 60, "dbPassword", "a log"));
    }

    [Fact]
    public async Task ATokenPassedAsALogArgumentIsReported()
    {
        await VerifyAsync(
            """        _logger.LogError("auth failed {Token}", sessionToken);""",
            At(19, 49, 61, "sessionToken", "a log"));
    }

    [Fact]
    public async Task ASecretWrittenToTheConsoleIsReported()
    {
        await VerifyAsync(
            """        Console.WriteLine(dbPassword);""",
            At(19, 27, 37, "dbPassword", "the console"));
    }

    [Fact]
    public async Task ASecretPutIntoAnExceptionMessageIsReported()
    {
        await VerifyAsync(
            """        throw new InvalidOperationException($"bad {dbPassword}");""",
            At(19, 52, 62, "dbPassword", "an exception message"));
    }

    [Fact]
    public async Task AnIdentifierThatMerelyMentionsAKeyIsNotReported()
    {
        // apiKeyName names the key, it is not the key: logging it is exactly how a secret is made
        // traceable without being disclosed.
        await VerifyAsync("""        _logger.LogInformation("using {Key}", apiKeyName);""");
    }

    [Fact]
    public async Task ACountIsNotASecret()
    {
        await VerifyAsync("""        _logger.LogInformation("tokens {Count}", tokenCount);""");
    }

    [Fact]
    public async Task AnOrdinaryValueIsNotReported()
    {
        await VerifyAsync("""        _logger.LogInformation("user {User}", userName);""");
    }

    [Fact]
    public async Task ASecretPassedToSomethingThatIsNotDiagnosticOutputIsNotReported()
    {
        // The rule is about disclosure into logs, not about handling secrets at all.
        await VerifyAsync("""        var length = dbPassword.Length;""");
    }
}
