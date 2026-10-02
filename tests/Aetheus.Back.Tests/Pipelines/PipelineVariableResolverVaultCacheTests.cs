// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Components.VariableLibraries;
using Aetheus.Back.Components.Vaults;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Aetheus.Back.Tests.Pipelines;

/// <summary>
/// A360-07. Launching a run resolves variables twice: once before the run row exists, to feed the
/// preflight, and once after, to record the run-scoped values (BUILD_BUILDID, BUILD_PIPELINE_RUNNUMBER)
/// that only exist once the row does. Both passes were decrypting the same vault secrets, so every
/// launch paid the decryption twice.
///
/// The two calls cannot simply be merged - the second genuinely needs the run id - so the resolver
/// memoizes the vault lookup for its own (scoped, per-request) lifetime instead.
/// </summary>
public sealed class PipelineVariableResolverVaultCacheTests
{
    private readonly IVariableLibraryService _varLib = Substitute.For<IVariableLibraryService>();
    private readonly IVaultService _vault = Substitute.For<IVaultService>();
    private readonly IPipelineRepository _repo = Substitute.For<IPipelineRepository>();

    private PipelineVariableResolver BuildSut() =>
        new(_varLib, _vault, _repo, new ConfigurationBuilder().Build(), TimeProvider.System);

    private static PipelineYamlDefinition Definition(params string[] vaults) =>
        new() { Name = "p", Vaults = [.. vaults], Stages = [] };

    private void VaultReturns(params string[] names)
        => _vault.ResolveVaultSecretsWithCrossAccessAndNamesAsync(
                Arg.Any<List<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["API_TOKEN"] = "s3cret-value" },
                new HashSet<string>(names, StringComparer.OrdinalIgnoreCase)));

    [Fact]
    public async Task ResolvingTwiceInOneRequest_DecryptsTheVaultOnce()
    {
        VaultReturns("prod-vault");
        var sut = BuildSut();
        var definition = Definition("prod-vault");

        // First pass: no run id yet, exactly like the preflight pass.
        var (first, _, firstSecrets) = await sut.ResolveVariablesWithWarningsAsync(
            definition, projectId: 1, additionalVariables: null, CancellationToken.None, pipelineId: 7);
        // Second pass: same request, now with the run-scoped values.
        var (second, _, secondSecrets) = await sut.ResolveVariablesWithWarningsAsync(
            definition, projectId: 1, additionalVariables: null, CancellationToken.None,
            pipelineId: 7, runId: 42, buildNumber: 3);

        await _vault.Received(1).ResolveVaultSecretsWithCrossAccessAndNamesAsync(
            Arg.Any<List<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>());

        // Caching must not cost correctness: both passes still carry the secret and mark it secret.
        Assert.Equal("s3cret-value", first["API_TOKEN"]);
        Assert.Equal("s3cret-value", second["API_TOKEN"]);
        Assert.Contains("API_TOKEN", firstSecrets);
        Assert.Contains("API_TOKEN", secondSecrets);
        // And the second pass still gets its run-scoped values, which is why it exists at all.
        Assert.Equal("42", second["BUILD_BUILDID"]);
        Assert.Equal("3", second["BUILD_PIPELINE_RUNNUMBER"]);
        Assert.Equal("0", first["BUILD_BUILDID"]);
    }

    [Fact]
    public async Task ADifferentProject_IsNotServedFromTheCache()
    {
        // The cache key carries the project: serving project B from project A's entry would be a
        // cross-tenant secret leak, which is a far worse defect than the one being fixed.
        VaultReturns("prod-vault");
        var sut = BuildSut();
        var definition = Definition("prod-vault");

        await sut.ResolveVariablesWithWarningsAsync(
            definition, projectId: 1, additionalVariables: null, CancellationToken.None);
        await sut.ResolveVariablesWithWarningsAsync(
            definition, projectId: 2, additionalVariables: null, CancellationToken.None);

        await _vault.Received(2).ResolveVaultSecretsWithCrossAccessAndNamesAsync(
            Arg.Any<List<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADifferentVaultSet_IsNotServedFromTheCache()
    {
        VaultReturns("prod-vault", "qa-vault");
        var sut = BuildSut();

        await sut.ResolveVariablesWithWarningsAsync(
            Definition("prod-vault"), projectId: 1, additionalVariables: null, CancellationToken.None);
        await sut.ResolveVariablesWithWarningsAsync(
            Definition("qa-vault"), projectId: 1, additionalVariables: null, CancellationToken.None);

        await _vault.Received(2).ResolveVaultSecretsWithCrossAccessAndNamesAsync(
            Arg.Any<List<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFreshResolver_DoesNotInheritAnotherRequestsCache()
    {
        // The resolver is registered scoped, so a new instance stands for a new request.
        VaultReturns("prod-vault");
        var definition = Definition("prod-vault");

        await BuildSut().ResolveVariablesWithWarningsAsync(
            definition, projectId: 1, additionalVariables: null, CancellationToken.None);
        await BuildSut().ResolveVariablesWithWarningsAsync(
            definition, projectId: 1, additionalVariables: null, CancellationToken.None);

        await _vault.Received(2).ResolveVaultSecretsWithCrossAccessAndNamesAsync(
            Arg.Any<List<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
