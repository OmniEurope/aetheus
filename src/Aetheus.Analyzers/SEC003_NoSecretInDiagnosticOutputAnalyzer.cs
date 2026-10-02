// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aetheus.Analyzers;

/// <summary>
/// SEC003: a value whose own name says it is a secret must not be written to a log, to the console,
/// or into an exception message.
///
/// This is the class of defect a generic scanner cannot find in this codebase, because it is not a
/// dangerous API call: every one of these calls is legitimate, and only the argument makes it a leak.
/// Aetheus holds deployment credentials, vault secrets, JWT keys and ingest keys, so a single
/// interpolated password in a warning is a durable disclosure into log storage that outlives the run.
///
/// The rule is deliberately name-based and therefore approximate in one direction only: it can miss a
/// secret held in a badly named variable, and it does not claim otherwise. What it must not do is
/// fire on something harmless, so it ignores anything that is obviously an identifier rather than a
/// value (a *KeyName, *TokenId, *SecretName, or a plain "key" used as a dictionary key).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SEC003_NoSecretInDiagnosticOutputAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SEC003";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Do not write a secret to a log, the console or an exception message",
        "'{0}' names a secret and is written to {1}; log an identifier or a masked form instead",
        "Aetheus.Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Diagnostic output is persisted and read by more people than the value itself. A password, key, token or secret written there is disclosed for as long as the log is kept.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeThrow, SyntaxKind.ObjectCreationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var sink = DescribeSink(invocation, context);
        if (sink is null) return;

        foreach (var argument in invocation.ArgumentList.Arguments)
            ReportSecretsIn(argument.Expression, sink, context);
    }

    private static void AnalyzeThrow(SyntaxNodeAnalysisContext context)
    {
        var creation = (ObjectCreationExpressionSyntax)context.Node;
        if (context.SemanticModel.GetSymbolInfo(creation, context.CancellationToken).Symbol
            is not IMethodSymbol constructor
            || !InheritsFromException(constructor.ContainingType))
        {
            return;
        }

        foreach (var argument in creation.ArgumentList?.Arguments ?? default)
            ReportSecretsIn(argument.Expression, "an exception message", context);
    }

    /// <summary>Names the sink when this call writes diagnostics, or null when it does not.</summary>
    private static string? DescribeSink(InvocationExpressionSyntax invocation, SyntaxNodeAnalysisContext context)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax member) return null;
        var name = member.Name.Identifier.Text;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method)
        {
            return null;
        }

        var containing = method.ContainingType?.ToDisplayString() ?? string.Empty;
        if (containing == "System.Console"
            && name is "Write" or "WriteLine" or "Error" or "Out")
        {
            return "the console";
        }
        // ILogger.Log*, and the LoggerExtensions that forward to it. Matching the method name plus
        // the Microsoft.Extensions.Logging namespace keeps this to real logging calls rather than any
        // method that happens to be called LogSomething.
        if (name.StartsWith("Log", System.StringComparison.Ordinal)
            && containing.StartsWith("Microsoft.Extensions.Logging", System.StringComparison.Ordinal))
        {
            return "a log";
        }
        return null;
    }

    private static void ReportSecretsIn(
        ExpressionSyntax expression, string sink, SyntaxNodeAnalysisContext context)
    {
        foreach (var identifier in expression.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
        {
            // The member being accessed, not the instance: `options.Password` is the secret, and
            // `options` is not.
            if (identifier.Parent is MemberAccessExpressionSyntax parent && parent.Name != identifier) continue;
            if (!NamesASecret(identifier.Identifier.Text)) continue;
            if (context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol
                is not ({ Kind: SymbolKind.Local or SymbolKind.Parameter or SymbolKind.Field or SymbolKind.Property } and var symbol))
            {
                continue;
            }
            if (!IsSecretValued(symbol)) continue;
            context.ReportDiagnostic(Diagnostic.Create(
                Rule, identifier.GetLocation(), identifier.Identifier.Text, sink));
        }
    }

    /// <summary>
    /// A name that says "this holds a secret". Suffixes that turn it into an identifier, a length, a
    /// version or a flag are excluded: those are exactly the values one SHOULD log to make a secret
    /// traceable without disclosing it.
    /// </summary>
    /// <summary>Suffixes that turn a secret's name into something about it: an identifier, a length,
    /// a version, a flag. Those are exactly what one SHOULD log to make a secret traceable.</summary>
    private static readonly string[] AboutASecretSuffixes =
        ["name", "names", "id", "ids", "length", "version", "hash", "count", "path", "file", "keys", "enabled"];

    private static readonly string[] SecretSubstrings = ["password", "passphrase", "secret", "credential"];

    private static readonly string[] SecretSuffixes =
        ["token", "apikey", "privatekey", "ingestkey", "jwtkey", "encryptionkey"];

    private static bool NamesASecret(string name)
    {
        if (name.Length == 0) return false;
        var lowered = name.ToLowerInvariant();
        if (EndsWithAny(lowered, AboutASecretSuffixes)) return false;
        return System.Array.Exists(SecretSubstrings, part => lowered.Contains(part))
            || EndsWithAny(lowered, SecretSuffixes);
    }

    private static bool EndsWithAny(string value, string[] suffixes) =>
        System.Array.Exists(suffixes, suffix => value.EndsWith(suffix, System.StringComparison.Ordinal));

    /// <summary>
    /// Only a value that could actually carry a secret. A bool, an int or an enum named
    /// <c>HasPassword</c> or <c>TokenKind</c> discloses nothing, and reporting it would be the noise
    /// that gets a rule switched off.
    /// </summary>
    private static bool IsSecretValued(ISymbol symbol)
    {
        var type = symbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null
        };
        if (type is null) return false;
        if (type.SpecialType is SpecialType.System_String or SpecialType.System_Object) return true;
        // char[] and byte[] hold key material just as well as a string does.
        return type is IArrayTypeSymbol array
            && array.ElementType.SpecialType is SpecialType.System_Char or SpecialType.System_Byte;
    }

    private static bool InheritsFromException(INamedTypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (current.ToDisplayString() == "System.Exception")
                return true;
        return false;
    }
}
