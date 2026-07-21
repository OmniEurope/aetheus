// SPDX-License-Identifier: EUPL-1.2
using System.Net;

namespace Aetheus.Back.IntegrationTests;

/// <summary>
/// Authenticated read sweep: as the bootstrap admin, every top-level collection GET must
/// return a concrete <c>200 OK</c> with a non-empty body through the real pipeline. Unlike the
/// anonymous sweep (which short-circuits at the auth middleware), this drives the full
/// controller → service → repository → Postgres path for each module, so a broken EF query, a
/// missing migration column, or a DI wiring fault surfaces as a concrete failing route rather
/// than a vague 500 somewhere in prod.
/// <para>
/// The assertion is <c>== 200</c> (not merely <c>IsSuccessStatusCode</c>) plus a non-empty body
/// on purpose: a route silently regressing to <c>204 No Content</c>, or a 200 whose serialization
/// collapsed to an empty body, is a real defect this sweep must catch rather than wave through as
/// "reachable". This is a reachability + content-presence check, NOT a projection check - a body
/// of <c>[]</c>/<c>{}</c> is non-empty and passes (an empty seeded DB legitimately yields those),
/// so it does not detect an EF projection that wrongly collapsed populated data to an empty list.
/// </para>
/// </summary>
[Collection(ApiSmokeCollection.Name)]
public sealed class ApiReadSmokeTests(ApiSmokeFixture fixture)
{
    [Theory]
    [InlineData("/api/projects")]
    [InlineData("/api/servers")]
    [InlineData("/api/servers/names")]
    [InlineData("/api/users")]
    [InlineData("/api/users/me")]
    [InlineData("/api/users/roles")]
    [InlineData("/api/roles")]
    [InlineData("/api/organizations")]
    [InlineData("/api/organizations/me")]
    [InlineData("/api/agent-pools")]
    [InlineData("/api/audit")]
    [InlineData("/api/audit/actions")]
    [InlineData("/api/audit/entity-types")]
    [InlineData("/api/dashboards")]
    [InlineData("/api/alerts")]
    [InlineData("/api/monitoring/dashboard")]
    [InlineData("/api/plugins")]
    [InlineData("/api/package-feeds")]
    [InlineData("/api/system-logs/files")]
    [InlineData("/api/releases")]
    [InlineData("/api/pipelines")]
    [InlineData("/api/pipelines/templates")]
    [InlineData("/api/service-connections")]
    [InlineData("/api/test-suites")]
    [InlineData("/api/webhooks")]
    [InlineData("/api/vaults")]
    [InlineData("/api/vaults/names")]
    [InlineData("/api/tasks")]
    [InlineData("/api/tasks/active")]
    [InlineData("/api/variable-libraries")]
    [InlineData("/api/variable-libraries/names")]
    [InlineData("/api/settings")]
    [InlineData("/api/environments")]
    [InlineData("/api/git/repos")]
    [InlineData("/api/git/connections")]
    [InlineData("/api/notifications/channels")]
    [InlineData("/api/auth/registration-tokens")]
    [InlineData("/api/personal-access-tokens")]
    [InlineData("/api/backups")]
    // Covers both AppMonitoringController and AppTelemetryController (shared api/appmonitoring prefix).
    [InlineData("/api/appmonitoring/summary")]
    [InlineData("/metrics")]
    public async Task CollectionGetEndpoint_AsAdmin_ReturnsNonEmpty200(string route)
    {
        using var client = fixture.CreateAdminClient();

        using var response = await client.GetAsync(route, cancellationToken: TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"GET {route} returned {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(body)}");
        Assert.False(string.IsNullOrWhiteSpace(body),
            $"GET {route} returned 200 with an empty body - broken projection or no-content regression.");
    }

    // --- server-scoped read paths -------------------------------------------------------------
    // The 7 server-management modules (Certbot/Docker/Apache/Mail/Portsentry/Rkhunter/Teamspeak)
    // plus modules/apps were previously excluded from the admin read sweep on the assumption that
    // their GETs hit the live agent and would flake. That assumption is false: every GET here
    // reads cached snapshots from its repository (only the POST actions queue agent tasks), so
    // they are safe deterministic read paths and a 500 (e.g. broken EF projection) must surface.

    [Theory]
    [InlineData("certbot")]
    [InlineData("docker/containers")]
    [InlineData("apache")]
    [InlineData("mail")]
    [InlineData("portsentry")]
    [InlineData("rkhunter")]
    [InlineData("teamspeak")]
    [InlineData("modules")]
    [InlineData("apps")]
    [InlineData("module-links")]
    public async Task ServerScopedGet_AsAdmin_ReturnsNonEmpty200(string suffix)
    {
        using var client = fixture.CreateAdminClient();

        using var response = await client.GetAsync($"/api/servers/{fixture.ServerId}/{suffix}", cancellationToken: TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"GET /api/servers/{{id}}/{suffix} returned {(int)response.StatusCode}. Body: {Truncate(body)}");
        Assert.False(string.IsNullOrWhiteSpace(body),
            $"GET /api/servers/{{id}}/{suffix} returned 200 with an empty body.");
    }

    // --- project-scoped read paths ------------------------------------------------------------
    // These require a real project id, so they live as dedicated cases (not the parametrized
    // top-level sweep). work-items additionally requires ?projectId - the controller now refuses
    // an unscoped listing - so this also re-covers the read path that was dropped when the route
    // was removed from the sweep rather than fixed with a seeded projectId.

    [Theory]
    [InlineData("/api/work-items?projectId=")]
    [InlineData("/api/artifacts/project/")]
    [InlineData("/api/gitgraph/project/")]
    public async Task ProjectScopedGet_AsAdmin_ReturnsNonEmpty200(string prefix)
    {
        using var client = fixture.CreateAdminClient();

        using var response = await client.GetAsync($"{prefix}{fixture.ProjectId}", cancellationToken: TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"GET {prefix}{{projectId}} returned {(int)response.StatusCode}. Body: {Truncate(body)}");
        Assert.False(string.IsNullOrWhiteSpace(body),
            $"GET {prefix}{{projectId}} returned 200 with an empty body.");
    }

    private static string Truncate(string value) =>
        value.Length <= 300 ? value : value[..300];
}
