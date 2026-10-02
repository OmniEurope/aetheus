// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Aetheus.Back.Components.Artifacts;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Back.IntegrationTests;

/// <summary>
/// End-to-end coverage for <c>GET /api/artifacts/{id}/download</c> through the full pipeline
/// (auth → controller → service → on-disk storage) against Testcontainers PostgreSQL: a real
/// artifact file is written via <see cref="IArtifactStorageService"/>, then streamed back and its
/// bytes asserted. The InMemory suite can't exercise the file streaming or the real auth flow.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ArtifactDownloadIntegrationTests(PostgresFixture fixture)
{
    private static Task<string> LoginAsync(HttpClient client)
        => IntegrationAuth.LoginAsAdminAsync(client, TestContext.Current.CancellationToken);

    [Fact]
    public async Task DownloadArtifact_StreamsStoredFileContent()
    {
        await using var factory = new AetheusWebApplicationFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client));

        var payload = Encoding.UTF8.GetBytes("artifact-bytes-payload");
        int artifactId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IArtifactStorageService>();

            // The pipeline needs a real owning project: authorization resolves the artifact's owner
            // transitively through it (ArtifactRepository.GetArtifactOwningProjectIdAsync), and an
            // owner that cannot be named is refused. Before F-003 a null owner skipped the permission
            // check entirely, which is what this fixture used to rely on.
            var organizationId = await db.Organizations.OrderBy(o => o.Id).Select(o => o.Id)
                .FirstAsync(cancellationToken: TestContext.Current.CancellationToken);
            var project = new Project { Name = "dl-project", OrganizationId = organizationId };
            db.Projects.Add(project);
            await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            var pipeline = new Pipeline { Name = "dl-pipeline", ProjectId = project.Id, YamlDefinition = "stages: []" };
            db.Pipelines.Add(pipeline);
            await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            var run = new PipelineRun { PipelineId = pipeline.Id, Status = PipelineStatus.Success };
            db.PipelineRuns.Add(run);
            await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            var (relativePath, sha256) = await storage.SaveArtifactAsync(1, pipeline.Id, run.Id, "drop.zip", new MemoryStream(payload), ct: TestContext.Current.CancellationToken);

            var artifact = new PipelineArtifact
            {
                PipelineRunId = run.Id,
                PipelineId = pipeline.Id,
                ProjectId = null, // realistic: the artifact's own column is null, the owner comes from the pipeline
                Name = "drop",
                FilePath = relativePath,
                SizeBytes = payload.Length,
                Sha256 = sha256,
                CreatedAt = DateTime.UtcNow,
                RetentionExpiresAt = DateTime.UtcNow.AddDays(30)
            };
            db.PipelineArtifacts.Add(artifact);
            await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
            artifactId = artifact.Id;
        }

        var response = await client.GetAsync($"/api/artifacts/{artifactId}/download", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(payload, await response.Content.ReadAsByteArrayAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadArtifact_MissingArtifact_Returns404()
    {
        await using var factory = new AetheusWebApplicationFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client));

        var response = await client.GetAsync("/api/artifacts/999999/download", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
