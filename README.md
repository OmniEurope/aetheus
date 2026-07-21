# Aetheus - Infrastructure Manager & CI/CD

Application complète de gestion d'infrastructure et CI/CD "fait maison" avec un serveur central, des agents distribués, et une interface web.

## Architecture

- **Serveur Central** : API ASP.NET Core + Blazor WebAssembly (tableau de bord)
- **Agents** : .NET Worker Services installables sur Linux (systemd) et Windows (Windows Service)
- **Communication** : Modèle Agent (Pull) avec HTTP Polling + SignalR (WebSocket) pour le temps réel
- **Base de données** : PostgreSQL (dev = conteneur local sur le port 15432 ; self-deploy prod = conteneur partagé persistant `aetheus-prod-database`, avec connexion externe configurable)

## Solution Structure

```
Aetheus.slnx
├── src/
│   ├── Aetheus.Back/              # API ASP.NET Core (.NET 10)
│   ├── Aetheus.Front/             # Blazor WebAssembly
│   ├── Aetheus.Shared/            # DTOs, Enums, Contrats API
│   ├── Aetheus.Agent.Core/        # Shared agent library (platform-agnostic)
│   ├── Aetheus.Agent.Windows/     # Windows Worker Service
│   ├── Aetheus.Agent.Linux/       # Linux Worker Service
│   ├── Aetheus.Cli/               # CLI tool (System.CommandLine)
│   └── Aetheus.Analyzers/         # Roslyn analyzers (custom rules)
├── tests/
│   ├── Aetheus.Back.Tests/        # xUnit + EF InMemory
│   ├── Aetheus.Back.IntegrationTests/ # xUnit + WebApplicationFactory + Testcontainers (real Postgres)
│   ├── Aetheus.Front.Tests/       # bUnit + NSubstitute
│   ├── Aetheus.Agent.Core.Tests/  # xUnit
│   ├── Aetheus.Analyzers.Tests/   # Roslyn analyzer regression tests (PRM001/PRM004)
│   └── Aetheus.E2E/              # Playwright (NUnit)
├── deploy/                           # Dockerfiles, compose, scripts, Apache config
├── templates/                        # Scaffolding (e.g. aetheus-module vertical-slice template)
└── scripts/                          # Repo maintenance scripts (e.g. add-spdx-header.ps1)
```

## Quick Start

### Prerequisites
- .NET SDK 10.0.202 exactly (pinned by `global.json`)
- Docker & Docker Compose (for local/remote deployment)
- Playwright browsers (for E2E tests): `pwsh bin/Debug/net10.0/playwright.ps1 install` from the `tests/Aetheus.E2E/` directory

### Development

```powershell
.\ylaunch.ps1                     # Build + start API (5301) + Front (5401) + open browser
.\ylaunch.ps1 -s                  # Build + start servers (no browser)
.\ylaunch.ps1 -r                  # Reset DB (fresh seed), then start servers
.\ylaunch.ps1 -hr                 # Start with hot reload (dotnet watch)
.\ylaunch.ps1 -tb                 # Run backend unit tests
.\ylaunch.ps1 -tf                 # Run frontend unit tests
.\ylaunch.ps1 -t                  # Run ALL unit tests (back + front + agent-core + analyzers)
.\ylaunch.ps1 -ti                 # Run integration tests (Testcontainers; dev DB untouched)
.\ylaunch.ps1 -te                 # Run E2E tests on an isolated ephemeral DB (:15433); dev DB untouched
.\ylaunch.ps1 -tec <filter>       # E2E tests with category filter (name, number, or mix)
.\ylaunch.ps1 -ta                 # Run EVERYTHING (unit + integration + E2E)
.\ylaunch.ps1 -c                  # Tests with code coverage report
.\ylaunch.ps1 -mf                 # Start Backend + Frontend (default)
.\ylaunch.ps1 -ma                 # Start Agent Worker Service
.\ylaunch.ps1 -ra                 # SSH tunnel for remote agent (target from .remoteagent)
.\ylaunch.ps1 -vps                # Build + start the disposable local VPS-sim (blank box; -full for the full box)
.\ylaunch.ps1 -h                  # Show help
.\ylaunch.ps1 -hl                 # Show detailed help with examples
```

