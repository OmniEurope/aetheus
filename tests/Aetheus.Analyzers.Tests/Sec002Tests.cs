// SPDX-License-Identifier: EUPL-1.2
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace Aetheus.Analyzers.Tests;

/// <summary>
/// SEC002 asks a controller to say which it is. Both answers are accepted; only silence is not.
/// </summary>
public sealed class Sec002Tests
{
    private const string AspNetStub = """
        namespace Microsoft.AspNetCore.Mvc
        {
            public abstract class ControllerBase { }
            public sealed class ApiControllerAttribute : System.Attribute { }
        }
        namespace Microsoft.AspNetCore.Authorization
        {
            public class AuthorizeAttribute : System.Attribute { }
            public sealed class AllowAnonymousAttribute : System.Attribute { }
        }
        """;

    private static string Source(string declaration) => $$"""
        using Microsoft.AspNetCore.Mvc;
        using Microsoft.AspNetCore.Authorization;
        {{AspNetStub}}
        {{declaration}}
        """;

    private static Task VerifyAsync(string declaration, params DiagnosticResult[] expected) =>
        Verifier<SEC002_EndpointDeclaresItsAuthorizationAnalyzer>.VerifyAsync(
            "Controller.cs", Source(declaration), expected);

    [Fact]
    public async Task AControllerWithNoAuthorizationAttributeIsReported()
    {
        await VerifyAsync(
            """
            public sealed class ArtifactsController : ControllerBase
            {
            }
            """,
            Verifier<SEC002_EndpointDeclaresItsAuthorizationAnalyzer>.Expect(
                "SEC002", DiagnosticSeverity.Warning, "Controller.cs", 13, 21, 13, 40, "ArtifactsController"));
    }

    [Fact]
    public async Task AnAuthorizedControllerIsAccepted()
    {
        await VerifyAsync("""
            [Authorize]
            public sealed class ArtifactsController : ControllerBase
            {
            }
            """);
    }

    [Fact]
    public async Task AnExplicitlyAnonymousControllerIsAccepted()
    {
        // Anonymous is a legitimate decision; the rule only asks that it be written down.
        await VerifyAsync("""
            [AllowAnonymous]
            public sealed class HealthController : ControllerBase
            {
            }
            """);
    }

    [Fact]
    public async Task AControllerInheritingAnAuthorizedBaseIsAccepted()
    {
        await VerifyAsync("""
            [Authorize]
            public abstract class SecuredController : ControllerBase
            {
            }
            public sealed class ProjectsController : SecuredController
            {
            }
            """);
    }

    [Fact]
    public async Task AnAbstractBaseIsNotItselfReported()
    {
        // A base carries plumbing, not routes; the concrete controller is what must decide.
        await VerifyAsync("""
            public abstract class ApiBase : ControllerBase
            {
            }
            [Authorize]
            public sealed class ProjectsController : ApiBase
            {
            }
            """);
    }

    [Fact]
    public async Task AControllerThatDeclaresItPerActionIsAccepted()
    {
        // The Git smart-HTTP endpoints each name their own authentication scheme, which cannot be
        // hoisted to the class; declaring it on every action is equally explicit.
        await VerifyAsync("""
            public sealed class GitController : ControllerBase
            {
                [Authorize]
                public int InfoRefs() => 0;
                [Authorize]
                public int UploadPack() => 0;
            }
            """);
    }

    [Fact]
    public async Task AControllerWithOneUndeclaredActionIsStillReported()
    {
        await VerifyAsync("""
            public sealed class GitController : ControllerBase
            {
                [Authorize]
                public int InfoRefs() => 0;
                public int ReceivePack() => 0;
            }
            """,
            Verifier<SEC002_EndpointDeclaresItsAuthorizationAnalyzer>.Expect(
                "SEC002", DiagnosticSeverity.Warning, "Controller.cs", 13, 21, 13, 34, "GitController"));
    }

    [Fact]
    public async Task AnOrdinaryClassIsNotAController()
    {
        await VerifyAsync("""
            public sealed class ArtifactService
            {
            }
            """);
    }
}
