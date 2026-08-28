// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Middleware;
using Microsoft.Extensions.Logging;

namespace Aetheus.Back.Tests;

public sealed class JsonFileLoggerProviderTests
{
    [Fact]
    public void StructuredCorrelationId_IsWrittenAsDedicatedJsonProperty()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"aetheus-logger-{Guid.NewGuid():N}");
        try
        {
            using (var provider = new JsonFileLoggerProvider(directory, LogLevel.Information))
            {
                var logger = provider.CreateLogger("CorrelationTest");
                logger.LogError("Failure; correlation {CorrelationId}", "request-42");
            }

            var content = string.Join(Environment.NewLine, Directory.GetFiles(directory).Select(File.ReadAllText));
            Assert.Contains("\"CorrelationId\":\"request-42\"", content, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Saturation_IncrementsCounter_AndWritesAggregatedWarning()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"aetheus-logger-{Guid.NewGuid():N}");
        var drainGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            using (var provider = new JsonFileLoggerProvider(
                       directory, LogLevel.Information, TimeProvider.System, capacity: 1, drainGate.Task))
            {
                var logger = provider.CreateLogger("SaturationTest");
                logger.LogInformation("first entry fills the bounded channel");
                logger.LogInformation("second entry must be rejected");

                Assert.Equal(1, provider.DroppedEntries);
                drainGate.SetResult();
            }

            var content = string.Join(Environment.NewLine, Directory.GetFiles(directory).Select(File.ReadAllText));
            Assert.Contains("File logger saturated: 1 entries were dropped (total 1).", content, StringComparison.Ordinal);
            Assert.Contains("JsonFileLoggerProvider", content, StringComparison.Ordinal);
        }
        finally
        {
            drainGate.TrySetResult();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
