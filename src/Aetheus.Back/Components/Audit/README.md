<!-- SPDX-License-Identifier: EUPL-1.2 -->
# Audit

Immutable action logging with hash-chain integrity: every mutating operation across the platform is recorded with actor, action, entity type, and a SHA-256 chain linking each entry to its predecessor for tamper detection.

## API Surface

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/api/audit` | GET | Admin | List audit logs (paginated, filterable) |
| `/api/audit/actions` | GET | Admin | Distinct action names |
| `/api/audit/entity-types` | GET | Admin | Distinct entity types |
| `/api/audit/verify-chain` | GET | Admin | Verify hash-chain integrity |
| `/api/audit/{id}/verify` | GET | Admin | Verifier une entree et la chaine jusqu'a celle-ci |

## Key Classes

- `AuditController` -- thin controller, Admin-only
- `IAuditService` / `AuditService` -- log recording, paginated queries, filtering
- `IAuditChainService` / `AuditChainService` -- SHA-256 hash-chain verification
- `IAuditRepository` / `AuditRepository` -- EF data access

## Cross-Module Dependencies

- Depends on: (none -- leaf module)
- Depended on by: Servers, Pipelines, Tasks, Auth, Git, Apache, Teamspeak, Docker, Vaults, and most other modules (all call `IAuditService` to record actions)
