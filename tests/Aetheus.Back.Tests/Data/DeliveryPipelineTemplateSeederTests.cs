// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Aetheus.Back.Tests.DataSeed;

public sealed class DeliveryPipelineTemplateSeederTests
{
    [Theory]
    [InlineData("application-candidate-v1.yaml", "e518dbb44946507c765c7206e1351b9cc03b2fd25e3c4be60a7783c9288d5788")]
    [InlineData("application-candidate-v2.yaml", "45879dce840b5a204df957296008c22e0f84b0788cac6e0329055abf8d987d14")]
    [InlineData("application-candidate-v3.yaml", "8467249a0e54eafbb80f65b936fe9b151f1ec2f6c9bb1234959a8730bcb42ac0")]
    [InlineData("application-candidate-v4.yaml", "7dcfbc0fc77892dad9e67b5011f659116daf3548a57ae1b7fbaed06fac2cb22a")]
    [InlineData("application-candidate-v5.yaml", "eabb46f37ec19b0fdcf3bad1a53e279b5296b5c086a1c7496b68c11141464064")]
    [InlineData("application-candidate-v6.yaml", "7b3b57fb0d4b9b94621bf74df45a87b10f047283150eeb72265ea62c7b5a4037")]
    [InlineData("application-candidate-v7.yaml", "40d0be0b54823f6096eae8b03017ee8d9e3ad3da3e42429468fe6d1d1a006e10")]
    public void CandidatePublishedVersions_HaveStableContent(string resource, string expectedSha256)
    {
        var normalized = DeliveryPipelineTemplateSeeder.ReadResource(resource)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();
        var actual = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant();

        Assert.Equal(expectedSha256, actual);
    }

