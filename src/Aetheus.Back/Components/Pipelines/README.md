<!-- SPDX-License-Identifier: EUPL-1.2 -->
# Pipelines

YAML-defined CI/CD pipelines: definition, validation, runs, stages, jobs, steps, scheduling, approvals, artifacts, test results, templates, webhooks, and dry-run/preflight checks. Supports both legacy `stages > steps` format and Azure DevOps-style `stages > jobs > steps` format (flattened via `YamlParsingHelper.FlattenJobs()`).

Stages and jobs accept an `os:` key (`linux` / `windows`, inherited stage → job) that pins resolution to a runner whose `Server.OsType` matches (`OsType` enum + `OsTypeHelper` in `Aetheus.Shared`); an OS-constrained stage with no compatible online runner fails the run with a clear reason.

Run, stages, and jobs also accept an `isolation:` block (`PipelineIsolationDefinition`: `mode` = `process` (default) / `container`, plus `image` / `runtime` / `network`, inherited run → stage → job). `mode: container` runs each step inside an ephemeral, hardened container on the agent (`ContainerRunExecutor`, `docker run --rm --cap-drop ALL --security-opt no-new-privileges --pids-limit`; pluggable runtime `runc`/`runsc`/`kata`). Container-isolated stages resolve **only** to `Server.DockerAvailable` runners and **fail-closed** (never downgraded to process mode) when none is available, raising a Sec-Audit alert. See ADR-017.

