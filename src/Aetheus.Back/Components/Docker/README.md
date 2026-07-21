<!-- SPDX-License-Identifier: EUPL-1.2 -->
# Docker

Docker management per server: containers, images, networks, volumes, compose stacks, resource limits, container inspection, env vars, file browsing, image builds, prune, and shell exec.

## API Surface

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/api/servers/{serverId}/docker/containers` | GET | User | List containers |
| `/api/servers/{serverId}/docker/action` | POST | User | Container action (start/stop/restart/remove) |
| `/api/servers/{serverId}/docker/containers/logs` | POST | User | Container logs |
| `/api/servers/{serverId}/docker/containers/{id}/inspect` | POST | User | Inspect container |
| `/api/servers/{serverId}/docker/containers/{id}/env` | POST | User | Container env vars |
| `/api/servers/{serverId}/docker/containers/browse` | POST | User | Browse container filesystem |
| `/api/servers/{serverId}/docker/images` | GET | User | List images |
| `/api/servers/{serverId}/docker/images/pull` | POST | User | Pull image |
| `/api/servers/{serverId}/docker/images/{id}` | DELETE | Admin | Remove image |
| `/api/servers/{serverId}/docker/build` | POST | User | Build image |
| `/api/servers/{serverId}/docker/networks` | GET | User | List networks |
| `/api/servers/{serverId}/docker/volumes` | GET | User | List volumes |
| `/api/servers/{serverId}/docker/compose` | GET | User | List compose stacks |
| `/api/servers/{serverId}/docker/compose/action` | POST | User | Compose action (up/down/restart) |
| `/api/servers/{serverId}/docker/compose/{stack}/file` | GET | User | Read compose file |
| `/api/servers/{serverId}/docker/compose/file` | PUT | User | Save compose file |
| `/api/servers/{serverId}/docker/prune` | POST | Admin | Prune unused resources |
| `/api/servers/{serverId}/docker/resource-limits` | POST | User | Update resource limits |
| `/api/servers/{serverId}/docker/exec` | POST | Admin | Shell exec in container |

## Key Classes

- `DockerController` -- thin controller with `ValidateServerExistsFilter`
- `IDockerService` / `DockerService` -- business logic, delegates to agent tasks
- `CachedDockerService` -- in-memory cache decorator
- `IDockerRepository` / `DockerRepository` -- EF data access
- `DockerCommandHelper` -- builds agent task payloads
- `DockerIdValidator` -- validates container/image ID format

## Cross-Module Dependencies

- Depends on: Audit, Shared (`ValidateServerExistsFilter`)
- Depended on by: (none)
