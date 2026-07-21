# Public Distribution Manifest

This repository is the public, history-free distribution of Aetheus. It is
licensed under the [European Union Public Licence v1.2](LICENSE).

The manifest documents the boundary applied on 2026-07-21. It is an allowlist:
new top-level content from the private development repository must be reviewed
before it can be copied here.

## Kept

| Path | Purpose |
|------|---------|
| `src/` | Product source code |
| `tests/` | Unit, integration, architecture and E2E tests that apply to the public distribution |
| `deploy/docker/`, `deploy/compose/`, `deploy/apache/` | Generic container and reverse-proxy deployment assets |
| `deploy/scripts/` | Generic local/remote deployment and agent installation scripts |
| `deploy/pipelines/` | Reproducible sample and test fixtures; host names are placeholders only |
| `templates/` | Public project scaffolding |
| `.github/workflows/build-test.yml` | Public CI, tests, SPDX and vulnerability checks |
| `.husky/` | Contributor pre-commit checks |
| `scripts/add-spdx-header.ps1` | EUPL SPDX header maintenance |
| `scripts/ylaunch-core.ps1`, `ylaunch.ps1`, `ybaunch.sh` | Local development launchers |
| `README.md`, `CONTRIBUTING.md`, `SECURITY.md` | Public project documentation |
| `LICENSE`, `THIRD_PARTY_NOTICES.md` | EUPL-1.2 and dependency notices |
| Root build/config files | Solution, SDK, NuGet, coverage, editor, Git and Docker configuration |

## Removed

| Private path | Reason |
|--------------|--------|
| `.claude/`, `CLAUDE.md`, `AGENTS.md` | Agent instructions, handoffs, audit history and private working context |
| `.pipeline/` | Self-deployment pipelines tied to the private control plane |
| `docs/` | Internal architecture notes, audits, plans and operational runbooks |
| `CHANGELOG.md` | Private development history and operational detail |
| `site/` | Separately deployed private showcase site |
| `scratch/` | Local scratch space |
| `z*.prompt.md` | Private prompts, backlog and work notes |
| `.github/workflows/release.yml` | Private release process coupled to the private changelog |
| `scripts/release-lab.ps1` | Private end-to-end self-deployment campaign |
| `src/Aetheus.Back/Data/ReleaseLabSeeder.cs` and its tests/hooks | Private release-lab bootstrap path |
| Internal QA/release helper scripts under `deploy/scripts/` | Helpers used only by the removed self-deployment pipelines |
| `tests/Aetheus.Back.Tests/Pipelines/SelfDeployPipelineYamlParseTests.cs` | Assertions for excluded private pipeline definitions |

## Adapted

- Production domains, host names, repository URLs and contact details were
  replaced with reserved `example.com` values or GitHub-native reporting.
- Public CI targets only `main` and `develop`; it does not publish releases.
- Architecture guards were narrowed only where their input was deliberately
  excluded by this manifest. Product security and correctness guards remain.
- `README.md` and `CONTRIBUTING.md` no longer link to private documentation.

## Never add

- Credentials, tokens, private keys, `.env*`, local overrides or database dumps.
- Real production domains, IP addresses, server names or filesystem paths.
- Private incident reports, audit outputs, handoffs or infrastructure runbooks.
- Generated build output (`bin/`, `obj/`, test results, coverage and artifacts).

## Publication checks

Before publishing a revision:

1. Verify that every top-level path is listed in **Kept** or explicitly reviewed.
2. Search for private domains, personal addresses, public server IPs and key/token signatures.
3. Run `dotnet build Aetheus.slnx -c Release --warnaserror`.
4. Run `dotnet test Aetheus.slnx -c Release --no-build --filter "Category!=E2E"`.
5. Review `git status` and the staged diff before any remote is configured or pushed.

No remote repository or automatic publication is configured by this manifest.