**Step types.** A step's `type:` picks its dispatch path (default is a free-form `shell` step): `substitute` (`#{VAR}#` token replacement in `target_files`), `release` (changelog + `Release` creation), `coverage` (publish Cobertura XML), `lint` (S-TECH-59 - publish a SARIF 2.1.0 report into a structured `LintResult`), `complexity` (Phase 4 L - Roslyn cyclomatic-complexity + LOC + CRAP over the C# workspace, published as `RunMetric` rows), `deploy` (cross-agent deployment, `OperationKind.PipelineDeploy` to a deployment-capable agent), `apache-proxy` (render a reverse-proxy vhost and apply it on a host Apache via `OperationKind.ApacheConfigureProxy` (307); requires the agent's `--enable-apache-manage`), `certbot` (obtain/install a real ACME certificate through an Apache webroot via `OperationKind.CertbotObtain` (320), failing honestly when validation fails; requires the agent's `--enable-certbot-manage`), `trigger` (launch another pipeline in the same project and wait for it - see below), and `restore-artifacts`. The latter accepts either a successful child artifact (`artifact` + `artifact_source_pipeline`) or a retained same-project `release`; `latest-published` ignores drafts/failed releases, while `previous-deployed` selects the prior factually deployed release, and `target_directory` safely restores beside the current payload. A restored V-1 payload must carry a valid rollback contract and source commit; otherwise the run fails rather than entering bootstrap mode. Coverage is **also auto-detected** during artifact collection (S-TECH-61): the agent best-effort publishes a Cobertura report found among collected files, so an explicit `type: coverage` step is optional. See ADR-012 for the full YAML model, including job-level `depends_on` (intra-stage DAG), `strategy.max_parallel`, and the multi-agent `os` matrix axis.

**Pipeline-as-orchestrator (`type: trigger`).** A `trigger` step launches another pipeline in the same project and **waits** for it to finish before the parent stage advances. The dispatch half (`PipelineRunService.CreateTriggerStepAsync`) links the child run to the parent step via `PipelineStepRun.TriggeredRunId`; the wait half (`PipelineRunCompletedTriggerHandler`, an `IDomainEventHandler<PipelineRunCompletedEvent>`) mirrors the child's terminal status onto the step (child Success ⇒ step Success; anything else ⇒ Failed, honouring `continue_on_error`) and re-enters `AdvanceStageAsync`. Cancelling an active orchestration run walks those persisted links and cancels every active descendant recursively, including pending runs and grandchildren. This lets one pipeline orchestrate others (e.g. `toto-orchestration-accept` triggering `toto-ci` then `toto-qa` then `toto-accept-deploy`, each independent and run exactly once). A **pure orchestration** pipeline - every step is `type: trigger` - needs no workspace, so `PipelineRunService.RequiresWorkspace` skips injecting `System:Prepare`/`System:Cleanup` for it (no "Clone Repository"/"Cleanup" rows); the child run view renders each trigger step as a collapsible node that lazy-loads the child's own timeline (front `RunTimelineTree`). A background self-heal (`PipelineTriggerReconcileService`) recovers a trigger step whose child completion event was lost, re-drives a run that has pending steps but no in-flight work after `SchedulerRecoveryGrace`, and force-fails a terminal dead end past `RunStuckTimeout` (see `BackgroundServicesOptions`).

**Git-strict project pipelines (ADR-015).** For a project-owned pipeline whose project has an Aetheus-hosted repo or an enabled external-repository mirror, the authoritative YAML lives in `.pipeline/<name>.yaml` on the pipeline's optional `SourceBranch` (or the repository default branch) and wins at run time; the DB column is a read-through mirror. Webhook filtering reads the authoritative Git YAML immediately before matching the accepted pushed ref and SHA. UI edits against an external mirror currently create local mirror commits only; write-back to the external remote is not supported. With the feature disabled or no mirror present, the definition remains DB-backed.

**Dependency view.** `GET /api/pipelines/dependencies` returns the caller-visible pipelines split into parents and leaves. `PipelineDependencyGraphBuilder` derives a parent's references from `on_success` entries and `type: trigger` steps; the global pipelines page renders both groups as navigable grids.

## Blue-Green Deployment Safety

Blue-green pipelines that use Docker Compose must distinguish **colour-scoped services** from
**shared services** such as PostgreSQL, persistent storage, or a message broker.

- A Compose service without `profiles:` is enabled for every profile. Consequently,
  `docker compose --profile <old-colour> stop` without explicit service names also stops shared,
  unprofiled services. Never use an unscoped `stop`, `down`, `restart`, or `rm` in a blue-green
  pipeline.
- Stop only the old colour's named services, for example:

  ```sh
  docker compose --profile "$LIVE" stop "back-$LIVE" "front-$LIVE"
  ```

- Use `/health/live` only to answer "is the process running?". A deployment gate must use
  `/health/ready`, which includes required dependencies such as PostgreSQL. Liveness alone can
  remain green while every DB-backed request fails.
- The safe order is: start the idle colour and shared dependencies, wait for backend readiness,
  verify the idle frontend, switch the reverse-proxy upstream, record the live colour, run the
  public smoke while the old colour is still available, then stop only the two old-colour
  application services. A failed public smoke must restore the old upstream and live-colour file.
- A fast pipeline may skip functional CI/QA, but it must never skip infrastructure readiness or
  weaken service targeting. Schema changes still require the full release path and expand/contract
  compatibility.
- Keep the loopback bindings and migration ownership contract in
  `deploy/compose/remote-bluegreen.compose.yml`; architecture guards verify both.

## API Surface

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/api/pipelines` | GET | User | List pipelines (paginated) |
| `/api/pipelines/dependencies` | GET | User | Resource-filtered parent/leaf dependency view |
| `/api/pipelines/dependencies/page` | GET | User | Paginated resource-filtered dependency graph |
| `/api/pipelines/{id}` | GET | User | Pipeline detail |
| `/api/pipelines/{id}/source` | GET | User | Effective Git repository, branch, path and commit |
| `/api/pipelines` | POST | User | Create pipeline (YAML validated) |
| `/api/pipelines/{id}` | PUT | User | Update pipeline |
| `/api/pipelines/{id}` | DELETE | Admin | Delete pipeline |
| `/api/pipelines/{id}/parameters` | GET | User | Parametres declaratifs disponibles au lancement |
| `/api/pipelines/{id}/run` | POST | User+ServerAdmin | Trigger a run |
| `/api/pipelines/{id}/preflight` | POST | User+ServerAdmin | Pre-flight target resolution |
| `/api/pipelines/{id}/dry-run` | POST | User+ServerAdmin | Dry-run with variable resolution |
| `/api/pipelines/{id}/runs` | GET | User | List runs (paginated) |
| `/api/pipelines/runs/{runId}` | GET | User | Run detail |
| `/api/pipelines/runs/{runId}/cancel` | POST | User | Cancel a run and its active triggered descendants recursively |
| `/api/pipelines/runs/{runId}/retry-failed` | POST | User+ServerAdmin | Retry failed steps |
| `/api/pipelines/runs/{runId}/rerun` | POST | User+ServerAdmin | Relancer depuis la definition courante ou le snapshot source |
| `/api/pipelines/runs/{runId}/artifacts` | GET | User | List artifacts |
| `/api/pipelines/runs/{runId}/artifacts` | POST | AgentToken | Publish artifact |
| `/api/pipelines/runs/{runId}/approvals` | GET | User | List approvals |
| `/api/pipelines/approvals/{id}/decide` | POST | User | Approve/reject |
| `/api/pipelines/runs/{runId}/test-results` | GET | User | Test results |
| `/api/pipelines/runs/{runId}/test-results` | POST | AgentToken | Publish test results |
| `/api/pipelines/runs/{runId}/coverage` | GET | User | Coverage summary |
| `/api/pipelines/runs/{runId}/coverage` | POST | AgentToken | Publish Cobertura coverage |
| `/api/pipelines/runs/{runId}/coverage-trend` | GET | User | Coverage trend across recent runs |
| `/api/pipelines/projects/{projectId}/quality-trend` | GET | User | Tendance qualite agregee du projet |
| `/api/pipelines/runs/{runId}/metrics-trend` | GET | User | Tendance des metriques de complexite du run |
| `/api/pipelines/runs/{runId}/complexity` | POST | AgentToken | Publish Roslyn complexity/LOC/CRAP metrics |
| `/api/pipelines/runs/{runId}/lint` | GET | User | Lint (SARIF) summary |
| `/api/pipelines/runs/{runId}/lint` | POST | AgentToken | Publish SARIF lint report |
| `/api/pipelines/validate` | POST | User | Validate YAML |
| `/api/pipelines/webhook` | POST | Anonymous | Webhook trigger (rate-limited) |
| `/api/pipelines/templates` | GET | User | List templates |
| `/api/pipelines/templates/{id}` | GET | User | Template detail |
| `/api/pipelines/templates` | POST | Admin | Create template |
| `/api/pipelines/templates/{id}` | PUT | Admin | Update template |
| `/api/pipelines/templates/{id}` | DELETE | Admin | Delete template |
| `/api/pipelines/templates/{id}/export` | GET | User | Export template as YAML |
| `/api/pipelines/templates/import` | POST | Admin | Import template from file |
| `/api/pipelines/templates/{id}/resolve` | POST | User | Resolve template parameters |

## Key Classes

- `PipelinesController` -- run/artifact/approval/webhook endpoints
- `PipelineTemplatesController` -- template CRUD + import/export
- `IPipelineService` / `PipelineService` -- pipeline CRUD, YAML validation
- `IPipelineRunService` / `PipelineRunService` -- run orchestration, dry-run, preflight, task dispatch (pure helpers extracted to the static `PipelineRunHelpers`)
- `PipelineCommandBuilder` -- static helper: OS-specific shell command construction for pipeline steps and system tasks
- `IPipelineVariableResolver` / `PipelineVariableResolver` -- resolves the full variable set (system, inline YAML, libraries, vaults, cross-access) with warnings and secret-key tracking
- `IPipelineGitService` / `PipelineGitService` -- git-strict storage for project-owned pipeline definitions (read/write `.pipeline/*.yaml`, env→project copy; internal repos only)
- `IPipelineApprovalService` / `PipelineApprovalService` -- gate approvals
- `IPipelineArtifactService` / `PipelineArtifactService` -- artifact + test-result + coverage storage
- `PipelineArtifactRepository` -- focused artifact persistence collaborator used by `PipelineCoreRepository`
- `CoverageResultParser` -- Cobertura XML parsing into `CoverageResult` entities
- `IPipelineTemplateService` / `PipelineTemplateService` -- template management
- `IPipelineWebhookService` / `PipelineWebhookService` -- webhook signature verification
- `IPipelineRepository` / `PipelineRepository` -- facade composing `PipelineCoreRepository` (core data access, with artifact persistence delegated to `PipelineArtifactRepository`), `PipelineServerResolver` (server resolution, org-fallback, effective-project-id), `PipelineCoverageRepository` (coverage queries), `PipelineLifecycleRepository` (retention/lifecycle), and `PipelineDependencyGraphRepository` (dependency graph queries)
- `PipelineSchedulerService` -- background: cron-triggered runs
- `PipelineRetentionService` -- background: old-run cleanup
- `PipelineTriggerReconcileService` -- background trigger reconciliation; scheduler, retention and reconcile use `PostgresLeaderLease` so only one blue-green replica executes each workload

## Cross-Module Dependencies

- Depends on: Audit, Vaults (secret injection), VariableLibraries, Settings (webhook config), Shared
- Depended on by: Tasks, Git, Releases, Artifacts (storage/retention)
