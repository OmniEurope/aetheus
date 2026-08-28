<!-- SPDX-License-Identifier: EUPL-1.2 -->
# Third-party license inventory

This directory records license material required for components redistributed with Aetheus.

Scanner containers, downloaded scanner binaries, project-owned linters and the external Dependency-Track service are not redistributed by this repository. Their exact versions, license expressions, integration modes and authoritative license links are recorded in `docs/security/scanner-license-matrix.md` and `scanner-manifest.json`.

The NuGet inventory is generated from `Directory.Packages.props`; package license files remain part of the restored packages and published software notices. `THIRD_PARTY_NOTICES.md` is the human-readable distribution notice.

Before a release, review the redistributed-dependency inventory and require a recognized SPDX expression
or an explicit reviewed exception for every entry. This review is currently manual; no fail-closed
dependency-license guard runs in CI.
