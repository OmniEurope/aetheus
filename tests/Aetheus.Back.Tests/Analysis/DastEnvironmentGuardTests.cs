// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Environments;
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Exceptions;

namespace Aetheus.Back.Tests.Analysis;

public sealed class DastEnvironmentGuardTests
{
    [Theory]
    [InlineData(EnvironmentType.Production, false, false, "qa.example.test")]
    [InlineData(EnvironmentType.Testing, false, false, "qa.example.test")]
    [InlineData(EnvironmentType.Testing, false, true, "qa.example.test")]
    [InlineData(EnvironmentType.Development, true, false, "qa.example.test")]
    [InlineData(EnvironmentType.Testing, true, false, "")]
    public void ValidateDastConfiguration_RejectsUnsafeEnvironment(
        EnvironmentType type,
        bool isEphemeral,
        bool containsRealData,
        string allowedHosts)
    {
        Assert.Throws<BadRequestException>(() => EnvironmentService.ValidateDastConfiguration(
            type, enabled: true, isEphemeral, containsRealData, allowedHosts));
    }

    [Fact]
    public void ValidateDastConfiguration_AcceptsEphemeralTestingAllowlist()
    {
        EnvironmentService.ValidateDastConfiguration(EnvironmentType.Testing, enabled: true,
            isEphemeral: true, containsRealData: false, "qa.example.test");
    }

    [Theory]
    [InlineData(EnvironmentType.Testing, true, false, true)]
    [InlineData(EnvironmentType.Staging, true, false, true)]
    [InlineData(EnvironmentType.Testing, false, false, false)]
    [InlineData(EnvironmentType.Staging, true, true, false)]
    [InlineData(EnvironmentType.Production, true, false, false)]
    public void RuntimeDispatch_RequiresEphemeralNonProductionEnvironment(
        EnvironmentType type,
        bool isEphemeral,
        bool containsRealData,
        bool expected)
    {
        Assert.Equal(expected, PipelineScannerTaskFactory.IsDastEnvironmentEligible(type, isEphemeral, containsRealData));
    }

    [Theory]
    [InlineData("https://qa.example.test/openapi.json", "https://qa.example.test", true)]
    [InlineData("http://127.0.0.1:10082/openapi/v1.json", "http://127.0.0.1:10081", true)]
    [InlineData("http://qa.example.test/openapi.json", "http://qa.example.test", false)]
    [InlineData("http://127.0.0.1/openapi.json", "http://localhost", false)]
    public void DastApiSpecification_AllowsHttpsOrExactLoopbackQa(
        string specificationUrl,
        string targetUrl,
        bool expected)
    {
        var specification = new Uri(specificationUrl);
        var target = new Uri(targetUrl);

        Assert.Equal(
            expected,
            PipelineScannerTaskFactory.IsDastApiSpecificationEligible(target, specification));
    }
}
