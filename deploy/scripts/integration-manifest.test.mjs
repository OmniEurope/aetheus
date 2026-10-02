// SPDX-License-Identifier: EUPL-1.2
// The integration manifest is sealed into the delivery contract, so its exact shape is part of what
// the contract's digest attests. These pin the shape and the two inputs that must be refused rather
// than written through: a revision that is not a full commit, and a declaration with no packages.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

import { buildIntegrationManifest } from "./write-integration-manifest.mjs";

const directory = fileURLToPath(new URL(".", import.meta.url));
const script = join(directory, "write-integration-manifest.mjs");
const commit = "a".repeat(40);

test("the manifest carries schema 1, the source commit and the declared packages", () => {
  const manifest = buildIntegrationManifest(
    commit,
    JSON.stringify({ packages: [{ id: "Aetheus.Telemetry", version: "1.0.0" }] }));

  assert.deepEqual(manifest, {
    schema: 1,
    sourceCommit: commit,
    packages: [{ id: "Aetheus.Telemetry", version: "1.0.0" }]
  });
});

test("a revision that is not a full commit is refused", () => {
  assert.throws(
    () => buildIntegrationManifest("abc", JSON.stringify({ packages: [] })),
    /40-character revision/);
});

test("a declaration without a packages array is refused", () => {
  assert.throws(
    () => buildIntegrationManifest(commit, JSON.stringify({ other: true })),
    /packages/);
});

test("run as a program it writes one JSON line to the named output", () => {
  const root = mkdtempSync(join(tmpdir(), "aetheus-manifest-"));
  try {
    const declaration = join(root, "declaration.json");
    const output = join(root, "integration-manifest.json");
    writeFileSync(declaration, JSON.stringify({ packages: [{ id: "Aetheus.WebAnalytics" }] }));

    const result = spawnSync(process.execPath, [script, commit, output, declaration], { encoding: "utf8" });

    assert.equal(result.status, 0, result.stderr);
    const written = readFileSync(output, "utf8");
    assert.ok(written.endsWith("\n"), "the manifest file must end with a newline");
    assert.deepEqual(JSON.parse(written), {
      schema: 1,
      sourceCommit: commit,
      packages: [{ id: "Aetheus.WebAnalytics" }]
    });
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("run with missing arguments it fails rather than writing a partial manifest", () => {
  const result = spawnSync(process.execPath, [script, commit], { encoding: "utf8" });

  assert.equal(result.status, 2);
  assert.match(result.stderr, /Usage:/);
});
