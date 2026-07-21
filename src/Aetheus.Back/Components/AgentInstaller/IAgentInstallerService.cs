// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.AgentInstaller;

/// <summary>
/// Builds a one-shot installer script (bash for Linux, PowerShell for Windows) pre-filled
/// with the registration token + server URL so an operator can run a single curl|bash
/// (or iwr|iex) line instead of stepping through the wizard manually.
/// </summary>
public interface IAgentInstallerService
{
    /// <summary>
    /// Builds the installer payload. Returns the raw script body, MIME type and suggested
    /// download filename. The token is embedded literally in the script body - this is the
    /// same secret the wizard already exposes on screen.
    /// </summary>
    /// <param name="pipelineRunner">When true (default), the script installs the CI/CD toolchain
    /// (.NET SDK, git) so the agent can run pipelines. No elevation is granted.</param>
    /// <param name="serverManagement">When true (Linux only), the script also installs the
    /// server-administration capabilities. Opt-in; defaults to false.</param>
    AgentInstallerScript Build(string platform, string registrationToken, string serverBaseUrl, string version,
        bool pipelineRunner = true, bool serverManagement = false);
}

public sealed record AgentInstallerScript(string Body, string ContentType, string FileName);
