<!-- SPDX-License-Identifier: EUPL-1.2 -->
# Apache

Apache HTTP Server state collection and management per server: virtual hosts, loaded modules, service actions (start/stop/restart/reload), log tailing, vhost config editing, and .htaccess management.

## API Surface

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/api/servers/{serverId}/apache` | GET | User | Apache state summary |
| `/api/servers/{serverId}/apache/modules` | GET | User | Loaded modules |
| `/api/servers/{serverId}/apache/vhosts` | GET | User | Virtual hosts |
| `/api/servers/{serverId}/apache/action` | POST | User | Execute action (start/stop/restart/reload) |
| `/api/servers/{serverId}/apache/logs` | POST | User | Tail Apache logs |
| `/api/servers/{serverId}/apache/vhosts/{site}/config` | GET | User | Read vhost config |
| `/api/servers/{serverId}/apache/vhosts/{site}/config` | PUT | User | Save vhost config |
| `/api/servers/{serverId}/apache/htaccess` | GET | User | Read .htaccess |
| `/api/servers/{serverId}/apache/htaccess` | PUT | User | Save .htaccess |

## Key Classes

- `ApacheController` -- thin controller with `ValidateServerExistsFilter`
- `IApacheService` / `ApacheService` -- business logic, delegates to agent tasks
- `CachedApacheService` -- in-memory cache decorator
- `IApacheRepository` / `ApacheRepository` -- EF data access
- `ApacheCommandHelper` -- builds agent task payloads

## Cross-Module Dependencies

- Depends on: Audit, Shared (`ValidateServerExistsFilter`)
- Depended on by: (none)