> Run a second worktree's stack alongside another one with `ylaunch.ps1 -w`
> (`-Worktree`) - it picks a free, non-default port triplet, writes a gitignored
> `.ylaunch.local` (`BACK_HTTPS_PORT` / `BACK_HTTP_PORT` / `FRONT_PORT` + `LABEL`
> + `WORKTREE`), and launches on those ports. The dev Postgres (15432) stays
> shared. A dev-only top-bar banner (hidden in Production) surfaces the
> environment, the worktree `LABEL`, and the current git branch so you can tell
> instances apart.

### Manual

```powershell
dotnet build Aetheus.slnx
dotnet test Aetheus.slnx
dotnet run --project src/Aetheus.Back   # https://localhost:5301
dotnet run --project src/Aetheus.Front  # https://localhost:5401
dotnet run --project src/Aetheus.Agent.Windows  # Agent Windows
dotnet run --project src/Aetheus.Agent.Linux    # Agent Linux
```

### Agent Installation

**Windows:**
1. Download the agent archive from the Aetheus UI (or directly via `{SERVER_URL}/downloads/aetheus-agent-win-x64.zip`).
2. Extract the archive to the desired install directory (e.g. `C:\Program Files\AetheusAgent`).
3. Run the install script from inside the extracted directory:
```powershell
powershell -ExecutionPolicy Bypass -File .\install-agent-windows.ps1
# Prompts for server URL and registration token, registers a Windows Service
```

**Linux:**
```bash
sudo sh deploy/scripts/install-agent-linux.sh
# Prompts for server URL and registration token, creates systemd service
```

## Ports (Dev)

| Service | URL |
|---------|-----|
| Backend API | `https://localhost:5301` (OpenAPI at `/openapi/v1.json`) |
| Frontend WASM | `https://localhost:5401` |
| SignalR Hubs | `https://localhost:5301/hubs/*` |
| Health | `/health` (authenticated), `/health/live` (anonymous liveness), `/health/ready` (anonymous DB-readiness) |

## Local Docker

```powershell
.\deploy\scripts\local.ps1           # Build + start
.\deploy\scripts\local.ps1 -Rebuild  # Force rebuild (no cache)
.\deploy\scripts\local.ps1 -Down     # Stop
```

### VPS-sim local jetable (test uniquement)

Boîte Ubuntu jetable pour exercer l'agent (collectors, vrai script d'installation) en local
plutôt que sur la VPS de prod. Une seule box à la fois, deux saveurs partageant la même identité
(conteneur `aetheus-vpssim`, port SSH 2222) : **vierge** par défaut (Ubuntu nu - systemd + sshd,
l'ardoise propre sur laquelle on installe petit à petit) ou **pleine** (`-full` - Docker + tous les
services des collectors pré-cuits). Jamais déployée en prod.

```powershell
.\ylaunch.ps1 -vps           # box VIERGE (défaut) - build + start + exit
.\ylaunch.ps1 -vps -net10    # box vierge + .NET SDK 10.0.202 pré-installé
.\ylaunch.ps1 -vps -full     # box PLEINE (tous les collectors pré-cuits)
.\ylaunch.ps1 -vps -r        # reset de la saveur courante (drop ses volumes)
.\ylaunch.ps1 -vps -full -aa # box pleine + auto-install de l'agent au boot
```

## Remote Deployment (Linux)

Set `GITREMOTE` and `SERVERNAME` for your own repository and DNS zone. The
defaults intentionally use public placeholders and cannot target the original
project infrastructure.

