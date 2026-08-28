// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;

namespace Aetheus.Agent.Core.Operations;

internal static class PipelineMetricEmitter
{
    internal static Task EmitAsync(
        Func<string, TaskLogLevel, Task> onOutput,
        string key,
        string type,
        string unit,
        double value) =>
        onOutput(
            $"##aetheus[pipelinemetric key={key};type={type};unit={unit}]" +
            value.ToString("0.######", CultureInfo.InvariantCulture),
            TaskLogLevel.Info);
}
