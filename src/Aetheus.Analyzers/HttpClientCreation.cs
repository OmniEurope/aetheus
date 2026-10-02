// SPDX-License-Identifier: EUPL-1.2
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aetheus.Analyzers;

/// <summary>
/// What SEC006 and SEC007 both need to know about an object creation: whether it builds an
/// <c>HttpClient</c>, and where the result is kept.
/// </summary>
internal static class HttpClientCreation
{
    private const string HttpClientType = "System.Net.Http.HttpClient";

    /// <summary>True when <paramref name="type"/> is <c>HttpClient</c> or derives from it.</summary>
    public static bool IsHttpClient(ITypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (current.ToDisplayString() == HttpClientType)
                return true;
        return false;
    }

    /// <summary>
    /// The syntax node that stands for the created value once parentheses, casts and the arms of a
    /// conditional are looked through: <c>x = cond ? new HttpClient() : new HttpClient(h)</c> stores
    /// both creations in <c>x</c>.
    /// </summary>
    public static SyntaxNode Outermost(SyntaxNode creation)
    {
        var node = creation;
        while (node.Parent is ParenthesizedExpressionSyntax or CastExpressionSyntax
               or ConditionalExpressionSyntax)
        {
            node = node.Parent;
        }
        return node;
    }
}
