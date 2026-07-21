// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Operations;

namespace Aetheus.Agent.Core.Tests;

/// <summary>PLAN-001 phase 2 zero-config: the deploy executor's app-env extraction + env-file writing.</summary>
public class DeployAppEnvTests
{
    [Fact]
    public void ExtractAppEnv_StripsPrefix_AndIgnoresControlKeys()
    {
        var input = new Dictionary<string, string>
        {
            ["AETHEUS_DEPLOY_APPENV_OTEL_EXPORTER_OTLP_ENDPOINT"] = "https://x/api/ingest/otlp/v1",
            ["AETHEUS_DEPLOY_APPENV_OTEL_EXPORTER_OTLP_HEADERS"] = "x-aetheus-ingest-key=abc",
            ["AETHEUS_DEPLOY_ARTIFACT_ID"] = "5",
            ["SOME_OTHER"] = "keep-out"
        };

        var appEnv = DeployOperationExecutor.ExtractAppEnv(input);

        Assert.Equal(2, appEnv.Count);
        Assert.Equal("https://x/api/ingest/otlp/v1", appEnv["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        Assert.Equal("x-aetheus-ingest-key=abc", appEnv["OTEL_EXPORTER_OTLP_HEADERS"]);
        Assert.False(appEnv.ContainsKey("AETHEUS_DEPLOY_ARTIFACT_ID"));
    }

    [Fact]
    public void WriteAppEnvFile_WritesKeyValueLines_ReturnsPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "prom-appenv-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var path = DeployOperationExecutor.WriteAppEnvFile(dir, new Dictionary<string, string>
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "https://x/api/ingest/otlp/v1",
                ["OTEL_EXPORTER_OTLP_HEADERS"] = "x-aetheus-ingest-key=abc=="
            });

            Assert.NotNull(path);
            var content = File.ReadAllText(path!);
            Assert.Contains("OTEL_EXPORTER_OTLP_ENDPOINT=https://x/api/ingest/otlp/v1\n", content);
            // First '=' splits key/value; the base64 padding '==' stays in the value.
            Assert.Contains("OTEL_EXPORTER_OTLP_HEADERS=x-aetheus-ingest-key=abc==\n", content);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void WriteAppEnvFile_Empty_ReturnsNull()
    {
        Assert.Null(DeployOperationExecutor.WriteAppEnvFile(Path.GetTempPath(), new Dictionary<string, string>()));
    }
}
