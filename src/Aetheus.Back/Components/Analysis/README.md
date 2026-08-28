<!-- SPDX-License-Identifier: EUPL-1.2 -->
# Analysis

Security and quality analysis platform for pipeline scanner ingestion, normalized findings, metrics, SBOM components, policy gates, governance decisions, Dependency-Track synchronization, and portfolio reporting.

## API Surface

All routes are under `/api/analysis` and require an authenticated user unless a stricter policy is listed.

| Endpoint group | Auth | Description |
|---|---|---|
| `/runs/{runId}/reports`, `/runs/{runId}/gate` | `AgentToken`, assigned runner | Publish one bounded scanner report and read the aggregated run gate |
| `/runs/{runId}/result` | Pipeline Read | Read the aggregated gate result from the pipeline run UI |
| `/portfolio` | User + resource filtering | Paginated cross-project security and quality portfolio |
| `/projects/{projectId}/findings`, `/findings/{findingId}/*` | Project Read/Admin | Findings, occurrences, immutable governance decisions |
| `/projects/{projectId}/summary`, `/metrics`, `/components`, `/reports` | Project Read | Project dashboards, trends, architecture graphs, SBOM and report history |
| `/projects/{projectId}/tracking`, `/vulnerabilities` | Project Read | Dependency-Track state and continuous vulnerability observations |
| `/projects/{projectId}/policies`, `/exceptions` | Project Read/Admin | Project policy hierarchy and expiring exceptions |
| `/policies/global`, `/organizations/{organizationId}/policies` | Admin | Global and organization policy administration |

## Key Classes

- `AnalysisController` - resource-scoped API and runner-bound ingestion boundary.
- `IAnalysisService` / `AnalysisService` - quotas, idempotent normalization, immutable policy revisions, governance audit, and real-time notifications.
- `IAnalysisRepository` / `AnalysisRepository` - persistence facade over core, governance, and insight repositories.
- `AnalysisReportNormalizer` - SARIF, metrics, native scanner, CycloneDX, and SPDX normalization.
- `AnalysisPolicyEngine` - deterministic built-in and scoped gates with complete immutable evaluation snapshots.
- `AnalysisContinuousSyncService` - Dependency-Track synchronization.
- `AnalysisExpirationNotificationService` - governance expiry notifications.
- `AnalysisOperationalMonitorService` - scanner manifest, storage, and synchronization health alerts.

## Security and Retention

Scanner execution is allowlisted by the root `scanner-manifest.json`. DAST is limited to explicitly ephemeral Testing or Staging environments without real data. Retention values and quotas are validated from `Analysis:Runtime`; destructive database cleanup is never automatic and raw report artifacts remain governed by the existing artifact-retention workflow.

See [ADR-030](../../../../docs/adr/ADR-030-security-quality-analysis-platform.md), the [archived implementation plan](../../../../docs/plans/archive/2026-07-24-securite-qualite-360.md), and the [operations guide](../../../../docs/security/analysis-platform-operations.md).

## Cross-Module Dependencies

- Depends on: Pipelines, Projects, Environments, Audit, Notifications, Artifacts, Shared.
- Depended on by: Pipelines (`scanner` and `analysis-gate` steps), Front project-quality and portfolio views.