```sh
GITREMOTE=https://github.com/your-organization/aetheus.git \
SERVERNAME=example.com \
sh deploy/scripts/deploy.sh                        # prod, branch main
sh deploy/scripts/deploy.sh -b develop             # prod, branch develop
sh deploy/scripts/deploy.sh -e accept              # accept, branch main
sh deploy/scripts/deploy.sh -r                     # rebuild (no cache)
sh deploy/scripts/deploy.sh -a                     # agent-only version bump
sh deploy/scripts/deploy.sh -d                     # stop
sh deploy/scripts/deploy.sh -k                     # regenerate JWT key
sh deploy/scripts/deploy.sh -x                     # run runtime diagnostics only
sh deploy/scripts/deploy.sh -p                     # git pull only (no build/restart)
sh deploy/scripts/deploy.sh -s                     # skip agent publishing (faster)
sh deploy/scripts/deploy.sh -w -f                  # WIPE env incl. DB volume
```

## Glossary

| Term | Definition |
|------|-----------|
| **Server** | A managed machine (Linux VPS or Windows) running a Aetheus agent. |
| **Agent** | A .NET Worker Service installed on a Server that collects metrics and executes tasks. |
| **Pipeline** | A YAML-defined CI/CD workflow with stages, jobs, steps, and triggers. |
| **Run** | A single execution of a Pipeline, producing logs and artifacts. |
| **Stage** | A sequential group of jobs or steps within a Pipeline run. |
| **Job** | An execution unit inside a stage (Azure DevOps-style). Each job carries its own agent/pool and steps. Stages using the legacy format have implicit single jobs. |
| **Step** | An atomic unit of work within a Job or Stage (shell command, Docker exec, etc.). |
| **Environment** | A logical deployment target (e.g., staging, production) with associated servers. |
| **Project** | An organizational unit grouping pipelines, environments, Git repos, and secrets. |
| **Organization** | A tenant scope - servers, projects, and tokens belong to an organization. |
| **Variable Library** | A named set of key-value variables, scoped to a project, environment, or project-server. Injected into pipeline runs. |
| **Vault** | An encrypted secret store scoped to a project, environment, or project-server (AES-256 at rest). |
| **Agent Pool** | A named group of servers available as pipeline execution targets. Stages can reference a pool instead of a specific server. |
| **Artifact** | A build output (zip archive) collected from a pipeline stage, stored on disk with configurable retention. |
| **Deploy target** | A server whose agent was installed with `--module deployment`; it can apply a build artifact or release on its host (binary symlink-flip + systemd restart, or container compose up) via a `type: deploy` pipeline step. |
| **Monitored App** | A deployed application watched by the AppMonitoring module (PLAN-001): availability via agent or backend black-box probes, plus optional OTLP metrics/logs/errors pushed to `/api/ingest/otlp/v1/*` with a per-app ingestion key. Scoped to a Project; on-fleet (linked to a Server) or off-fleet (public URL). |

## Security Model

- **Authentication**: JWT (1h access tokens) with refresh-token rotation (30d). 2FA TOTP (RFC 6238) with 8 recovery codes. Account lockout after 5 failed attempts (exponential backoff).
- **Authorization**: roles administrables (`Admin` et `Contributor` sont crees au seed) avec permissions par ressource via `ResourceAuthorizationService`. Isolation de la propriete par organisation.
- **Agents**: Non-root by default, zero-elevation baseline. Capabilities opt-in via install flags. Tokens stored with DPAPI (Windows) or file permissions (Linux).
- **Secrets**: AES-256-GCM encrypted in PostgreSQL. Never logged, masked in output.
- **Audit**: SHA-256 hash-chained audit log with tamper-detection endpoint. Append-only at the database level (DELETE/UPDATE revoked on the `AuditLogs` table).

## Learn More

- [Contributing](CONTRIBUTING.md) - how to contribute
- [Security](SECURITY.md) - vulnerability reporting and security scope
- [Publication manifest](PUBLICATION_MANIFEST.md) - public/private boundary applied to this snapshot

## License

Licensed under the [European Union Public Licence v1.2](LICENSE) (EUPL-1.2).
