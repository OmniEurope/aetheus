// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Operations;

internal static class AptProcessRunner
{
    public static Task<ExecutorResult> RunSudoAsync(
        IReadOnlyList<string> argv,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var startInfo = SudoProcessStartInfo.Create();
        startInfo.Environment["DEBIAN_FRONTEND"] = "noninteractive";
        foreach (var argument in argv) startInfo.ArgumentList.Add(argument);
        return ProcessRunner.RunAsync(
            startInfo, timeoutSeconds, onOutput, logger, cancellationToken);
    }
}
