# Security Policy

## Supported Versions

Support is not tied to a version number: security patches target the active development line
(`develop`) and the latest deployed release. Older releases are not maintained.

## Reporting a Vulnerability

**Do not open a public issue for security vulnerabilities.**

Send an email to **dacer08@gmail.com** with:

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

Also out of scope as a finding: the public demo authenticates with a deliberately known, displayed
password. The demo is public, disposable, seeded with synthetic data and rebuilt by the nightly
pipeline; its credential is an entry ticket, not a secret. Report instead anything that would let the
demo reach beyond its own `aetheus-demo-*` boundary (its ports, volumes, state files and hostname).

## Fixtures négatives intentionnelles

Le projet de conformité opt-in `Toto Vulnerable` matérialise volontairement des canaris synthétiques
inertes et du code dangereux déterministe dans son dépôt Git interne isolé. Ces fixtures servent
uniquement à exercer les vraies analyses qualité et sécurité ; elles ne sont jamais des identifiants
valides. La campagne négative orchestre uniquement `toto-ci`, `toto-quality` et `toto-security`. Les
notes dégradées restent non bloquantes dans ce
laboratoire attendu rouge, qui ne possède aucune pipeline QA, candidate, release, déploiement ou
promotion.

Il faut en revanche signaler tout vrai identifiant, toute exposition des fixtures hors du laboratoire
isolé, tout ajout d'une capacité de déploiement, tout contournement de scanner ou tout résultat qui
rendrait verte la campagne attendue rouge. Ne publiez pas le contenu des canaris dans une issue publique.

## Security Design

- JWT authentication with refresh-token renewal.
- RBAC via `ResourceAuthorizationService` with org-scoped ownership.
- Secrets encrypted at rest (AES-256-GCM); never logged.
- Agent: non-root by default, zero-elevation baseline, opt-in capabilities.
- Known trust boundary: an agent installed with `--module deployment` can reach the production
  database on its host - the expand/contract migration gate reads `__EFMigrationsHistory` through a
  fixed `psql` argv, and compose receives the database credentials from the host-held env file. This
  is inherent to deploying on that host. Name the containment precisely, because the two mechanisms
  are different and only one gates the database:
  - the database path runs `docker exec` against the compose container, so it is gated by
    **docker-group membership** - a separate `--enable-docker` opt-in, off by default, which the
    installer itself flags as effectively equivalent to root on that host;
  - the sudoers grant is narrower and unrelated to the database: `Cmnd_Alias AETHEUS_DEPLOY` names
    the single `deploy-restart` helper, so it can restart a service and nothing else.

  Both, plus the module being opt-in per server, are what keep the path bounded. Treat any widening
  of either as report-worthy - in particular any command added to `AETHEUS_DEPLOY`.
- Kestrel request body capped at 10 MB.
- XXE-safe XML parsing (`SafeXml` helper).
- All DTO text properties carry `[Required]` + `[StringLength]` validation.
- Pagination clamped server-side (`Math.Clamp(pageSize, 1, 200)`).

## Acknowledgements

We appreciate responsible disclosure and will credit reporters (with permission)
in release notes.
