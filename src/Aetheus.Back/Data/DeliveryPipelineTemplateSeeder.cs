// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Data;

public sealed class DeliveryPipelineTemplateSeeder(
    AppDbContext db,
    TimeProvider timeProvider)
{
    private const string ResourceRoot = "DeliveryPipelineTemplates/";

    internal static readonly IReadOnlyDictionary<string, (string Description, string Category, string[] Resources)> Definitions =
        new Dictionary<string, (string, string, string[])>(StringComparer.Ordinal)
        {
            ["application-light"] = (
                "Reusable differential feedback orchestration delegating project commands to a versioned adapter.",
                "Application Delivery",
                ["application-light-v1.yaml"]),
            ["application-candidate"] = (
                "Reusable build-once candidate orchestration with production baseline, QA and immutable release.",
                "Application Delivery",
                ["application-candidate-v1.yaml", "application-candidate-v2.yaml", "application-candidate-v3.yaml", "application-candidate-v4.yaml", "application-candidate-v5.yaml", "application-candidate-v6.yaml", "application-candidate-v7.yaml"]),
            ["application-promotion"] = (
                "Reusable promotion orchestration deploying and verifying an already-qualified candidate.",
                "Application Delivery",
                ["application-promotion-v1.yaml", "application-promotion-v2.yaml"]),
            ["application-ci"] = (
                "Run the project's CI adapter and package its immutable application payload.",
                "Application Delivery",
                ["generic/application-ci.yaml"]),
            ["application-quality"] = (
                "Run the project's quality adapter against immutable CI evidence.",
                "Quality",
                ["generic/application-quality.yaml"]),
            ["application-security"] = (
                "Run the project's security adapter against immutable CI evidence.",
                "Security",
                ["generic/application-security.yaml"]),
            ["application-qa"] = (
                "Run the project's QA adapter against the immutable application payload.",
                "Quality",
                ["generic/application-qa.yaml"]),
            ["application-nightly"] = (
                "Scheduled complete assurance and drift checks.",
                "Quality",
                ["generic/application-nightly.yaml"]),
            ["host-bluegreen-deploy"] = (
                "Reusable blue-green host cutover built from native step types, with an advisory evidence stage and an automatic rollback.",
                "Application Delivery",
                ["host-bluegreen-deploy-v1.yaml", "host-bluegreen-deploy-v2.yaml", "host-bluegreen-deploy-v3.yaml",
                 "host-bluegreen-deploy-v4.yaml"]),
            ["application-deploy-prod"] = (
                "Restore an immutable candidate and run the project's production deployment adapter.",
                "Application Delivery",
                ["generic/application-deploy-prod.yaml"]),
            ["application-release-fast"] = (
                "Run the project's explicitly opted-in fast release adapter.",
                "Application Delivery",
                ["generic/application-release-fast.yaml"]),
            ["package-telemetry"] = (
                "Build and qualify the project's telemetry package through its adapter.",
                "Packages",
                ["generic/package-telemetry.yaml"]),
            ["package-web-analytics-dotnet"] = (
                "Build and qualify the project's .NET web analytics package through its adapter.",
                "Packages",
                ["generic/package-web-analytics-dotnet.yaml"]),
            ["package-web-analytics-browser"] = (
                "Build and qualify the project's browser analytics package through its adapter.",
                "Packages",
                ["generic/package-web-analytics-browser.yaml"]),
            ["publish-observability-packages"] = (
                "Build the selected package set and run the project's publication adapter.",
                "Packages",
                ["generic/publish-observability-packages.yaml"])
        };

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var organizationId = await db.Organizations
            .Where(organization => organization.Slug == "aetheus")
            .Select(organization => organization.Id)
            .SingleAsync(ct).ConfigureAwait(false);
        await ArchiveLegacyCatalogAsync(organizationId, ct).ConfigureAwait(false);
        var names = Definitions.Keys.ToArray();
        var existing = await db.PipelineTemplates
            .Include(template => template.Versions)
            .Where(template => template.OrganizationId == organizationId && names.Contains(template.Name))
            .ToDictionaryAsync(template => template.Name, StringComparer.Ordinal, ct)
            .ConfigureAwait(false);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var (name, definition) in Definitions)
        {
            var versions = definition.Resources
                .Select((resource, index) => new
                {
                    Version = index + 1,
                    Yaml = ReadResource(resource)
                })
                .ToArray();
            if (!existing.TryGetValue(name, out var template))
            {
                template = new PipelineTemplate
                {
                    Name = name,
                    Description = definition.Description,
                    Category = definition.Category,
                    OrganizationId = organizationId,
                    LatestVersion = versions.Length,
                    CreatedAt = now,
                    UpdatedAt = now,
                    Versions = versions
                        .Select(version => new PipelineTemplateVersion
                        {
                            Version = version.Version,
                            YamlContent = version.Yaml,
                            ChangelogEntry = GetChangelogEntry(name, version.Version),
                            CreatedAt = now,
                            CreatedByUsername = "system"
                        })
                        .ToList()
                };
                db.PipelineTemplates.Add(template);
                continue;
            }

            if (definition.Resources.All(resource =>
                    resource.StartsWith("generic/", StringComparison.Ordinal)))
            {
                var desired = versions.Single().Yaml;
                var latest = template.Versions
                    .OrderByDescending(version => version.Version)
                    .FirstOrDefault();
                if (latest is null || !string.Equals(
                        Normalize(latest.YamlContent), Normalize(desired), StringComparison.Ordinal))
                {
                    var nextVersion = Math.Max(template.LatestVersion,
                        template.Versions.Select(version => version.Version).DefaultIfEmpty(0).Max()) + 1;
                    template.Versions.Add(new PipelineTemplateVersion
                    {
                        Version = nextVersion,
                        YamlContent = desired,
                        ChangelogEntry = "Synchronized from the current reference pipeline.",
                        CreatedAt = now,
                        CreatedByUsername = "system"
                    });
                    template.LatestVersion = nextVersion;
                }
                template.Description = definition.Description;
                template.Category = definition.Category;
                template.UpdatedAt = now;
                continue;
            }

            foreach (var seededVersion in versions)
            {
                var version = template.Versions
                    .SingleOrDefault(item => item.Version == seededVersion.Version);
                if (version is null)
                {
                    template.Versions.Add(new PipelineTemplateVersion
                    {
                        Version = seededVersion.Version,
                        YamlContent = seededVersion.Yaml,
                        ChangelogEntry = GetChangelogEntry(name, seededVersion.Version),
                        CreatedAt = now,
                        CreatedByUsername = "system"
                    });
                }
                else if (!string.Equals(
                             Normalize(version.YamlContent),
                             Normalize(seededVersion.Yaml),
                             StringComparison.Ordinal) &&
                         !IsRecognizedHistoricalVersion(name, seededVersion.Version, version.YamlContent))
                {
                    throw new InvalidOperationException(
                        $"Seeded delivery template '{name}@{seededVersion.Version}' differs from its immutable published version.");
                }
            }
            template.Description = definition.Description;
            template.Category = definition.Category;
            template.LatestVersion = Math.Max(template.LatestVersion, versions.Length);
            template.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    internal static string ReadResource(string fileName)
    {
        var assembly = typeof(DeliveryPipelineTemplateSeeder).Assembly;
        using var stream = assembly.GetManifestResourceStream($"{ResourceRoot}{fileName}")
            ?? throw new InvalidOperationException(
                $"Embedded delivery pipeline template '{fileName}' is unavailable.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private async Task ArchiveLegacyCatalogAsync(int organizationId, CancellationToken ct)
    {
        var canonicalNames = Definitions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var templates = await db.PipelineTemplates
            .Include(template => template.Versions)
            .Where(template => template.OrganizationId == organizationId)
            .ToListAsync(ct).ConfigureAwait(false);
        var pipelines = await db.Pipelines.AsNoTracking()
            .Where(pipeline => pipeline.Project != null
                && pipeline.Project.OrganizationId == organizationId)
            .Select(pipeline => new { pipeline.TemplateReferenceName })
            .ToListAsync(ct).ConfigureAwait(false);
        var allVersionYaml = templates.SelectMany(template => template.Versions)
            .Select(version => version.YamlContent)
            .ToArray();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var template in templates.Where(template =>
                     !canonicalNames.Contains(template.Name)
                     && !template.Name.StartsWith("legacy-", StringComparison.OrdinalIgnoreCase)))
        {
            var legacyName = LegacyName(template.Name);
            if (!templates.Any(existing =>
                    string.Equals(existing.Name, legacyName, StringComparison.OrdinalIgnoreCase)))
            {
                db.PipelineTemplates.Add(new PipelineTemplate
                {
                    Name = legacyName,
                    Description = $"Archived snapshot of '{template.Name}'. {template.Description}".Trim(),
                    Category = "Legacy",
                    OrganizationId = organizationId,
                    LatestVersion = template.LatestVersion,
                    CreatedAt = now,
                    UpdatedAt = now,
                    Versions = template.Versions.Select(version => new PipelineTemplateVersion
                    {
                        Version = version.Version,
                        YamlContent = version.YamlContent,
                        ChangelogEntry = $"Legacy snapshot: {version.ChangelogEntry}",
                        CreatedAt = now,
                        CreatedByUsername = "system"
                    }).ToList()
                });
            }

            var referenced = pipelines.Any(pipeline => string.Equals(
                    pipeline.TemplateReferenceName, template.Name, StringComparison.OrdinalIgnoreCase))
                || allVersionYaml.Any(yaml => ReferencesTemplate(yaml, template.Name));
            if (referenced)
            {
                if (!string.Equals(template.Category, "Legacy reference", StringComparison.Ordinal))
                {
                    template.Category = "Legacy reference";
                    template.Description = $"Retained until references migrate to '{legacyName}'. {template.Description}";
                    template.UpdatedAt = now;
                }
            }
            else
            {
                db.PipelineTemplates.Remove(template);
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static bool ReferencesTemplate(string yaml, string templateName)
    {
        var marker = $"extends: {templateName}";
        return yaml.Split('\n').Any(line =>
            line.Trim().StartsWith(marker, StringComparison.OrdinalIgnoreCase)
            && (line.Trim().Length == marker.Length
                || line.Trim()[marker.Length] is '@' or ' ' or '\r'));
    }

    private static string LegacyName(string name)
    {
        var slug = new string(name.Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return $"legacy-{slug.Trim('-')}";
    }

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static bool IsRecognizedHistoricalVersion(string name, int version, string yamlContent) =>
        string.Equals(name, "application-candidate", StringComparison.Ordinal) &&
        version == 3 &&
        string.Equals(
            Normalize(yamlContent),
            Normalize(ReadResource("application-candidate-v3-legacy-advisory.yaml")),
            StringComparison.Ordinal);

    private static string GetChangelogEntry(string name, int version) =>
        (name, version) switch
        {
            (_, 1) => "Initial reusable application delivery contract.",
            ("application-candidate", 3) =>
                "Isolate baseline contract inspection in a pinned, networkless container.",
            ("application-candidate", 4) =>
                "Make quality and security gates advisory while preserving their grades.",
            ("application-candidate", 5) =>
                "Propagate reproducible cache, differential format and bounded Gitleaks history modes.",
            ("application-candidate", 6) =>
                "Make optional assurance capabilities composable for generated application pipeline sets.",
            ("application-candidate", 7) =>
                "Complete every enabled assurance producer before immutable candidate publication.",
            ("host-bluegreen-deploy", 2) =>
                "Carry the run's Compose interpolation variables into the cutover steps.",
            ("host-bluegreen-deploy", 3) =>
                "Separate the cutover's Compose variables from the identity only the starting step receives.",
            ("host-bluegreen-deploy", 4) =>
                "Let the rollback run after a failure that never produced the run's image tags.",
            _ => "Require an explicit immutable candidate release version."
        };
}
