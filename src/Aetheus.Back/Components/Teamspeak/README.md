<!-- SPDX-License-Identifier: EUPL-1.2 -->
# Teamspeak

TeamSpeak server management per server: state collection, channel CRUD, client actions (kick/ban/move/poke), server groups, tokens, snapshots, complaints, global messages, and graceful restart.

## API Surface

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/api/servers/{serverId}/teamspeak` | GET | User | TS state (channels, clients) |
| `/api/servers/{serverId}/teamspeak/action` | POST | User | Service action (start/stop) |
| `/api/servers/{serverId}/teamspeak/setup` | POST | Admin | Initial TS setup |
| `/api/servers/{serverId}/teamspeak/logs` | POST | User | Tail TS logs |
| `/api/servers/{serverId}/teamspeak/kick` | POST | User | Kick client |
| `/api/servers/{serverId}/teamspeak/ban` | POST | User | Ban client |
| `/api/servers/{serverId}/teamspeak/move-client` | POST | User | Move client to channel |
| `/api/servers/{serverId}/teamspeak/poke` | POST | User | Poke client |
| `/api/servers/{serverId}/teamspeak/bans` | GET | User | List bans |
| `/api/servers/{serverId}/teamspeak/bans/{banId}` | DELETE | User | Unban |
| `/api/servers/{serverId}/teamspeak/channels` | POST | User | Create channel |
| `/api/servers/{serverId}/teamspeak/channels/{id}` | PUT | User | Edit channel |
| `/api/servers/{serverId}/teamspeak/channels/{id}` | DELETE | User | Delete channel |
| `/api/servers/{serverId}/teamspeak/server` | PUT | User | Edit server settings |
| `/api/servers/{serverId}/teamspeak/message` | POST | User | Global message |
| `/api/servers/{serverId}/teamspeak/clientinfo` | POST | User | Client detail |
| `/api/servers/{serverId}/teamspeak/graceful-restart` | POST | User | Graceful restart |
| `/api/servers/{serverId}/teamspeak/snapshots` | POST | User | Create snapshot |
| `/api/servers/{serverId}/teamspeak/snapshots/deploy` | POST | Admin | Deploy snapshot |
| `/api/servers/{serverId}/teamspeak/server-groups` | GET | User | List server groups |
| `/api/servers/{serverId}/teamspeak/server-groups/add` | POST | User | Add client to group |
| `/api/servers/{serverId}/teamspeak/server-groups/remove` | POST | User | Remove from group |
| `/api/servers/{serverId}/teamspeak/tokens` | GET/POST/DELETE | Admin | Token management |
| `/api/servers/{serverId}/teamspeak/server-info` | GET | User | Server info |
| `/api/servers/{serverId}/teamspeak/complaints` | GET/DELETE | User | Complaints |

## Key Classes

- `TeamspeakController` -- thin controller with `ValidateServerExistsFilter`
- `ITeamspeakService` / `TeamspeakService` -- business logic, delegates to agent tasks
- `ITeamspeakRepository` / `TeamspeakRepository` -- EF data access
- `TeamspeakCommandHelper` -- builds agent task payloads

## Cross-Module Dependencies

- Depends on: Audit, Shared (`ValidateServerExistsFilter`)
- Depended on by: (none)
