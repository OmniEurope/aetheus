// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Validation;

namespace Aetheus.Back.Components.Pipelines;

internal static partial class PipelineIsolationHelpers
{
    [GeneratedRegex(@"^\d+(\.\d+)?\s*[bkmgBKMG]?$")]
    private static partial Regex MemoryLimitPattern();

    [GeneratedRegex(@"^\d+(\.\d+)?$")]
    private static partial Regex CpusLimitPattern();

    internal static IReadOnlyList<string> Apply(
        ServerTask task,
        PipelineIsolationDefinition? isolation)
    {
        if (isolation?.IsContainer != true) return [];

        var limitErrors = ValidateLimits(
            [new PipelineStageDefinition { Name = task.Name, Isolation = isolation }]);
        if (limitErrors.Count > 0)
            throw new ArgumentException(string.Join(" ", limitErrors), nameof(isolation));

        task.Executor = ExecutorType.Container;
        task.ContainerImage = isolation.Image;
        task.ContainerToolchain = isolation.Toolchain;
        task.ContainerShell = isolation.Shell;
        task.ContainerRuntime = isolation.Runtime;
        task.ContainerNetwork = isolation.Network;
        if (IsValidMemory(isolation.Memory))
            task.ContainerMemory = isolation.Memory;
        if (IsValidCpus(isolation.Cpus))
            task.ContainerCpus = isolation.Cpus;

        return [];
    }

    internal static List<string> ValidateLimits(IEnumerable<PipelineStageDefinition> stages)
    {
        var errors = new List<string>();
        foreach (var stage in stages)
        {
            var isolation = stage.Isolation;
            if (isolation is null) continue;
            if (!string.IsNullOrWhiteSpace(isolation.Memory) && !IsValidMemory(isolation.Memory))
                errors.Add($"Stage '{stage.Name}': container memory limit '{isolation.Memory}' is invalid.");
            if (!string.IsNullOrWhiteSpace(isolation.Cpus) && !IsValidCpus(isolation.Cpus))
                errors.Add($"Stage '{stage.Name}': container CPU limit '{isolation.Cpus}' is invalid.");
        }
        return errors;
    }

    internal static List<string> ValidateDefinitions(
        PipelineIsolationDefinition? runIsolation,
        IEnumerable<PipelineStageDefinition> stages)
    {
        var errors = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(runIsolation?.Toolchain))
        {
            errors.Add(
                "Run-level container isolation cannot use a toolchain because the repository " +
                "manifest is unavailable before the system checkout step. Declare toolchains per stage or job.");
        }
        AddDefinitionError(errors, "Run", runIsolation);
        foreach (var stage in stages)
            AddDefinitionError(errors, $"Stage '{stage.Name}'", stage.Isolation);
        return [.. errors];
    }

    internal static string? ValidateDefinition(string label, PipelineIsolationDefinition? isolation)
    {
        if (isolation is null)
            return null;
        if (isolation.Mode is not (PipelineIsolationDefinition.ModeProcess
            or PipelineIsolationDefinition.ModeContainer))
            return $"{label}: isolation mode '{isolation.Mode}' is invalid.";
        if (!isolation.IsContainer)
            return null;

        var hasImage = !string.IsNullOrWhiteSpace(isolation.Image);
        var hasToolchain = !string.IsNullOrWhiteSpace(isolation.Toolchain);
        if (hasImage == hasToolchain)
            return $"{label}: container isolation requires exactly one of 'image' or 'toolchain'.";
        if (hasImage && !ContainerExecutionContractValidator.IsImmutableImage(isolation.Image))
            return $"{label}: container image must end in an immutable '@sha256:<digest>' reference.";
        if (hasToolchain && !ContainerExecutionContractValidator.IsToolchainName(isolation.Toolchain))
            return $"{label}: toolchain key '{isolation.Toolchain}' is invalid.";
        if (!ContainerExecutionContractValidator.IsSupportedShell(isolation.Shell))
            return $"{label}: container shell '{isolation.Shell}' is unsupported.";
        if (hasToolchain && isolation.Shell is not null)
            return $"{label}: a toolchain-backed container takes its shell from the lock manifest.";
        return null;
    }

    private static void AddDefinitionError(
        HashSet<string> errors,
        string label,
        PipelineIsolationDefinition? isolation)
    {
        var error = ValidateDefinition(label, isolation);
        if (error is not null)
            errors.Add(error);
    }

    private static bool IsValidMemory(string? value) =>
        !string.IsNullOrWhiteSpace(value) && MemoryLimitPattern().IsMatch(value.Trim());

    private static bool IsValidCpus(string? value) =>
        !string.IsNullOrWhiteSpace(value) && CpusLimitPattern().IsMatch(value.Trim());
}
