<!-- SPDX-License-Identifier: EUPL-1.2 -->
# Servers

Fleet management for Linux and Windows servers: registration, heartbeats, diagnostics, service control, agent self-update, and per-server resource aggregation (projects, pipelines, vaults, tasks, releases).

## API Surface

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/api/servers` | GET | User | List servers (paginated, filterable by type/status) |
| `/api/servers/agent-compatibility-summary` | GET | User | Compatibility totals for the caller-visible fleet |
| `/api/servers/names` | GET | User | Server name list |
| `/api/servers/{id}` | GET | User | Server detail |
| `/api/servers/{id}` | PUT | User | Update server |
| `/api/servers/{id}` | DELETE | Admin | Delete server |
| `/api/servers/{id}/pipeline-runner` | POST | Admin | Opt-in/out pipeline execution |
| `/api/servers/{id}/container-isolation-policy` | POST | ServerAdmin | Exiger ou non l'isolation conteneur sur ce runner |
| `/api/servers/{id}/heartbeat` | POST | AgentToken | Agent heartbeat |
| `/api/servers/{id}/contact-agent` | POST | User | Probe agent reachability |
| `/api/servers/{id}/diagnostic` | GET | User | Offline diagnostic summary |
| `/api/servers/{id}/agent/update` | POST | Admin | Queue agent self-update |
| `/api/servers/{id}/agent/progress` | POST | AgentToken | Report update progress |
| `/api/servers/agent/update-all-preview` | GET | Admin | Preview fleet-wide agent updates |
| `/api/servers/agent/update-all` | POST | Admin | Queue update for all servers |
| `/api/servers/{id}/projects` | GET | User | Projects on server |
| `/api/servers/{id}/pipelines` | GET | User | Pipelines targeting server |
| `/api/servers/{id}/variable-libraries` | GET | User | Variable libraries |
| `/api/servers/{id}/vaults` | GET | User | Vaults linked to server |
| `/api/servers/{id}/releases` | GET | User | Releases on server |
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
- `ServerTimeoutService` (in `Services/`, outside this module) -- background: marks servers offline on heartbeat timeout

## Cross-Module Dependencies

- Depends on: Audit, Auth (token renewal), AgentUpdate, Shared
- Depended on by: Projects, AgentUpdate, Apache/Docker/Teamspeak (via `ValidateServerExistsFilter`)
