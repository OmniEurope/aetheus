// SPDX-License-Identifier: EUPL-1.2
using NuGet.Versioning;

namespace Aetheus.Back.Components.AgentUpdate;

internal sealed class AgentCompatibilityPolicy(IAgentReleaseCatalog releases) : IAgentCompatibilityPolicy
{
    public AgentCompatibilityDto Evaluate(ServerDto server)
    {
        var release = releases.Current;
        var present = server.AgentCapabilities
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var required = RequiredCapabilities(server);
        var missing = required.Except(present, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        var (status, reason) = ResolveStatus(server, release, missing);
        return new AgentCompatibilityDto
        {
            Status = status,
            Reason = reason,
            InstalledVersion = server.AgentVersion,
            TargetVersion = release.SoftwareVersion,
            AgentProtocolVersion = server.AgentProtocolVersion,
            MinimumSupportedProtocol = release.MinimumSupportedProtocol,
            MaximumSupportedProtocol = release.MaximumSupportedProtocol,
            PresentCapabilities = present,
            MissingCapabilities = missing
        };
    }

    public bool CanExecute(ServerDto server, string requiredCapability)
    {
        var compatibility = Evaluate(server);
        return server.Status == ServerStatus.Online
            && compatibility.AgentProtocolVersion is { } protocol
            && AgentProtocol.IsSupported(protocol)
            && compatibility.PresentCapabilities.Contains(requiredCapability, StringComparer.Ordinal);
    }

    private static (AgentCompatibilityStatus Status, AgentCompatibilityReason Reason) ResolveStatus(
        ServerDto server,
        AgentReleaseManifestDto release,
        IReadOnlyCollection<string> missing)
    {
        if (server.LastHeartbeat == default)
            return (AgentCompatibilityStatus.Unknown, AgentCompatibilityReason.NoHeartbeat);
        if (!NuGetVersion.TryParse(server.AgentVersion, out var installed))
            return (AgentCompatibilityStatus.UpdateRequired, AgentCompatibilityReason.InvalidSoftwareVersion);

        if (server.AgentProtocolVersion is not { } protocol
            || protocol < release.MinimumSupportedProtocol
            || protocol > release.MaximumSupportedProtocol)
            return (AgentCompatibilityStatus.UpdateRequired, AgentCompatibilityReason.UnsupportedProtocol);
        if (missing.Count > 0)
            return (AgentCompatibilityStatus.UpdateRequired, AgentCompatibilityReason.RequiredCapabilityMissing);
        if (!NuGetVersion.TryParse(release.SoftwareVersion, out var target))
            throw new InvalidOperationException($"Release version is invalid: {release.SoftwareVersion}");
        if (installed < target)
            return (AgentCompatibilityStatus.UpdateRecommended, AgentCompatibilityReason.OlderSoftware);
        return (AgentCompatibilityStatus.UpToDate, AgentCompatibilityReason.Current);
    }

    private static HashSet<string> RequiredCapabilities(ServerDto server)
    {
        var required = new HashSet<string>(StringComparer.Ordinal)
        {
            AgentCapabilities.SelfUpdate
        };
        if (server.PipelineRunnerEnabled is true)
            required.Add(AgentCapabilities.PipelineBuild);
        if (server.DeploymentTargetAvailable is true)
            required.Add(AgentCapabilities.Deployment);
        return required;
    }
}
