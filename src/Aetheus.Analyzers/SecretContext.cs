// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Aetheus.Analyzers;

/// <summary>
/// Decides whether an expression is producing something secret, from the names around it (SEC009).
///
/// The names read are the ones a reader would read: what the value is stored in (a local, a field, a
/// property, a parameter it is passed to), up to the statement that holds it, plus the name of the
/// enclosing method, local function or property. A name is split into its words
/// (<c>resetTokenHash</c> is reset, token, hash) and matches when one word is a secret word.
///
/// <c>key</c> alone is not a secret word: a dictionary, cache or idempotency key is the common case
/// and hashing one with SHA-1 is harmless. It counts only when qualified (<c>apiKey</c>,
/// <c>signingKey</c>, <c>privateKey</c>...). <c>token</c> after <c>cancellation</c> is not one either.
/// </summary>
internal static class SecretContext
{
    private static readonly HashSet<string> SecretWords = new(System.StringComparer.Ordinal)
    {
        "token", "tokens", "secret", "secrets", "password", "passwords", "passwd", "passphrase",
        "salt", "nonce", "otp", "credential", "credentials",
        // An all-caps acronym does not split ("APIKey" is one word), so the fused form is listed.
        "apikey"
    };

    private static readonly HashSet<string> KeyQualifiers = new(System.StringComparer.Ordinal)
    {
        "api", "private", "signing", "encryption", "secret", "hmac", "jwt", "master", "session",
        "access", "ingest", "crypto", "aes", "symmetric", "shared"
    };

    /// <summary>The first name around <paramref name="expression"/> that says it is a secret.</summary>
    public static string? FindSecretName(SyntaxNode expression, SyntaxNodeAnalysisContext context) =>
        NamesAround(expression, context).FirstOrDefault(IsSecretName);

    private static IEnumerable<string> NamesAround(SyntaxNode expression, SyntaxNodeAnalysisContext context)
    {
        for (var node = expression.Parent; node is not null; node = node.Parent)
        {
            var name = DestinationName(node, context);
            if (name is not null) yield return name;
            if (node is StatementSyntax or MemberDeclarationSyntax or AnonymousFunctionExpressionSyntax) break;
        }
        var enclosing = EnclosingMemberName(expression);
        if (enclosing is not null) yield return enclosing;
    }

    private static string? DestinationName(SyntaxNode node, SyntaxNodeAnalysisContext context) => node switch
    {
        VariableDeclaratorSyntax declarator => declarator.Identifier.Text,
        AssignmentExpressionSyntax assignment => RightmostName(assignment.Left),
        ArgumentSyntax argument =>
            (context.SemanticModel.GetOperation(argument, context.CancellationToken) as IArgumentOperation)
                ?.Parameter?.Name,
        _ => null
    };

    private static string? RightmostName(ExpressionSyntax expression) => expression switch
    {
        SimpleNameSyntax simple => simple.Identifier.Text,
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        ElementAccessExpressionSyntax element => RightmostName(element.Expression),
        _ => null
    };

    private static string? EnclosingMemberName(SyntaxNode expression)
    {
        foreach (var node in expression.Ancestors())
        {
            switch (node)
            {
                case MethodDeclarationSyntax method: return method.Identifier.Text;
                case LocalFunctionStatementSyntax local: return local.Identifier.Text;
                case PropertyDeclarationSyntax property: return property.Identifier.Text;
                case TypeDeclarationSyntax: return null;
            }
        }
        return null;
    }

    public static bool IsSecretName(string name)
    {
        var words = SplitWords(name);
        for (var i = 0; i < words.Count; i++)
        {
            var previous = i > 0 ? words[i - 1] : string.Empty;
            if (IsSecretWord(words[i], previous)) return true;
        }
        return false;
    }

    private static bool IsSecretWord(string word, string previous)
    {
        if (word is "token" or "tokens" && previous == "cancellation") return false;
        if (word is "key" or "keys") return KeyQualifiers.Contains(previous);
        return SecretWords.Contains(word);
    }

    /// <summary>camelCase, PascalCase, snake_case and digits all split into lower-case words.</summary>
    private static List<string> SplitWords(string name)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var character in name)
        {
            var boundary = !char.IsLetter(character)
                || (char.IsUpper(character) && current.Length > 0 && !char.IsUpper(current[current.Length - 1]));
            if (boundary && current.Length > 0)
            {
                words.Add(current.ToString().ToLowerInvariant());
                current.Clear();
            }
            if (char.IsLetter(character)) current.Append(character);
        }
        if (current.Length > 0) words.Add(current.ToString().ToLowerInvariant());
        return words;
    }
}
