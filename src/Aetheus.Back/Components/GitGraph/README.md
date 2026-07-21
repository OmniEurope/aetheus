<!-- SPDX-License-Identifier: EUPL-1.2 -->
# GitGraph

Git provenance cross-linking: first-class `GitCommit` / `GitBranch` entities that releases, artifacts and commits/branches link to many-to-many. Surfaces the internal `/git/commits/{id}` and `/git/branches/{id}` detail pages and records provenance continuously as runs are triggered and releases created (no longer only via the one-shot migration backfill).

## API Surface

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/api/gitgraph/commits/{id}` | GET | User (Project Read) | Commit detail + cross-linked releases/artifacts/branches |
| `/api/gitgraph/branches/{id}` | GET | User (Project Read) | Branch detail + cross-linked releases/artifacts/commits |
| `/api/gitgraph/project/{projectId}` | GET | User (Project Read) | Project commit→release→artifact graph (`ProjectGitGraphDto`, S-FEAT-29): recent commits with their cross-links, for the project git-graph timeline view |

`commits/{id}` and `branches/{id}` return `404` uniformly on missing **or** no-access (never `403`) to avoid leaking existence, since the permission check needs the loaded entity's `ProjectId`. `project/{projectId}` knows the project up-front, so it gates before the lookup (also `404` on no-access).

## Key Classes

- `GitGraphController` -- thin controller, gates by `ResourceType.Project` Read permission
- `IGitGraphService` / `GitGraphService` -- maps entities + cross-links to DTOs
- `IGitGraphRepository` / `GitGraphRepository` -- EF reads (detail with includes) + get-or-create + lightweight read-only resolution
- `IGitGraphRecorder` / `GitGraphRecorder` -- get-or-create `GitCommit`/`GitBranch` by `(ProjectId, Sha/Name)` and link them to runs/releases; **best-effort** (a recording failure never breaks the run-trigger / release-create flow)
- `GitGraphMapper` -- shared entity→link-DTO mapper used by Releases/Artifacts/GitGraph so every page renders identical cross-link tiles

## Cross-Module Dependencies

- Depends on: Shared (`ResourceAuthorizationService`, link DTOs)
- Depended on by: Releases (`ReleaseService` records release git context), Pipelines (`PipelineRunService` records run git context + resolves run-detail links), Artifacts (`ArtifactService` maps cross-links)
