// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Aetheus.Analyzers;

/// <summary>
/// SEC014, which replaces SEC001: an action that names a resource by id reaches
/// <c>IResourceAuthorizationService</c> before it returns that resource.
///
/// SEC001 filtered at the <c>DbSet</c> and raised 240 alerts, because the multi-tenant boundary is not
/// the repository: it is the controller or the service it calls. So this rule follows the call. An
/// action is satisfied when it, or any method of the compilation it calls (interfaces resolved to
/// their implementations in the same compilation), invokes the authorization service, and that call
/// happens before the first <c>return</c> that hands back something other than a refusal.
///
/// In scope: public actions of concrete controllers whose effective authorization is a plain
/// <c>[Authorize]</c> - an administrator-only or policy-bound endpoint is not a tenant one, while an
/// authentication scheme only says how an ordinary user signs in (Git over HTTP) and stays in scope -
/// and that take an integer or GUID parameter named <c>id</c> or ending in <c>Id</c>. An action that
/// deliberately needs no resource check says so with <c>[NotResourceScoped("why")]</c>, which the
/// rule accepts only with a reason.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SEC014_ResourceActionAuthorizesTheResourceAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SEC014";

    private const string AuthorizationServiceName = "IResourceAuthorizationService";
    private const string OptOutAttributeName = "NotResourceScopedAttribute";

    /// <summary>How far a call is followed; the services that authorize do it within two or three calls.</summary>
    private const int MaxDepth = 6;

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "An action naming a resource must authorize it",
        "Action '{0}' takes '{1}' but does not reach IResourceAuthorizationService before it returns data",
        "Aetheus.Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.CompilationEnd],
        description: "Any authenticated user can call an [Authorize] endpoint with any id. Unless the action, or a service it calls, checks the caller's permission on that resource first, the id is the only thing between one tenant and another's data.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var graph = new CallGraph();
            start.RegisterOperationBlockStartAction(block => CollectBlock(block, graph));
            start.RegisterCompilationEndAction(end => Report(end, graph));
        });
    }

    private sealed class CallGraph
    {
        public readonly ConcurrentDictionary<IMethodSymbol, ConcurrentBag<IMethodSymbol>> Calls = new(SymbolEqualityComparer.Default);
        public readonly ConcurrentBag<ActionBody> Actions = [];
    }

    private sealed class ActionBody(IMethodSymbol method, IParameterSymbol resource)
    {
        public IMethodSymbol Method { get; } = method;
        public IParameterSymbol Resource { get; } = resource;
        public ConcurrentBag<(IMethodSymbol Target, int Position)> Invocations { get; } = [];
        public ConcurrentBag<(int Start, int End)> DataReturns { get; } = [];
    }

    private static void CollectBlock(OperationBlockStartAnalysisContext block, CallGraph graph)
    {
        if (block.OwningSymbol is not IMethodSymbol method) return;
        var calls = graph.Calls.GetOrAdd(method.OriginalDefinition, _ => []);
        var action = CandidateResource(method) is { } resource ? new ActionBody(method, resource) : null;

        block.RegisterOperationAction(operation =>
        {
            var invocation = (IInvocationOperation)operation.Operation;
            var target = invocation.TargetMethod.OriginalDefinition;
            calls.Add(target);
            action?.Invocations.Add((target, invocation.Syntax.SpanStart));
        }, OperationKind.Invocation);

        if (action is null) return;
        block.RegisterOperationAction(operation =>
        {
            var returned = (IReturnOperation)operation.Operation;
            if (returned.ReturnedValue is { } value && !IsRefusal(value))
                action.DataReturns.Add((returned.Syntax.SpanStart, returned.Syntax.Span.End));
        }, OperationKind.Return);
        block.RegisterOperationBlockEndAction(_ => graph.Actions.Add(action));
    }

    private static void Report(CompilationAnalysisContext end, CallGraph graph)
    {
        var implementations = ImplementationsOf(graph);
        var memo = new Memo();
        foreach (var action in graph.Actions)
        {
            var firstData = action.DataReturns.Count == 0 ? (Start: int.MaxValue, End: int.MaxValue)
                : action.DataReturns.OrderBy(item => item.Start).First();
            var authorized = action.Invocations
                .Where(item => item.Position <= firstData.End)
                .Any(item => Reaches(item.Target, graph, implementations, memo, 0, []));
            if (authorized) continue;
            var location = action.Method.Locations.FirstOrDefault(item => item.IsInSource);
            if (location is null) continue;
            end.ReportDiagnostic(Diagnostic.Create(Rule, location, action.Method.Name, action.Resource.Name));
        }
    }

    /// <summary>Interface members mapped to the methods of the compilation that implement them.</summary>
    private static Dictionary<IMethodSymbol, List<IMethodSymbol>> ImplementationsOf(CallGraph graph)
    {
        var map = new Dictionary<IMethodSymbol, List<IMethodSymbol>>(SymbolEqualityComparer.Default);
        foreach (var type in graph.Calls.Keys.Select(method => method.ContainingType).Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default))
        {
            foreach (var member in type.AllInterfaces.SelectMany(face => face.GetMembers().OfType<IMethodSymbol>()))
            {
                if (type.FindImplementationForInterfaceMember(member) is not IMethodSymbol implementation) continue;
                var key = member.OriginalDefinition;
                if (!map.TryGetValue(key, out var list)) map[key] = list = [];
                list.Add(implementation.OriginalDefinition);
            }
        }
        return map;
    }

    private static bool Reaches(
        IMethodSymbol method, CallGraph graph, Dictionary<IMethodSymbol, List<IMethodSymbol>> implementations,
        Memo memo, int depth, HashSet<IMethodSymbol> path)
    {
        if (IsAuthorizationService(method.ContainingType)) return true;
        if (memo.Reaching.Contains(method)) return true;
        // Already explored from this depth or shallower (with at least this much budget) without success.
        if (memo.Exhausted.TryGetValue(method, out var exploredAt) && exploredAt <= depth) return false;
        if (depth >= MaxDepth || !path.Add(method)) return false;

        var next = graph.Calls.TryGetValue(method, out var calls) ? calls.ToList() : [];
        if (implementations.TryGetValue(method, out var implementing)) next.AddRange(implementing);
        var reaches = next.Any(target => Reaches(target, graph, implementations, memo, depth + 1, path));
        path.Remove(method);
        if (reaches) memo.Reaching.Add(method);
        else memo.Exhausted[method] = depth;
        return reaches;
    }

    private sealed class Memo
    {
        public readonly HashSet<IMethodSymbol> Reaching = new(SymbolEqualityComparer.Default);
        public readonly Dictionary<IMethodSymbol, int> Exhausted = new(SymbolEqualityComparer.Default);
    }

    private static bool IsAuthorizationService(INamedTypeSymbol? type) =>
        type is not null
        && (type.Name == AuthorizationServiceName || type.AllInterfaces.Any(face => face.Name == AuthorizationServiceName));

    /// <summary>Forbid(), NotFound(), Unauthorized() and the like return no resource.</summary>
    private static bool IsRefusal(IOperation value)
    {
        while (value is IConversionOperation conversion) value = conversion.Operand;
        if (value is IObjectCreationOperation creation)
            return creation.Type?.Name is "ForbidResult" or "NotFoundResult" or "UnauthorizedResult" or "BadRequestResult"
                or "BadRequestObjectResult" or "NotFoundObjectResult" or "ConflictResult" or "ConflictObjectResult"
                or "UnauthorizedObjectResult" or "UnprocessableEntityResult" or "UnprocessableEntityObjectResult";
        return value is IInvocationOperation invocation
            && invocation.TargetMethod.Name is "Forbid" or "NotFound" or "Unauthorized" or "BadRequest" or "Conflict"
                or "Problem" or "ValidationProblem" or "UnprocessableEntity" or "StatusCode" or "Challenge";
    }

    /// <summary>The resource id an in-scope action takes, or null when the action is out of scope.</summary>
    private static IParameterSymbol? CandidateResource(IMethodSymbol method)
    {
        if (method is not { DeclaredAccessibility: Accessibility.Public, MethodKind: MethodKind.Ordinary, IsStatic: false }) return null;
        var type = method.ContainingType;
        if (type is null || type.IsAbstract || !ControllerAuthorization.IsController(type)) return null;
        if (method.GetAttributes().Any(attribute => attribute.AttributeClass?.Name is "NonActionAttribute")) return null;
        if (DeclaresOptOut(method)) return null;
        if (!IsPlainAuthorize(method)) return null;
        return method.Parameters.FirstOrDefault(IsResourceId);
    }

    private static bool DeclaresOptOut(IMethodSymbol method) =>
        method.GetAttributes().Concat(method.ContainingType.GetAttributes()).Any(attribute =>
            attribute.AttributeClass?.Name == OptOutAttributeName
            && attribute.ConstructorArguments.FirstOrDefault().Value is string reason
            && reason.Trim().Length > 0);

    private static bool IsResourceId(IParameterSymbol parameter)
    {
        var name = parameter.Name;
        if (name != "id" && !(name.Length > 2 && name.EndsWith("Id", System.StringComparison.Ordinal))) return false;
        var type = parameter.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : parameter.Type;
        return type.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64 || type.Name == "Guid";
    }

    /// <summary>
    /// The attribute nearest the action decides: an action-level [Authorize] or [AllowAnonymous]
    /// overrides its controller, and a role or policy names a population that is not a tenant.
    /// </summary>
    private static bool IsPlainAuthorize(IMethodSymbol method)
    {
        var onMethod = method.GetAttributes().Where(ControllerAuthorization.IsAuthorizationAttribute).ToList();
        var effective = onMethod.Count > 0 ? onMethod : ClassAuthorization(method.ContainingType);
        if (effective.Count == 0) return false;
        if (effective.Any(attribute => attribute.AttributeClass?.Name == "AllowAnonymousAttribute")) return false;
        if (onMethod.Count > 0 && ClassAuthorization(method.ContainingType).Any(IsRestricted)) return false;
        return !effective.Any(IsRestricted);
    }

    private static List<AttributeData> ClassAuthorization(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var attributes = current.GetAttributes().Where(ControllerAuthorization.IsAuthorizationAttribute).ToList();
            if (attributes.Count > 0) return attributes;
        }
        return [];
    }

    private static bool IsRestricted(AttributeData attribute) =>
        attribute.NamedArguments.Any(argument => argument.Key is "Roles" or "Policy"
            && argument.Value.Value is string value && value.Length > 0)
        || attribute.ConstructorArguments.Any(argument => argument.Value is string value && value.Length > 0);
}
