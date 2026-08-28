// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using NuGet.Versioning;

namespace Aetheus.Back.Components.AgentUpdate;

internal sealed class AgentReleaseCatalog : IAgentReleaseCatalog, IHostedService
{
    private readonly string _downloadsPath;
    private readonly bool _strict;
    private readonly ILogger<AgentReleaseCatalog> _logger;

    public AgentReleaseCatalog(IWebHostEnvironment environment, ILogger<AgentReleaseCatalog> logger)
    {
        _downloadsPath = Path.Combine(environment.ContentRootPath, "wwwroot", "downloads");
        _strict = !environment.IsDevelopment();
        _logger = logger;
        Current = LoadManifest() ?? BuildDevelopmentManifest();
    }

    public AgentReleaseManifestDto Current { get; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(_downloadsPath, "agent-release-manifest.json");
        if (!File.Exists(manifestPath))
        {
            if (_strict)
                throw new InvalidOperationException(
                    $"Agent release manifest is required outside Development: {manifestPath}");

            _logger.LogWarning(
                "Agent release manifest is absent; Development uses assembly version {Version} without release archives",
                Current.SoftwareVersion);
            return;
        }

        ValidateManifestShape(Current);
        foreach (var archive in Current.Archives)
            await ValidateArchiveAsync(Current.SoftwareVersion, archive, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Validated agent release {Version}, protocol {Protocol}, {ArchiveCount} immutable archive(s)",
            Current.SoftwareVersion,
            Current.ProtocolVersion,
            Current.Archives.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private AgentReleaseManifestDto? LoadManifest()
    {
        var path = Path.Combine(_downloadsPath, "agent-release-manifest.json");
        if (!File.Exists(path)) return null;

        var manifest = JsonSerializer.Deserialize<AgentReleaseManifestDto>(
            File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return manifest ?? throw new InvalidOperationException($"Agent release manifest is empty: {path}");
    }

    private static AgentReleaseManifestDto BuildDevelopmentManifest()
    {
        var version = typeof(AgentReleaseCatalog).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        return new AgentReleaseManifestDto
        {
            SoftwareVersion = version,
            ProtocolVersion = AgentProtocol.CurrentVersion,
            MinimumSupportedProtocol = AgentProtocol.MinimumSupportedVersion,
            MaximumSupportedProtocol = AgentProtocol.MaximumSupportedVersion,
            SoftwareCapabilities = [.. AgentCapabilities.SoftwareCapabilities],
            ProducedAtUtc = DateTime.UnixEpoch,
            Commit = "development"
        };
    }

    private static void ValidateManifestShape(AgentReleaseManifestDto manifest)
    {
        Validator.ValidateObject(manifest, new ValidationContext(manifest), validateAllProperties: true);
        if (!NuGetVersion.TryParse(manifest.SoftwareVersion, out _))
            throw new InvalidOperationException(
                $"Agent release softwareVersion is not valid SemVer: {manifest.SoftwareVersion}");
        if (manifest.ProtocolVersion != AgentProtocol.CurrentVersion
            || manifest.MinimumSupportedProtocol > AgentProtocol.CurrentVersion
            || manifest.MaximumSupportedProtocol < AgentProtocol.CurrentVersion)
        {
            throw new InvalidOperationException("Agent release protocol window does not include the running backend.");
        }

        if (manifest.Archives.Count == 0)
            throw new InvalidOperationException("Agent release manifest contains no archives.");
        if (manifest.Archives.Select(a => (a.Platform, a.Architecture)).Distinct().Count()
            != manifest.Archives.Count)
        {
            throw new InvalidOperationException("Agent release manifest contains duplicate platform/architecture entries.");
        }

        var capabilities = manifest.SoftwareCapabilities;
        if (capabilities.Count != capabilities.Distinct(StringComparer.Ordinal).Count()
            || !capabilities.SequenceEqual(capabilities.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Agent release capabilities must be sorted and unique.");
        }
    }

    private async Task ValidateArchiveAsync(
        string softwareVersion,
        AgentReleaseArchiveDto archive,
        CancellationToken ct)
    {
        Validator.ValidateObject(archive, new ValidationContext(archive), validateAllProperties: true);
        if (Path.GetFileName(archive.FileName) != archive.FileName)
            throw new InvalidOperationException($"Agent archive fileName must not contain a path: {archive.FileName}");
        if (archive.Platform is not ("linux" or "windows"))
            throw new InvalidOperationException($"Unsupported agent archive platform: {archive.Platform}");
        if (archive.Architecture is not ("x64" or "arm64"))
            throw new InvalidOperationException($"Unsupported agent archive architecture: {archive.Architecture}");

        var path = Path.Combine(_downloadsPath, "releases", softwareVersion, archive.FileName);
        var file = new FileInfo(path);
        if (!file.Exists)
            throw new InvalidOperationException($"Agent release archive is missing: {path}");
        if (file.Length != archive.SizeBytes)
            throw new InvalidOperationException(
                $"Agent release archive size mismatch for {archive.FileName}: expected {archive.SizeBytes}, got {file.Length}");

        await using var stream = file.OpenRead();
        var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
        if (!string.Equals(actual, archive.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Agent release archive SHA-256 mismatch for {archive.FileName}: expected {archive.Sha256}, got {actual}");
    }
}
