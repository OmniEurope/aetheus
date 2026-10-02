// SPDX-License-Identifier: EUPL-1.2
// Writes the integration manifest the delivery contract seals: the source commit, and the optional
// observability packages declared by the example consumer.
//
// It was a heredoc inside .pipeline/aetheus-ci.yaml, where nothing could run it, lint it or test it.
// Usage: node write-integration-manifest.mjs <commit> <output> [declaration]
import { readFileSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

const DEFAULT_DECLARATION = "examples/optional-observability/aetheus.integrations.json";

/// Builds the manifest object. Separate from the file IO so a test can assert its exact shape,
/// which is what the delivery contract's digest is taken over.
export function buildIntegrationManifest(commit, declarationJson) {
  if (typeof commit !== "string" || !/^[0-9a-fA-F]{40}$/.test(commit))
    throw new Error("The source commit must be a full 40-character revision.");

  const declaration = JSON.parse(declarationJson);
  if (!Array.isArray(declaration.packages))
    throw new Error("The integration declaration must carry a `packages` array.");

  return { schema: 1, sourceCommit: commit, packages: declaration.packages };
}

// Only when run as a program, so `node --test` can import the builder without writing anything.
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const [commit, output, declaration = DEFAULT_DECLARATION] = process.argv.slice(2);
  if (!commit || !output) {
    console.error("Usage: node write-integration-manifest.mjs <commit> <output> [declaration]");
    process.exit(2);
  }
  const manifest = buildIntegrationManifest(commit, readFileSync(declaration, "utf8"));
  writeFileSync(output, `${JSON.stringify(manifest)}\n`);
}
