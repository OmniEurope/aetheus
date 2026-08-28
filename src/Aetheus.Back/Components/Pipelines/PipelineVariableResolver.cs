// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using Aetheus.Back.Components.ExternalRepos;
using Aetheus.Back.Components.VariableLibraries;
using Aetheus.Back.Components.Vaults;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Helpers;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// F-009: extracted from <see cref="PipelineRunService"/> to reduce its dependency count and
/// improve cohesion. Resolves all pipeline variables (system, inline, libraries, vaults,
/// additional) and tracks warnings + secret-key set.
/// </summary>
internal sealed class PipelineVariableResolver(
    IVariableLibraryService variableLibraryService,
    IVaultService vaultService,
    IPipelineRepository repo,
    IConfiguration configuration,
    TimeProvider timeProvider) : IPipelineVariableResolver
{
    /// <summary>
    /// Per-request memo of resolved vault secrets. This resolver is registered scoped, so the cache
    /// lives exactly as long as the request that owns it and never crosses runs or users. Keyed by
    /// project plus the declared vault names, which is everything the lookup depends on.
    /// </summary>
    private readonly Dictionary<string, (Dictionary<string, string> Vars, HashSet<string> FoundNames)> _vaultCache =
        new(StringComparer.Ordinal);

    /// <summary>Per-request memo of resolved variable libraries, same lifetime and reasoning as
    /// <see cref="_vaultCache"/>.</summary>
    private readonly Dictionary<string, (Dictionary<string, string> Vars, HashSet<string> FoundNames)> _libraryCache =
        new(StringComparer.Ordinal);


    public async Task<(Dictionary<string, string> Resolved, List<string> Warnings, HashSet<string> SecretKeys)> ResolveVariablesWithWarningsAsync(
        PipelineYamlDefinition definition, int? projectId, Dictionary<string, string>? additionalVariables, CancellationToken ct,
        int? pipelineId = null, int? runId = null, string? pipelineName = null, int? buildNumber = null)
    {
        var warnings = new List<string>();
        var secretKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolved = CreateSystemVariables(definition, pipelineId, runId, pipelineName, buildNumber);
        AddProjectAndDeclaredVariables(resolved, definition, projectId, runId);
        await AddLibraryVariablesAsync(resolved, warnings, definition, projectId, ct).ConfigureAwait(false);
        await AddVaultVariablesAsync(resolved, secretKeys, definition, projectId, ct).ConfigureAwait(false);
        AddAdditionalVariables(resolved, additionalVariables);
        ExpandVariableReferences(resolved);
        PipelineDeploymentTargetGuard.ValidateVariables(resolved);
        return (resolved, warnings, secretKeys);
    }

    private Dictionary<string, string> CreateSystemVariables(
        PipelineYamlDefinition definition,
        int? pipelineId,
        int? runId,
        string? pipelineName,
        int? buildNumber)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BUILD_BUILDID"] = (runId ?? 0).ToString(),
            ["BUILD_BUILDNUMBER"] = (runId ?? 0).ToString(),
            // Per-pipeline sequential counter, unlike the globally monotonic run id above. This is
            // what a release/version pattern should use: it increments by exactly one per run of
            // this pipeline, so 1.1.$(BUILD_PIPELINE_RUNNUMBER) yields 1.1.1, 1.1.2, 1.1.3…
            ["BUILD_PIPELINE_RUNNUMBER"] = (buildNumber ?? 0).ToString(),
            ["BUILD_PIPELINEID"] = (pipelineId ?? 0).ToString(),
            ["BUILD_PIPELINENAME"] = pipelineName ?? definition.Name,
            // Stable per-run host port for disposable QA environments. Run ids are globally
            // monotonic, so concurrently active runs map to distinct ports without a shared bind.
            ["BUILD_RUN_PORT"] = (20_000 + Math.Abs((long)(runId ?? 0)) % 40_000)
                .ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["BUILD_TRIGGEREDBY"] = definition.Trigger,
            ["SYSTEM_DATE"] = now.ToString("yyyy-MM-dd"),
            ["SYSTEM_DATETIME"] = now.ToString("o"),
            ["SYSTEM_TIMESTAMP"] = new DateTimeOffset(now, TimeSpan.Zero).ToUnixTimeSeconds().ToString(),
            ["AETHEUS_VERSION"] = typeof(PipelineVariableResolver).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
            ["CI"] = "true",
        };
    }

    private static void AddProjectAndDeclaredVariables(
        IDictionary<string, string> resolved,
        PipelineYamlDefinition definition,
        int? projectId,
        int? runId)
    {
        if (!string.IsNullOrWhiteSpace(definition.ProjectType))
            resolved["PROJECT_TYPE"] = definition.ProjectType;
        if (projectId is { } pid0)
        {
            resolved["BUILD_PROJECTID"] = pid0.ToString();
            resolved["WORKSPACE"] = PipelineCommandBuilder.GetDefaultWorkspace(runId ?? 0, isWindows: false);
        }
        foreach (var (name, value) in PipelineParameterResolver.Defaults(definition.Parameters))
            if (!resolved.ContainsKey(name)) resolved.Add(name, value);
        foreach (var (key, value) in definition.Variables)
            resolved[key] = value ?? string.Empty;
    }

    private async Task AddLibraryVariablesAsync(
        IDictionary<string, string> resolved,
        ICollection<string> warnings,
        PipelineYamlDefinition definition,
        int? projectId,
        CancellationToken ct)
    {
        if (definition.VariableLibraries.Count == 0) return;
        // Same per-request memo as the vault lookup below, for the same reason: a launch and a stage
        // advance both resolve variables more than once, and the libraries a project declares cannot
        // change inside one request.
        var cacheKey = $"{projectId?.ToString(CultureInfo.InvariantCulture) ?? "-"}|"
                       + string.Join(',', definition.VariableLibraries.Order(StringComparer.Ordinal));
        if (!_libraryCache.TryGetValue(cacheKey, out var cached))
        {
            cached = projectId is { } id
                ? await variableLibraryService.ResolveLibrariesWithCrossAccessAndNamesAsync(
                    definition.VariableLibraries, id, ct).ConfigureAwait(false)
                : await variableLibraryService.ResolveLibrariesWithNamesAsync(
                    definition.VariableLibraries, projectId, ct).ConfigureAwait(false);
            _libraryCache[cacheKey] = cached;
        }
        var (libraryVariables, foundNames) = cached;
        libraryVariables ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foundNames ??= [];
        foreach (var name in definition.VariableLibraries)
            if (!foundNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                warnings.Add($"Variable library '{name}' not found.");
        foreach (var (key, value) in libraryVariables) resolved[key] = value;
    }

    private async Task AddVaultVariablesAsync(
        IDictionary<string, string> resolved,
        ISet<string> secretKeys,
        PipelineYamlDefinition definition,
        int? projectId,
        CancellationToken ct)
    {
        if (definition.Vaults.Count == 0) return;
        // Memoized for the lifetime of this scoped resolver. A launch resolves variables twice - once
        // before the run row exists (to feed the preflight) and once after, to record run-scoped values
        // like BUILD_BUILDID - and both passes decrypt the very same vault secrets. The answer cannot
        // differ between the two within one request, so the second pass reuses the first.
        var cacheKey = $"{projectId?.ToString(CultureInfo.InvariantCulture) ?? "-"}|"
                       + string.Join(',', definition.Vaults.Order(StringComparer.Ordinal));
        if (!_vaultCache.TryGetValue(cacheKey, out var cached))
        {
            cached = projectId is { } id
                ? await vaultService.ResolveVaultSecretsWithCrossAccessAndNamesAsync(
                    definition.Vaults, id, ct).ConfigureAwait(false)
                : await vaultService.ResolveVaultSecretsWithNamesAsync(
                    definition.Vaults, projectId, ct).ConfigureAwait(false);
            _vaultCache[cacheKey] = cached;
        }
        var (vaultVariables, foundNames) = cached;
        vaultVariables ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foundNames ??= [];
        var missingVaults = definition.Vaults
            .Where(name => !foundNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (missingVaults.Count > 0)
            throw new BadRequestException(
                $"Required Vault(s) not found or inaccessible: {string.Join(", ", missingVaults.Select(name => $"'{name}'"))}.");
        foreach (var (key, value) in vaultVariables)
        {
            resolved[key] = value;
            secretKeys.Add(key);
        }
    }

    private static void AddAdditionalVariables(
        IDictionary<string, string> resolved,
        IReadOnlyDictionary<string, string>? additionalVariables)
    {
        if (additionalVariables is { Count: > 0 })
            foreach (var (key, value) in additionalVariables)
                resolved[key] = value;
    }

    private static void ExpandVariableReferences(Dictionary<string, string> resolved)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            var anyReplaced = false;
            foreach (var key in resolved.Keys.ToList())
            {
                var val = resolved[key];
                if (!val.Contains("$(")) continue;
                var newVal = PipelineCommandBuilder.Substitute(val, resolved);
                if (newVal != val)
                {
                    resolved[key] = newVal;
                    anyReplaced = true;
                }
            }
            if (!anyReplaced) break;
        }
    }

    public static void InjectStageSystemVariables(
        Dictionary<string, string> stageVars, string stageName, Server server)
    {
        stageVars["SYSTEM_STAGENAME"] = stageName;
        stageVars["AGENT_NAME"] = server.Name;
        stageVars["AGENT_HOSTNAME"] = server.Hostname;
        stageVars["AGENT_OS"] = server.OsDescription ?? string.Empty;
        stageVars["AGENT_PLATFORM"] = OsTypeHelper.IsWindows(server.OsType, server.OsDescription) ? "Windows" : "Linux";
        stageVars["AGENT_ID"] = server.Id.ToString();
    }

    public async Task<Dictionary<string, string>> ResolveFullVariablesForRunAsync(
        PipelineRun run, PipelineYamlDefinition definition, CancellationToken ct)
        => (await ResolveFullVariablesWithSecretsForRunAsync(run, definition, ct).ConfigureAwait(false)).Resolved;

    public async Task<(Dictionary<string, string> Resolved, HashSet<string> SecretKeys)> ResolveFullVariablesWithSecretsForRunAsync(
        PipelineRun run, PipelineYamlDefinition definition, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(run.Pipeline, nameof(run.Pipeline));

        var additionalVars = PipelineRunService.DeserializeResolvedVariablesStatic(run.AdditionalVariablesJson);
        var projectId = run.Pipeline.ProjectId
            ?? await repo.GetPipelineProjectIdAsync(run.Pipeline, ct).ConfigureAwait(false);

        // A run is resolved again before every stage. Its immutable source revision must therefore be
        // re-injected here, not only at launch: otherwise BUILD_SOURCEVERSION disappears after Prepare
        // and CI can package an unpinned artifact despite the run itself having a valid commit.
        if (PipelineRunHelpers.IsGitCommitHash(run.CommitHash))
            additionalVars["BUILD_SOURCEVERSION"] = run.CommitHash!;

        // S-TECH-RPVI: shared injection point (see PipelineProjectVariables), identical to the
        // trigger-time path so the rehomed mirror URL stays consistent across re-resolutions.
        PipelineProjectVariables.Inject(
            additionalVars, run.Pipeline?.Project, configuration, run.BranchName ?? run.Pipeline?.SourceBranch);
        if (!string.IsNullOrWhiteSpace(run.RepositoryUrl))
        {
            additionalVars["BUILD_REPOSITORY_URI"] = run.RepositoryUrl;
            additionalVars["REPOSITORY_URL"] = run.RepositoryUrl;
        }

        var (resolved, _, secretKeys) = await ResolveVariablesWithWarningsAsync(
            definition, projectId, additionalVars, ct,
            pipelineId: run.PipelineId, runId: run.Id,
            pipelineName: run.Pipeline?.Name, buildNumber: run.BuildNumber).ConfigureAwait(false);
        return (resolved, secretKeys);
    }
}
