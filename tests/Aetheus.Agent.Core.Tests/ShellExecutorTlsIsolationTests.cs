// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Executors;

namespace Aetheus.Agent.Core.Tests;

public sealed class ShellExecutorTlsIsolationTests
{
    [Fact]
    public void ConfigureEnvironment_ControlPlaneInsecureTlsDoesNotDisableGitTlsValidation()
    {
        var startInfo = new ProcessStartInfo();
        startInfo.Environment.Clear();
        var options = new AetheusAgentOptions { AllowInsecureCerts = true };

        ShellExecutorBase.ConfigureEnvironment(
            startInfo,
            options,
            new Dictionary<string, string> { ["SAFE_SETTING"] = "value" });

        Assert.Equal("value", startInfo.Environment["SAFE_SETTING"]);
        Assert.False(startInfo.Environment.ContainsKey("GIT_SSL_NO_VERIFY"));
    }
}
