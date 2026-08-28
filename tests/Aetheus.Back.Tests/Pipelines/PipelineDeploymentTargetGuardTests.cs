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
            ["PUBLIC_APP_URL"] = "https://app.aetheus.sonytumen.com"
        });

    [Fact]
    public void ValidateVariables_Local_AcceptsHttpsLoopbackContract()
        => PipelineDeploymentTargetGuard.ValidateVariables(LocalVariables());

    [Theory]
    [InlineData("PUBLIC_API_URL", "https://aetheus-api.sonytumen.com")]
    [InlineData("COMPOSE_PROJECT", "aetheus-prod")]
    [InlineData("TARGET_SERVER", "vps2577917")]
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
    public void ValidateVariables_Local_AcceptsAnExplicitRenamedAgentSelector()
    {
        var variables = LocalVariables();
        variables[PipelineDeploymentTargetGuard.LocalAgentVariable] = "renamed-local-runner";

        PipelineDeploymentTargetGuard.ValidateVariables(variables);
    }

    [Fact]
    public void ValidateVariables_Local_RejectsMissingAgentSelector()
    {
        var variables = LocalVariables();
        variables.Remove(PipelineDeploymentTargetGuard.LocalAgentVariable);

        Assert.Throws<BadRequestException>(() => PipelineDeploymentTargetGuard.ValidateVariables(variables));
    }

    [Fact]
    public void ValidateServer_Local_RequiresExactExplicitSelector()
    {
        var variables = LocalVariables();
        var selectedRunner = new Server
        {
            Name = "local-runner",
            Hostname = "vpssim",
            OrganizationId = 1
        };

        Assert.Null(PipelineDeploymentTargetGuard.ValidateServer(variables, selectedRunner));
        Assert.NotNull(PipelineDeploymentTargetGuard.ValidateServer(variables,
            new Server { Name = "other-runner", Hostname = "other-runner", OrganizationId = 1 }));
    }

    private static Dictionary<string, string> LocalVariables() => new(StringComparer.OrdinalIgnoreCase)
    {
        [PipelineDeploymentTargetGuard.TargetVariable] = PipelineDeploymentTargetGuard.Local,
        [PipelineDeploymentTargetGuard.LocalAgentVariable] = "vpssim",
        ["REPOSITORY_URL"] = "https://host.docker.internal:5301/git/1/aetheus.git",
        ["PUBLIC_APP_URL"] = "https://app.aetheus.localhost",
        ["PUBLIC_API_URL"] = "https://api.aetheus.localhost",
        ["SITE_DOMAIN"] = "aetheus.localhost",
        ["APP_HOST"] = "app.aetheus.localhost",
        ["API_HOST"] = "api.aetheus.localhost",
        ["COMPOSE_PROJECT"] = "aetheus-local-sim",
        ["CERTBOT_MODE"] = "local",
        ["TLS_CA_FILE"] = "/etc/letsencrypt/live/aetheus.localhost/fullchain.pem"
    };
}
