<!-- SPDX-License-Identifier: EUPL-1.2 -->
# Git

External git connections (GitHub/GitLab), internal hosted repositories (GitLight), pull requests, branch policies, branch protection rules, smart HTTP clone/push, file browsing, blame, and commit graph.

## API Surface

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/api/git/connections` | GET | User | List connections for a project |
| `/api/git/connections/{id}` | GET | User | Connection detail |
| `/api/git/connections` | POST | User | Create connection |
| `/api/git/connections/{id}` | PUT | User | Update connection |
| `/api/git/connections/{id}` | DELETE | Admin | Delete connection |
| `/api/git/connections/{id}/pull-requests` | GET | User | List external PRs |
| `/api/git/connections/{id}/pull-requests/sync` | POST | User | Sync external PR data |
| `/api/git/connections/{id}/branch-policies` | GET | User | List branch policies |
| `/api/git/branch-policies` | POST | User | Create branch policy |
| `/api/git/branch-policies/{id}` | PUT | User | Update branch policy |
| `/api/git/branch-policies/{id}` | DELETE | Admin | Delete branch policy |
| `/api/git/status-report` | POST | User | Record pipeline status locally (returns `{ recorded, forwardedToProvider }`; provider forwarding not yet wired, so `forwardedToProvider` is always `false`) |
| `/api/git/repos` | GET | User | List internal repos; optional `?projectId=` filter (omitted = every repo across the projects the caller can read) |
| `/api/git/repos` | POST | User | Create internal repo |
| `/api/git/repos/{id}` | GET/PUT/DELETE | User/Admin | Read, update, or delete internal repo |
| `/api/git/repos/{id}/commits` | GET | User | Commit log (paginated) |
| `/api/git/repos/{id}/commits/{sha}` | GET | User | Single commit detail + file-level diff (against the first parent, or the empty tree for a root commit) |
| `/api/git/repos/{id}/commit-messages` | POST | User | Resolve 1 to 200 commit SHAs into their messages in one request |
| `/api/git/repos/{id}/branches` | GET/POST | User | List/create branches |
| `/api/git/repos/{id}/branches/{name}` | DELETE | User | Delete branch |
| `/api/git/repos/{id}/tags` | GET/POST | User | List/create tags |
| `/api/git/repos/{id}/tags/{name}` | DELETE | User | Delete tag |
| `/api/git/repos/{id}/tree` | GET | User | File tree browser |
| `/api/git/repos/{id}/blob` | GET | User | File content |
| `/api/git/repos/{id}/blob/raw` | GET | User | Raw file download |
| `/api/git/repos/{id}/blame` | GET | User | Git blame |
| `/api/git/repos/{id}/graph` | GET | User | Commit graph (text/ASCII lanes) |
| `/api/git/repos/{id}/graph-data` | GET | User | Structured commit graph for the SVG lane view |
| `/api/git/repos/{id}/pull-requests` | GET/POST | User | Internal PRs |
| `/api/git/repos/{id}/pull-requests/{n}` | GET | User | Detail d'une pull request interne |
| `/api/git/repos/{id}/pull-requests/{n}/merge` | POST | User | Merge PR |
| `/api/git/repos/{id}/pull-requests/{n}/close` | POST | User | Close PR |
| `/api/git/repos/{id}/pull-requests/{n}/diff` | GET | User | PR diff |
| `/api/git/repos/{id}/branch-protection` | GET/POST | User/Admin | List or create protection rules |
| `/api/git/repos/{id}/branch-protection/{ruleId}` | PUT/DELETE | User/Admin | Update or delete one protection rule |
| `/git/{projectId}/{slug}.git/*` | GET/POST | BasicAuth | Smart HTTP clone/push with account credentials, owner-bound PATs, or run-scoped clone tokens |

## Key Classes

- `GitController` -- external connections, PRs, branch policies, status reports
- `GitLightController` -- internal repo CRUD, commits, branches, tags, tree, blame, PRs
- `GitSmartHttpController` -- smart HTTP transport (info/refs, upload-pack, and receive-pack with a scoped 512 MiB request limit)
- `IGitService` / `GitService` -- external connection logic
- `IGitLightService` / `GitLightService` -- internal repo logic
- `IGitSmartHttpService` / `GitSmartHttpService` -- smart HTTP protocol + streamed extraction of updated branch refs + `.pipeline/` YAML auto-sync before webhook branch-filter evaluation + real-time pipeline change notification after each synced create/update + HEAD auto-fix after push (detects default branch, updates HEAD on main/master mismatch)
- `GitUnifiedDiffParser` -- pure parser turning a `git diff --patch` unified diff into the structured `PullRequestDiffDto` (per-file hunks + add/del counts); also exposes the SHA validation and empty-tree-SHA constant used by the commit-detail endpoint
- `IGitLightCliService` / `GitLightCliService` -- shells out to `git` binary via the `GitProcessRunner` collaborator; write operations (commit/push) delegated to `GitLightCliWriter`; `CommitFileAsync` commits a file to a bare repo via a throwaway temp clone + push (used by git-strict project pipelines, ADR-015)
- `IGitRepository` / `GitRepository` -- external connection EF access
- `IGitLightRepository` / `GitLightRepository` -- internal repo EF access
- `GitBasicAuthenticationHandler` -- HTTP basic auth for smart HTTP via account credentials, owner-bound personal access tokens, or project-scoped pipeline-run clone tokens. `PatScopeEnforcementMiddleware` explicitly permits POST `git-upload-pack` for read-only PAT clone/fetch while denying `git-receive-pack` and unrelated writes.
- `GitLightMaintenanceService` (in `Services/`, outside this module) -- background: `git gc`, housekeeping
- `GitRepoPathResolver` -- single source of truth for resolving an internal repo's on-disk path from `(projectId, slug)` with the path-traversal guard (OS-aware comparator: case-insensitive on Windows, case-sensitive on Linux). Replaces the three drifted copies in `GitLightService` / `GitSmartHttpService` / `ExternalRepoMirrorService` (M-git-6)

## Cross-Module Dependencies

- Depends on: Audit, Pipelines (status reporting), Auth (basic auth handler), PersonalAccessTokens, Webhooks
- Depended on by: ExternalRepos (mirror-as-internal-repo at the standard path; feature-flagged `Features:ExternalRepos`, ADR-018/020)
