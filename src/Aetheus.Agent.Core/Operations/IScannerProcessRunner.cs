// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;

namespace Aetheus.Agent.Core.Operations;

public interface IScannerProcessRunner
{
    Task<ExecutorResult> RunAsync(
        ProcessStartInfo process,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken ct);
}
