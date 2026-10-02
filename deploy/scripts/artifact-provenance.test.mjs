// SPDX-License-Identifier: EUPL-1.2
import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { spawnSync } from "node:child_process";
import test from "node:test";

const script = resolve("deploy/scripts/generate-artifact-provenance.mjs");

// The fixture named `aetheus-back.tar` while the pipeline had long since moved to `.tar.gz`, so the
// generator matched nothing and the docker-images category came back empty. The test failed for an
// unknown number of commits because it was in the repository and listed in no pipeline (PLAN-006
// lot 7); it is registered in CI's script-test list now, which is what keeps this from recurring.
test("produces deterministic category sizes without file contents", () => {
  const workspace = mkdtempSync(join(tmpdir(), "aetheus-provenance-"));
  mkdirSync(join(workspace, ".pipeline-artifacts"), { recursive: true });
  mkdirSync(join(workspace, ".pipeline-artifacts", "agent-release"), { recursive: true });
  writeFileSync(join(workspace, ".pipeline-artifacts", "aetheus-back.tar.gz"), "secret-payload");
  writeFileSync(join(workspace, ".pipeline-artifacts", "source-commit"), "a".repeat(40));
  writeFileSync(join(workspace, ".pipeline-artifacts", "agent-release", "agent-release-manifest.json"),
    JSON.stringify({ archives: [{ fileName: "agent.tar.gz", sizeBytes: 123, sha256: "b".repeat(64) }] }));
  const output = join(workspace, ".pipeline-artifacts", "artifact-provenance.json");

  const result = spawnSync(process.execPath, [script, output, "a".repeat(40)], {
    cwd: workspace,
    encoding: "utf8"
  });

  assert.equal(result.status, 0, result.stderr);
  const manifest = JSON.parse(readFileSync(output, "utf8"));
  assert.equal(manifest.sourceCommit, "a".repeat(40));
  assert.equal(manifest.categories.dockerImages.fileCount, 1);
  assert.equal(manifest.categories.dockerImages.totalBytes, 14);
  assert.deepEqual(manifest.categories.agentArchives.declaredArtifacts, [{
    fileName: "agent.tar.gz", sizeBytes: 123, sha256: "b".repeat(64)
  }]);
  assert.doesNotMatch(JSON.stringify(manifest), /secret-payload/);
});
