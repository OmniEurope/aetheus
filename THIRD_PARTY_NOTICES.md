# Third-party notices

This repository uses third-party packages under their respective licenses. Package versions are centrally declared in `Directory.Packages.props`. CI enforces vulnerability scans and project SPDX headers; the redistributed-dependency license inventory is reviewed manually and is not currently protected by a fail-closed CI guard.

## NetArchTest.Rules

- Package: `NetArchTest.Rules` 1.3.2
- Purpose: architecture assertions in the test projects
- License: MIT
- Upstream: `BenMorris/NetArchTest`
- Compliance note: the NuGet package metadata does not expose a license expression, so this local inventory records the upstream repository license explicitly.

The upstream copyright and MIT license text apply to NetArchTest.Rules. No project code is relicensed by this notice.

## External analysis tools

Aetheus can orchestrate external security and quality tools. They are not copied into this repository or bundled in Aetheus images. Their versions, license expressions, immutable provenance and integration modes are listed in [`docs/security/scanner-license-matrix.md`](docs/security/scanner-license-matrix.md) and [`scanner-manifest.json`](scanner-manifest.json).

The current external tool set includes OpenGrep (LGPL-2.1-only), Gitleaks (MIT), Trivy (Apache-2.0), Syft (Apache-2.0), OWASP ZAP (Apache-2.0), Dependency-Track (Apache-2.0), ESLint (MIT), Istanbul/nyc (ISC), Ruff (MIT), coverage.py (Apache-2.0), PMD (BSD-3-Clause) and jscpd (MIT). Their upstream notices apply to their respective artifacts.

## Aetheus analysis rules

The OpenGrep rules authored in this repository are part of Aetheus and remain licensed under EUPL-1.2. They are not relicensed under the scanner's license.

## OpenTelemetry protocol definitions

- Component: `open-telemetry/opentelemetry-proto` v1.10.0
- Purpose: vendored OTLP metrics, logs, traces, resource, common, and collector `.proto` definitions
- License: Apache-2.0
- Upstream: `open-telemetry/opentelemetry-proto`
- Source: <https://github.com/open-telemetry/opentelemetry-proto/tree/v1.10.0>
- License: <https://github.com/open-telemetry/opentelemetry-proto/blob/v1.10.0/LICENSE>

The included protocol files retain their upstream copyright and Apache-2.0 license headers. No upstream OpenTelemetry code is relicensed by this notice.
