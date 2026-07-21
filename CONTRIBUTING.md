# Contributing to Aetheus

Thank you for your interest in contributing! This document explains how to get
started and what standards to follow.

## Getting Started

1. Review the architecture and coding summary in this document.
2. Set up the development environment:
   - .NET SDK 10.0.202 exactly (pinned by `global.json`)
   - Docker (for the local PostgreSQL container)
   - Run `ylaunch.ps1 -s` (Windows) or `ybaunch.sh -s` (Linux) to start the
     backend + database.

## Branch Conventions

| Branch          | Purpose                          |
|-----------------|----------------------------------|
| `main`          | Production-ready releases        |
| `develop`       | Active development               |
| `feature/*`     | Feature branches off `develop`   |
| `fix/*`         | Bug-fix branches                 |

## Before Submitting

Run through this checklist:

- [ ] `dotnet build -c Release --warnaserror` -- zero errors, zero warnings.
- [ ] `dotnet test --filter "Category!=E2E"` -- all unit + integration tests green.
- [ ] Architecture Guard Tests pass (migration drift, file size, controller auth).
- [ ] New `.cs` / `.razor` files have the SPDX header:
      `// SPDX-License-Identifier: EUPL-1.2`
- [ ] User-facing strings use `IStringLocalizer<AppStrings>` (EN + FR resx).
- [ ] No `DateTime.UtcNow` / `DateTime.Now` in backend production code -- inject
      `TimeProvider`.
- [ ] No inline `@code` in `.razor` files -- use code-behind `.razor.cs`.
- [ ] DTO text properties carry `[Required]` + `[StringLength]`.
- [ ] Data controllers have `[Authorize]` at class level.

## Coding Standards (Summary)

- **One class per file** (exception: related DTOs may share a file).
- **Code-behind** for Blazor: `.razor` + `.razor.cs`.
- **Least exposure**: `private` > `internal` > `protected` > `public`.
- **Async**: methods end with `Async`, propagate `CancellationToken`,
  `ConfigureAwait(false)` in services/repos.
- **EF Core**: `AsNoTracking()` on reads, `WHERE` before `Include`, no raw SQL.
- **CSS in rem**, responsive, dark-mode aware. No `.razor.css` / inline styles.

Architecture guard tests under `tests/` are the executable source of truth for
additional repository conventions.

## Commit Messages

Follow [Conventional Commits](https://www.conventionalcommits.org/):

```
feat(module): short description
fix(module): what was broken and how it's fixed
test(module): what coverage was added
docs(module): what was documented
refactor(module): what changed structurally
chore(module): maintenance task
```

## Pre-commit Hooks

This project uses [Husky.NET](https://alirezanet.github.io/Husky.Net/) to run
checks before each commit. After cloning, restore the tooling and install the
hooks:

```bash
dotnet tool restore
dotnet husky install
```

The pre-commit hook (`dotnet husky run --group pre-commit`) runs three tasks:

1. **Build** -- `dotnet build -c Release --warnaserror` (zero errors, zero warnings).
2. **SPDX check** -- verifies every `.cs` / `.razor` file under `src/` and
   `tests/` contains the `SPDX-License-Identifier: EUPL-1.2` header (ignores
   `obj/`, `bin/`, and `Migrations/`).
3. **Credential scan** -- rejects commits that contain potential secrets
   (passwords, keys, tokens) in `appsettings.json` / `appsettings.Development.json`.

If any task fails, the commit is blocked. Fix the issue and retry.

## Security

If you discover a vulnerability, **do not open a public issue**. Follow the
process in [`SECURITY.md`](SECURITY.md).

## License

By contributing, you agree that your contributions will be licensed under the
[EUPL-1.2](LICENSE).