    [Fact]
    public async Task SeedAsync_IsIdempotentAndPublishesPinnedImmutableTemplates()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(options);
        db.Organizations.Add(new Organization
        {
            Name = "Aetheus",
            Slug = "aetheus",
            CreatedAt = DateTime.UnixEpoch,
            UpdatedAt = DateTime.UnixEpoch
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var clock = new FakeTimeProvider(
            new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));
        var seeder = new DeliveryPipelineTemplateSeeder(db, clock);

        await seeder.SeedAsync(TestContext.Current.CancellationToken);
        await seeder.SeedAsync(TestContext.Current.CancellationToken);

        var templates = await db.PipelineTemplates
            .Include(template => template.Versions)
            .OrderBy(template => template.Name)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(DeliveryPipelineTemplateSeeder.Definitions.Count, templates.Count);
        Assert.All(templates, template =>
            Assert.All(template.Versions, version =>
                Assert.Contains("stages:", version.YamlContent, StringComparison.Ordinal)));
        Assert.Contains(templates, template => template.Name == "application-light");
        Assert.Contains(templates, template => template.Name == "application-ci");
        Assert.Contains(templates, template => template.Name == "application-quality");
        Assert.Contains(templates, template => template.Name == "application-security");
        Assert.Contains(templates, template => template.Name == "application-qa");
        Assert.Contains(templates, template => template.Name == "application-nightly");
        Assert.Contains(templates, template => template.Name == "application-release-fast");
        Assert.Contains(templates, template => template.Name == "publish-observability-packages");
        Assert.DoesNotContain(templates, template => template.Name.Contains("aetheus", StringComparison.OrdinalIgnoreCase));
        var candidate = Assert.Single(templates, template => template.Name == "application-candidate");
        Assert.Equal(7, candidate.LatestVersion);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], candidate.Versions.OrderBy(version => version.Version).Select(version => version.Version));
        var promotion = Assert.Single(templates, template => template.Name == "application-promotion");
        Assert.Equal(2, promotion.LatestVersion);
        Assert.Equal([1, 2], promotion.Versions.OrderBy(version => version.Version).Select(version => version.Version));
        var light = Assert.Single(templates, template => template.Name == "application-light");
        Assert.Equal(1, light.LatestVersion);
        Assert.Equal(1, Assert.Single(light.Versions).Version);
        // v1 could describe an environment but not the run acting on it, so v2 adds the Compose
        // inputs; v2 then handed that one list to every blue-green step including the identity only
        // the starting step receives, so v3 separates the cutover's list; v3 still handed the cutover
        // list to the rollback, which fires on runs that never produced those tags, so v4 gives the
        // rollback its own. All four stay published: a version, once seeded, is immutable.
        var blueGreen = Assert.Single(templates, template => template.Name == "host-bluegreen-deploy");
        Assert.Equal(4, blueGreen.LatestVersion);
        Assert.Equal([1, 2, 3, 4], blueGreen.Versions.OrderBy(version => version.Version).Select(version => version.Version));
    }

    [Fact]
    public async Task SeedAsync_PreservesRecognizedHistoricalCandidateVersionCollision()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(options);
        db.Organizations.Add(new Organization
        {
            Name = "Aetheus",
            Slug = "aetheus",
            CreatedAt = DateTime.UnixEpoch,
            UpdatedAt = DateTime.UnixEpoch
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var organizationId = await db.Organizations
            .Select(organization => organization.Id)
            .SingleAsync(TestContext.Current.CancellationToken);
        var historicalYaml = DeliveryPipelineTemplateSeeder.ReadResource(
            "application-candidate-v3-legacy-advisory.yaml");
        db.PipelineTemplates.Add(CreateHistoricalCandidate(organizationId, historicalYaml));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var seeder = new DeliveryPipelineTemplateSeeder(db, TimeProvider.System);

        await seeder.SeedAsync(TestContext.Current.CancellationToken);

        var candidate = await db.PipelineTemplates
            .Include(template => template.Versions)
            .SingleAsync(template => template.Name == "application-candidate", TestContext.Current.CancellationToken);
        var versionThree = Assert.Single(candidate.Versions, version => version.Version == 3);
        Assert.Equal(historicalYaml, versionThree.YamlContent);
        Assert.Equal(7, candidate.LatestVersion);
    }

    [Fact]
    public async Task SeedAsync_RejectsUnknownImmutableVersionMutation()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(options);
        db.Organizations.Add(new Organization
        {
            Name = "Aetheus",
            Slug = "aetheus",
            CreatedAt = DateTime.UnixEpoch,
            UpdatedAt = DateTime.UnixEpoch
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var organizationId = await db.Organizations
            .Select(organization => organization.Id)
            .SingleAsync(TestContext.Current.CancellationToken);
        db.PipelineTemplates.Add(CreateHistoricalCandidate(organizationId, "stages: []"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var seeder = new DeliveryPipelineTemplateSeeder(db, TimeProvider.System);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => seeder.SeedAsync(TestContext.Current.CancellationToken));
        Assert.Contains("differs from its immutable published version", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CandidateTemplate_UsesProductionBaselineAndBuildOnceReleaseContract()
    {
        var versionOne = DeliveryPipelineTemplateSeeder.ReadResource("application-candidate-v1.yaml");
        var versionTwo = DeliveryPipelineTemplateSeeder.ReadResource("application-candidate-v2.yaml");
        var versionThree = DeliveryPipelineTemplateSeeder.ReadResource("application-candidate-v3.yaml");
        var versionFour = DeliveryPipelineTemplateSeeder.ReadResource("application-candidate-v4.yaml");
        var yaml = DeliveryPipelineTemplateSeeder.ReadResource("application-candidate-v5.yaml");

        Assert.Contains("release: previous-deployed", versionOne, StringComparison.Ordinal);
        Assert.Contains("release: current-deployed", versionTwo, StringComparison.Ordinal);
        Assert.DoesNotContain("isolation:", versionTwo, StringComparison.Ordinal);
        Assert.Contains("mode: container", versionThree, StringComparison.Ordinal);
        Assert.Contains("network: none", versionThree, StringComparison.Ordinal);
        Assert.DoesNotContain("continue_on_error: true", versionThree, StringComparison.Ordinal);
        Assert.Contains("release: current-deployed", versionFour, StringComparison.Ordinal);
        Assert.Contains("network: none", versionFour, StringComparison.Ordinal);
        Assert.Contains("DELIVERY_BASELINE_CONTRACT_SHA256", versionFour, StringComparison.Ordinal);
        Assert.Contains("mode: container", versionFour, StringComparison.Ordinal);
        Assert.Contains("node:24.4.1-alpine@sha256:", versionFour, StringComparison.Ordinal);
        Assert.Contains("network: none", versionFour, StringComparison.Ordinal);
        Assert.Contains("artifact_source_pipeline: \"$(APPLICATION_CI_PIPELINE)\"", versionFour, StringComparison.Ordinal);
        Assert.Contains("deployed: false", versionFour, StringComparison.Ordinal);
        Assert.Equal(2, versionFour.Split("continue_on_error: true", StringSplitOptions.None).Length - 1);
        Assert.Contains("name: cacheScenario", yaml, StringComparison.Ordinal);
        Assert.Contains("APPLICATION_ARTIFACT_NAME: ApplicationPayload-artifacts", yaml, StringComparison.Ordinal);
        Assert.Contains("APPLICATION_GITLEAKS_MODE: release-range", yaml, StringComparison.Ordinal);
        Assert.Contains("APPLICATION_FORMAT_MODE: affected", yaml, StringComparison.Ordinal);
        Assert.Contains("depends_on: [BaselineIdentity]", yaml, StringComparison.Ordinal);
        Assert.Contains("APPLICATION_CACHE_SCENARIO: \"${{ parameters.cacheScenario }}\"", yaml, StringComparison.Ordinal);
        Assert.Contains("APPLICATION_GITLEAKS_MODE: \"$(APPLICATION_GITLEAKS_MODE)\"", yaml, StringComparison.Ordinal);
        Assert.Contains("APPLICATION_FORMAT_MODE: \"$(APPLICATION_FORMAT_MODE)\"", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void LatestCandidateTemplate_CompletesAssuranceBeforePublishingRelease()
    {
        var yaml = DeliveryPipelineTemplateSeeder.ReadResource("application-candidate-v7.yaml");
        var candidate = Assert.IsType<PipelineYamlDefinition>(
            YamlParsingHelper.Deserializer.Deserialize<PipelineYamlDefinition>(yaml));

        Assert.Equal(["CI", "Quality", "Security", "QA", "Candidate"],
            candidate.Stages.Select(stage => stage.Name));
        Assert.Equal(["CI"], candidate.Stages.Single(stage => stage.Name == "Quality").DependsOn);
        Assert.Equal(["Quality"], candidate.Stages.Single(stage => stage.Name == "Security").DependsOn);
        Assert.Equal(["Security"], candidate.Stages.Single(stage => stage.Name == "QA").DependsOn);
        Assert.Equal(["QA"], candidate.Stages.Single(stage => stage.Name == "Candidate").DependsOn);
        Assert.All(candidate.Stages.Take(4).SelectMany(stage => stage.Steps),
            step => Assert.Equal("trigger", step.Type));
        var release = Assert.Single(candidate.Stages.Single(stage => stage.Name == "Candidate").Steps);
        Assert.Equal("release", release.Type);
        Assert.False(release.Deployed);
    }

    [Fact]
    public void PromotionTemplate_RequiresAnExplicitImmutableCandidateVersion()
    {
        var versionOne = DeliveryPipelineTemplateSeeder.ReadResource("application-promotion-v1.yaml");
        var versionTwo = DeliveryPipelineTemplateSeeder.ReadResource("application-promotion-v2.yaml");

        Assert.Contains("default: \"c-$(BUILD_SOURCEVERSION)\"", versionOne, StringComparison.Ordinal);
        Assert.Contains("required: true", versionTwo, StringComparison.Ordinal);
        Assert.DoesNotContain("default: \"c-$(BUILD_SOURCEVERSION)\"", versionTwo, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericPipelineTemplates_UseProjectAdaptersWithoutAetheusImplementationDetails()
    {
        var yaml = DeliveryPipelineTemplateSeeder.ReadResource("generic/application-nightly.yaml");
        var packages = DeliveryPipelineTemplateSeeder.ReadResource(
            "generic/publish-observability-packages.yaml");

        Assert.Contains("name: application-nightly-template", yaml, StringComparison.Ordinal);
        Assert.Contains("schedule: \"0 0 2 * * *\"", yaml, StringComparison.Ordinal);
        Assert.Contains("APPLICATION_CI_PIPELINE", yaml, StringComparison.Ordinal);
        Assert.Contains("APPLICATION_QUALITY_PIPELINE", yaml, StringComparison.Ordinal);
        Assert.Contains("APPLICATION_SECURITY_PIPELINE", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CANDIDATE", yaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("type: release", yaml, StringComparison.Ordinal);
        Assert.Contains(".pipeline/scripts/publish-packages.sh", packages, StringComparison.Ordinal);
        Assert.DoesNotContain("Aetheus", yaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Aetheus", packages, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GenericPipelineTemplates_AreAdapterBasedAndContainNoAetheusProductPaths()
    {
        var genericResources = DeliveryPipelineTemplateSeeder.Definitions.Values
            .SelectMany(definition => definition.Resources)
            .Where(resource => resource.StartsWith("generic/", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(11, genericResources.Length);
        foreach (var resource in genericResources)
        {
            var yaml = DeliveryPipelineTemplateSeeder.ReadResource(resource);
            var definition = YamlParsingHelper.Deserializer.Deserialize<PipelineYamlDefinition>(yaml);
            var errors = new List<string>();
            var warnings = new List<string>();
            Assert.NotNull(definition);
            PipelineDefinitionValidator.ValidateDefinitionBasics(definition, errors, warnings);
            PipelineDefinitionValidator.ValidateStages(definition, errors, warnings);
            Assert.Empty(errors);
            Assert.NotEmpty(definition.Stages);
            Assert.DoesNotContain("Aetheus", yaml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sonytumen.com", yaml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/var/lib/", yaml, StringComparison.Ordinal);
            Assert.DoesNotContain("src/", yaml, StringComparison.Ordinal);
            Assert.DoesNotContain("tests/", yaml, StringComparison.Ordinal);
        }

        Assert.All(genericResources.Where(resource => resource is not "generic/application-nightly.yaml"),
            resource => Assert.Contains(".pipeline/scripts/",
                DeliveryPipelineTemplateSeeder.ReadResource(resource), StringComparison.Ordinal));
    }

    [Fact]
    public async Task SeedAsync_ArchivesAndRemovesAnUnreferencedLegacyTemplate()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(options);
        var organization = new Organization
        {
            Name = "Aetheus",
            Slug = "aetheus",
            CreatedAt = DateTime.UnixEpoch,
            UpdatedAt = DateTime.UnixEpoch
        };
        db.Organizations.Add(organization);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.PipelineTemplates.Add(new PipelineTemplate
        {
            Name = "Build=VPS, Deploy=VPS",
            Description = "Old platform matrix",
            Category = "CI/CD",
            OrganizationId = organization.Id,
            LatestVersion = 1,
            CreatedAt = DateTime.UnixEpoch,
            UpdatedAt = DateTime.UnixEpoch,
            Versions =
            [
                new PipelineTemplateVersion
                {
                    Version = 1,
                    YamlContent = "name: old\nstages: []",
                    ChangelogEntry = "Initial version",
                    CreatedAt = DateTime.UnixEpoch,
                    CreatedByUsername = "system"
                }
            ]
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await new DeliveryPipelineTemplateSeeder(db, TimeProvider.System)
            .SeedAsync(TestContext.Current.CancellationToken);

        Assert.False(await db.PipelineTemplates.AnyAsync(
            template => template.Name == "Build=VPS, Deploy=VPS",
            TestContext.Current.CancellationToken));
        var legacy = await db.PipelineTemplates.Include(template => template.Versions)
            .SingleAsync(template => template.Name == "legacy-build-vps-deploy-vps",
                TestContext.Current.CancellationToken);
        Assert.Equal("Legacy", legacy.Category);
        Assert.Equal("name: old\nstages: []", Assert.Single(legacy.Versions).YamlContent);
    }

    [Fact]
    public async Task SeedAsync_AppendsInsteadOfMutatingWhenAReferencePipelineEvolves()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(options);
        var organization = new Organization
        {
            Name = "Aetheus",
            Slug = "aetheus",
            CreatedAt = DateTime.UnixEpoch,
            UpdatedAt = DateTime.UnixEpoch
        };
        db.Organizations.Add(organization);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.PipelineTemplates.Add(new PipelineTemplate
        {
            Name = "application-ci",
            Description = "Old reference",
            Category = "Application Delivery",
            OrganizationId = organization.Id,
            LatestVersion = 1,
            CreatedAt = DateTime.UnixEpoch,
            UpdatedAt = DateTime.UnixEpoch,
            Versions =
            [
                new PipelineTemplateVersion
                {
                    Version = 1,
                    YamlContent = "name: application-ci\nstages: []",
                    ChangelogEntry = "Old published reference",
                    CreatedAt = DateTime.UnixEpoch,
                    CreatedByUsername = "system"
                }
            ]
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await new DeliveryPipelineTemplateSeeder(db, TimeProvider.System)
            .SeedAsync(TestContext.Current.CancellationToken);

        var template = await db.PipelineTemplates.Include(item => item.Versions)
            .SingleAsync(item => item.Name == "application-ci", TestContext.Current.CancellationToken);
        Assert.Equal(2, template.LatestVersion);
        Assert.Equal("name: application-ci\nstages: []", template.Versions.Single(item => item.Version == 1).YamlContent);
        Assert.Contains("Run project CI adapter", template.Versions.Single(item => item.Version == 2).YamlContent,
            StringComparison.Ordinal);
    }

    private static PipelineTemplate CreateHistoricalCandidate(int organizationId, string yamlContent) =>
        new()
        {
            Name = "application-candidate",
            Description = "Historical application candidate",
            Category = "Application Delivery",
            OrganizationId = organizationId,
            LatestVersion = 3,
            CreatedAt = DateTime.UnixEpoch,
            UpdatedAt = DateTime.UnixEpoch,
            Versions =
            [
                new PipelineTemplateVersion
                {
                    Version = 3,
                    YamlContent = yamlContent,
                    ChangelogEntry = "Historical published variant",
                    CreatedAt = DateTime.UnixEpoch,
                    CreatedByUsername = "system"
                }
            ]
        };
}
