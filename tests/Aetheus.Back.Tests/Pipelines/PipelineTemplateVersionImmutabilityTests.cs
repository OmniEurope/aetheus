// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests;

public sealed class PipelineTemplateVersionImmutabilityTests
{
    [Fact]
    public async Task SaveChangesAsync_ModifyingPublishedVersion_IsRejected()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);
        var template = new PipelineTemplate
        {
            Name = "ci",
            Category = "CI",
            OrganizationId = 1,
            LatestVersion = 1,
            Versions =
            [
                new PipelineTemplateVersion
                {
                    Version = 1,
                    YamlContent = "name: ci\nstages: []",
                    ChangelogEntry = "Initial version"
                }
            ]
        };
        db.PipelineTemplates.Add(template);
        await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        template.Versions.Single().YamlContent = "name: mutated\nstages: []";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("immutable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
