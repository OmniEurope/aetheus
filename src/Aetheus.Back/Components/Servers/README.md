<!-- SPDX-License-Identifier: EUPL-1.2 -->
# Servers

Fleet management for Linux and Windows servers: registration, heartbeats, diagnostics, service control, agent self-update, and per-server resource aggregation (projects, pipelines, vaults, tasks, releases).

## API Surface

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/api/servers` | GET | User | List servers (paginated, filterable by type/status) |
| `/api/servers/agent-compatibility-summary` | GET | User | Compatibility totals for the caller-visible fleet |
| `/api/servers/filter-values` | GET | User | OS, agent versions and tags offered by the servers list's column filters |
| `/api/servers/names` | GET | User | Server name list |
| `/api/servers/{id}` | GET | User | Server detail |
| `/api/servers/{id}` | PUT | User | Update server |
| `/api/servers/{id}` | DELETE | Admin | Retire server: row and links kept, agent tokens revoked, hidden from lists and dispatch; re-enrolling the same machine (machine-id hash, then hostname) revives it |
| `/api/servers/retired` | GET | User | Retired servers (paginated) |
| `/api/servers/{id}/permanent` | DELETE | Admin | Permanently delete a retired server with its links and backup policies (409 on an active server) |
| `/api/servers/{id}/pipeline-runner` | POST | Admin | Opt-in/out pipeline execution |
| `/api/servers/{id}/container-isolation-policy` | POST | ServerAdmin | Exiger ou non l'isolation conteneur sur ce runner |
| `/api/servers/{id}/heartbeat` | POST | AgentToken | Agent heartbeat |
| `/api/servers/{id}/contact-agent` | POST | User | Probe agent reachability |
| `/api/servers/{id}/diagnostic` | GET | User | Offline diagnostic summary |
| `/api/servers/{id}/agent/update` | POST | Admin | Queue agent self-update (always queues, even when the agent is already current -- see `Components/AgentUpdate/README.md`) |
| `/api/servers/{id}/agent/progress` | POST | AgentToken | Report update progress |
| `/api/servers/agent/update-all-preview` | GET | Admin | Preview fleet-wide agent updates |
| `/api/servers/agent/update-all` | POST | Admin | Queue update for all servers |
| `/api/servers/{id}/projects` | GET | User | Projects on server |
| `/api/servers/{id}/pipelines` | GET | User | Pipelines targeting server |
| `/api/servers/{id}/variable-libraries` | GET | User | Variable libraries |
| `/api/servers/{id}/vaults` | GET | User | Vaults linked to server |
| `/api/servers/{id}/services/action` | POST | User | Start/stop/restart a service |
| `/api/servers/{id}/services/install` | POST | User | Install a service |
| `/api/servers/{id}/services/uninstall` | POST | User | Uninstall a service |
| `/api/servers/{id}/services/logs` | POST | User | Request service log tail |
| `/api/servers/{id}/security-updates` | GET | User | Lire les mises a jour systeme signalees par l'agent |
| `/api/servers/{id}/system/upgrade` | POST | User | Simuler ou mettre en file une mise a niveau systeme |
| `/api/servers/{id}/firewall` | GET | User | Lire l'etat et les regles du pare-feu |
| `/api/servers/{id}/firewall/allow` | POST | User | Autoriser une regle de pare-feu |
| `/api/servers/{id}/firewall/deny` | POST | User | Refuser une regle de pare-feu |
| `/api/servers/{id}/firewall/delete` | POST | User | Supprimer une regle de pare-feu |
| `/api/servers/{id}/firewall/toggle` | POST | User | Activer ou desactiver le pare-feu |
| `/api/servers/{id}/tasks` | GET | User | Tasks for server (paginated) |
| `/api/servers/{id}/logs` | GET | User | Task logs for server |
| `/api/servers/{id}/ports/observe` | POST | User (Server Read) | Queue an on-demand scan of the listening ports (`ServerPortObservationController`) |
| `/api/servers/{id}/ports/observed` | POST | AgentToken | Agent pushes what its port scan saw, scoped to the token's own server |

The releases of a server (`/api/servers/{id}/releases` and its `filter-values`) are served by the Releases module (`ReleasesController`).

## Key Classes

- `ServersController` -- thin controller, RBAC-gated
- `ServerService` -- business logic (implements segregated facets below)
- `IServerLifecycleService` -- CRUD, project/pipeline/vault aggregation
- `IServerHeartbeatService` -- heartbeat processing
- `IServerServiceManagementService` -- systemd/Windows service control
- `IServerAgentContactService` -- agent reachability probe
- `IServerDiagnosticService` -- offline diagnostics
- `IServerRepository` / `ServerRepository` -- EF data access
- `ServerHeartbeatProcessor` / `ServerHeartbeatCapabilityProjector` -- heartbeat ingestion and capability projection
- `ServerDiagnosticAnalyzer`, `ServerAgentContactProbe`, `ServerServiceManager` -- diagnostics, reachability, service control collaborators
- `IServerRetirementService` / `ServerRetirementService` -- retire, list retired, permanently delete
- `SudoersDriftMonitor` -- compares the sudoers fingerprint each heartbeat reports with the server's baseline; raises `SudoersDriftDetectedEvent` once per drifted state (fingerprint persisted on the server), then a reminder every 6 h while the drift persists (`Server.SudoersDriftAlertedAt`), and re-captures the baseline when a heartbeat confirms an agent update (recette R2-023). The event is audited and handled by `Handlers/SudoersDriftNotificationHandler`, which records a persistent notification for every active administrator and hands it to the `alert.triggered` notification rules
- `ServerTimeoutService` (in `Services/`, outside this module) -- background: marks servers offline on heartbeat timeout

## Cross-Module Dependencies

- Depends on: Audit, Auth (token renewal), AgentUpdate, Notifications (sudoers drift notifications), Tasks (agent task queueing), Shared
- Depended on by: Projects, AgentUpdate, Apache/Docker/Teamspeak (via `ValidateServerExistsFilter`)
