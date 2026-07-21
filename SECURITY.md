# Security Policy

## Supported Versions

| Version   | Supported          |
|-----------|---------------------|
| 0.2.x-dev | Yes (active dev)   |
| 0.1.x     | Yes                |

Security patches target the active development line and the latest stable release listed above.

## Reporting a Vulnerability

**Do not open a public issue for security vulnerabilities.**

Use the repository's **Security** tab and select **Report a vulnerability** to
open a private GitHub Security Advisory. Include:

1. A description of the vulnerability and its impact.
2. Steps to reproduce (or a proof-of-concept).
3. Affected component(s): `Back` (API), `Front` (Blazor WASM), `Agent` (Worker Service), `Deploy` (Docker / scripts).
4. Your suggested severity (Critical / High / Medium / Low).

### Response Timeline

| Step                        | Target          |
|-----------------------------|-----------------|
| Acknowledgement             | 48 hours        |
| Initial triage              | 7 days          |
| Fix or mitigation available | 90 days (embargo)|

During the embargo window the vulnerability details remain private. If the issue
is not resolved within 90 days, the reporter may disclose it publicly.

## Scope

The following components are in scope:

- **Aetheus.Back** -- Web API, authentication, authorization (RBAC), secrets management, EF Core data layer.
- **Aetheus.Agent.Core / Agent.Linux / Agent.Windows** -- command execution, token storage, metrics collection, plugin system.
- **Aetheus.Front** -- Blazor WASM client, JWT handling, SignalR connections.
- **Deploy** -- Dockerfiles, compose files, deploy scripts, Apache reverse-proxy config.

Out of scope: third-party dependencies (report upstream), infrastructure hosting configuration.

## Security Design

- JWT authentication with refresh-token renewal.
- RBAC via `ResourceAuthorizationService` with org-scoped ownership.
- Secrets encrypted at rest (AES-256-GCM); never logged.
- Agent: non-root by default, zero-elevation baseline, opt-in capabilities.
- Kestrel request body capped at 10 MB.
- XXE-safe XML parsing (`SafeXml` helper).
- All DTO text properties carry `[Required]` + `[StringLength]` validation.
- Pagination clamped server-side (`Math.Clamp(pageSize, 1, 200)`).

## Acknowledgements

We appreciate responsible disclosure and will credit reporters (with permission)
in release notes.
