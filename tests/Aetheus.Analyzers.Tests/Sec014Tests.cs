// SPDX-License-Identifier: EUPL-1.2
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace Aetheus.Analyzers.Tests;

/// <summary>
/// SEC014: an action naming a resource by id reaches IResourceAuthorizationService, directly or through
/// the service it calls, before it hands the resource back.
/// </summary>
public sealed class Sec014Tests
{
    private const string Stubs = """
        namespace Microsoft.AspNetCore.Mvc
        {
            public abstract class ControllerBase
            {
                public IActionResult Ok(object value) => null!;
                public IActionResult Forbid() => null!;
                public IActionResult NotFound() => null!;
            }
            public interface IActionResult { }
        }
        namespace Microsoft.AspNetCore.Authorization
        {
            public class AuthorizeAttribute : System.Attribute
            {
                public string? Roles { get; set; }
                public string? AuthenticationSchemes { get; set; }
            }
            public sealed class AllowAnonymousAttribute : System.Attribute { }
        }
        public interface IResourceAuthorizationService { bool HasPermission(int id); }
        public sealed class NotResourceScopedAttribute(string reason) : System.Attribute { }
        public interface IReleaseService { object Get(int id); object GetChecked(int id); }
        public sealed class ReleaseService(IResourceAuthorizationService authz) : IReleaseService
        {
            public object Get(int id) => id;
            public object GetChecked(int id) => authz.HasPermission(id) ? id : null!;
        }
        """;

    private static string Source(string controller) => $$"""
        using Microsoft.AspNetCore.Mvc;
        using Microsoft.AspNetCore.Authorization;
        {{Stubs}}
        {{controller}}
        """;

    private static Task VerifyAsync(string controller, params string[] reportedActions)
    {
        var source = Source(controller);
        var expected = reportedActions.Select(action => Locate(source, action)).ToArray();
        return Verifier<SEC014_ResourceActionAuthorizesTheResourceAnalyzer>.VerifyAsync("Controller.cs", source, expected);
    }

    /// <summary>The diagnostic sits on the action's name, in its declaration.</summary>
    private static DiagnosticResult Locate(string source, string action)
    {
        var lines = source.Split('\n');
        var controller = Array.FindIndex(lines, line => line.Contains("Controller(", StringComparison.Ordinal));
        for (var index = controller; index < lines.Length; index++)
        {
            var column = lines[index].IndexOf($" {action}(", StringComparison.Ordinal);
            if (column < 0 || !lines[index].Contains("public", StringComparison.Ordinal)) continue;
            return Verifier<SEC014_ResourceActionAuthorizesTheResourceAnalyzer>.Expect(
                "SEC014", DiagnosticSeverity.Warning, "Controller.cs",
                index + 1, column + 2, index + 1, column + 2 + action.Length, action, "id");
        }
        throw new InvalidOperationException($"No declaration of {action}");
    }

    [Fact]
    public async Task AnActionThatChecksTheResourceFirstIsAccepted()
    {
        await VerifyAsync("""
            [Authorize]
            public sealed class ReleasesController(IResourceAuthorizationService authz, IReleaseService service) : ControllerBase
            {
                public IActionResult Get(int id)
                {
                    if (!authz.HasPermission(id)) return Forbid();
                    return Ok(service.Get(id));
                }
            }
            """);
    }

    [Fact]
    public async Task AnActionThatNeverChecksIsReported()
    {
        await VerifyAsync("""
            [Authorize]
            public sealed class ReleasesController(IReleaseService service) : ControllerBase
            {
                public IActionResult Get(int id) => Ok(service.Get(id));
            }
            """, "Get");
    }

    /// <summary>The call is followed through the interface to the implementation that authorizes.</summary>
    [Fact]
    public async Task AnActionWhoseServiceChecksIsAccepted()
    {
        await VerifyAsync("""
            [Authorize]
            public sealed class ReleasesController(IReleaseService service) : ControllerBase
            {
                public IActionResult Get(int id) => Ok(service.GetChecked(id));
            }
            """);
    }

    [Fact]
    public async Task ACheckAfterTheDataWasReturnedIsReported()
    {
        await VerifyAsync("""
            [Authorize]
            public sealed class ReleasesController(IResourceAuthorizationService authz, IReleaseService service) : ControllerBase
            {
                public IActionResult Get(int id)
                {
                    var release = service.Get(id);
                    if (release is not null) return Ok(release);
                    if (!authz.HasPermission(id)) return Forbid();
                    return NotFound();
                }
            }
            """, "Get");
    }

    [Fact]
    public async Task AnAdministratorOnlyEndpointIsNotATenantOne()
    {
        await VerifyAsync("""
            [Authorize(Roles = "Admin")]
            public sealed class ReleasesController(IReleaseService service) : ControllerBase
            {
                public IActionResult Get(int id) => Ok(service.Get(id));
            }
            """);
    }

    /// <summary>A scheme only says how an ordinary user signs in (Git over HTTP): still a tenant endpoint.</summary>
    [Fact]
    public async Task ASchemeBoundEndpointIsStillATenantOne()
    {
        await VerifyAsync("""
            public sealed class GitController(IReleaseService service) : ControllerBase
            {
                [Authorize(AuthenticationSchemes = "GitBasic")]
                public IActionResult Get(int id) => Ok(service.Get(id));
            }
            """, "Get");
    }

    [Fact]
    public async Task AnActionWithoutAResourceIdIsOutOfScope()
    {
        await VerifyAsync("""
            [Authorize]
            public sealed class ReleasesController(IReleaseService service) : ControllerBase
            {
                public IActionResult Get(string name) => Ok(service.Get(name.Length));
            }
            """);
    }

    [Fact]
    public async Task AnOptOutIsAcceptedOnlyWithAReason()
    {
        await VerifyAsync("""
            [Authorize]
            public sealed class DashboardsController(IReleaseService service) : ControllerBase
            {
                [NotResourceScoped("Owned by the caller, filtered on their user id.")]
                public IActionResult Get(int id) => Ok(service.Get(id));

                [NotResourceScoped(" ")]
                public IActionResult Delete(int id) => Ok(service.Get(id));
            }
            """, "Delete");
    }
}
