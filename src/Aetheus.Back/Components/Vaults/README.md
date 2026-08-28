<!-- SPDX-License-Identifier: EUPL-1.2 -->
# Vaults

AES-256 encrypted secret storage: vault CRUD, secret lifecycle (create/update/rotate/delete), version history, bulk import, and key export. Secrets are encrypted at rest and injected into pipeline runs at execution time.

## API Surface

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/api/vaults` | GET | User | List vaults (paginated, filterable by project/environment/project-server) |
| `/api/vaults/names` | GET | User | Vault name list |
| `/api/vaults/{id}` | GET | User | Vault detail with secrets |
| `/api/vaults` | POST | User | Create vault |
| `/api/vaults/{id}` | PUT | User | Update vault |
| `/api/vaults/{id}` | DELETE | Admin | Delete vault |
| `/api/vaults/{id}/secrets` | POST | User | Create secret |
| `/api/vaults/{id}/secrets/{secretId}` | PUT | User | Update secret |
| `/api/vaults/{id}/secrets/{secretId}/rotate` | POST | User | Rotate secret value |
| `/api/vaults/{id}/secrets/{secretId}` | DELETE | User | Delete secret |
| `/api/vaults/{id}/secrets/{secretId}/versions` | GET | User | Secret version history |
| `/api/vaults/{id}/export-keys` | GET | User | Export secret key names |
| `/api/vaults/{id}/import` | POST | User | Bulk import secrets (max 200) |

## Key Classes

- `VaultsController` -- thin controller, RBAC-gated
- `IVaultService` / `VaultService` -- vault/secret lifecycle, encryption, rotation
- `IVaultRepository` / `VaultRepository` -- EF data access
- `SecretExpirationService` (in `Services/`, outside this module) -- background: alerts on expiring secrets

## Cross-Module Dependencies

- Depends on: Audit, EncryptionService (shared)
- Depended on by: Pipelines (secret injection at run time), Servers (vault listing)
