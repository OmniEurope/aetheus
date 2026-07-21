// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;

namespace Aetheus.Back.Tests.Pipelines;

public class PipelineDeploymentTargetGuardTests
{
    [Fact]
    public void ValidateVariables_Production_RemainsAccepted()
        => PipelineDeploymentTargetGuard.ValidateVariables(new Dictionary<string, string>
        {
            [PipelineDeploymentTargetGuard.TargetVariable] = PipelineDeploymentTargetGuard.Production,
            ["PUBLIC_APP_URL"] = "https://app.aetheus.example.com"
        });

    [Fact]
    public void ValidateVariables_Local_AcceptsHttpsLoopbackContract()
        => PipelineDeploymentTargetGuard.ValidateVariables(LocalVariables());

    [Theory]
    [InlineData("PUBLIC_API_URL", "https://aetheus-api.example.com")]
    [InlineData("COMPOSE_PROJECT", "aetheus-prod")]
    [InlineData("TARGET_SERVER", "production-host")]
    [InlineData("PUBLIC_APP_URL", "https://example.com")]
    [InlineData("REPOSITORY_URL", "http://host.docker.internal:5300/git/1/aetheus.git")]
    public void ValidateVariables_Local_RejectsProductionOrNonLocalTargets(string key, string value)
    {
        var variables = LocalVariables();
        variables[key] = value;

        Assert.Throws<BadRequestException>(() => PipelineDeploymentTargetGuard.ValidateVariables(variables));
    }

    [Fact]
    public void ValidateVariables_RejectsUnknownTarget()
    {
        var variables = LocalVariables();
        variables[PipelineDeploymentTargetGuard.TargetVariable] = "qa";

        Assert.Throws<BadRequestException>(() => PipelineDeploymentTargetGuard.ValidateVariables(variables));
    }

    [Fact]
    public void ValidateServer_Local_RequiresDedicatedVpsSimAgent()
    {
        var variables = LocalVariables();

        Assert.Null(PipelineDeploymentTargetGuard.ValidateServer(variables,
            new Server { Name = "release-vpssim", Hostname = "release-vpssim", OrganizationId = 1 }));
        Assert.NotNull(PipelineDeploymentTargetGuard.ValidateServer(variables,
            new Server { Name = "production", Hostname = "prod-host", OrganizationId = 1 }));
    }

    private static Dictionary<string, string> LocalVariables() => new(StringComparer.OrdinalIgnoreCase)
    {
        [PipelineDeploymentTargetGuard.TargetVariable] = PipelineDeploymentTargetGuard.Local,
        ["REPOSITORY_URL"] = "https://host.docker.internal:5301/git/1/aetheus.git",
        ["PUBLIC_APP_URL"] = "https://app.aetheus.localhost",
        ["PUBLIC_API_URL"] = "https://api.aetheus.localhost",
        ["SITE_DOMAIN"] = "aetheus.localhost",
        ["APP_HOST"] = "app.aetheus.localhost",
        ["API_HOST"] = "api.aetheus.localhost",
        ["COMPOSE_PROJECT"] = "aetheus-release-lab",
        ["CERTBOT_MODE"] = "local",
        ["TLS_CA_FILE"] = "/etc/letsencrypt/live/aetheus.localhost/fullchain.pem"
    };
}
