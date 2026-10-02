// SPDX-License-Identifier: EUPL-1.2
using Microsoft.CodeAnalysis;

namespace Aetheus.Analyzers;

/// <summary>
/// What SEC002 and SEC014 both need to know about MVC authorization: whether a type is a controller,
/// and whether an attribute is <c>[Authorize]</c> or <c>[AllowAnonymous]</c> (or derives from one).
/// </summary>
internal static class ControllerAuthorization
{
    public static bool IsController(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            var name = current.ToDisplayString();
            if (name is "Microsoft.AspNetCore.Mvc.ControllerBase" or "Microsoft.AspNetCore.Mvc.Controller")
                return true;
        }
        return false;
    }

    public static bool IsAuthorizationAttribute(AttributeData attribute)
    {
        for (var current = attribute.AttributeClass; current is not null; current = current.BaseType)
        {
            var name = current.ToDisplayString();
            if (name is "Microsoft.AspNetCore.Authorization.AuthorizeAttribute"
                or "Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute")
            {
                return true;
            }
        }
        return false;
    }
}
