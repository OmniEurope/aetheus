// SPDX-License-Identifier: EUPL-1.2
// record-storage-evidence.sh ends every successful QA: it refuses to record storage evidence while a QA
// stack of this run or an older one survives. A newer run's stack is another QA still running on the
// host (candidate 2505 failed on nightly QA 2515's live stack, 2026-10-02). The script runs here for
// real with docker and df replaced by stand-ins, so what it counts as residual is observed directly.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { chmodSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import test from "node:test";

const script = resolve("deploy/scripts/record-storage-evidence.sh");

function setUp(projects) {
  const root = mkdtempSync(join(tmpdir(), "aetheus-storage-"));
  const bin = join(root, "bin");
  const work = join(root, "work");
  mkdirSync(bin);
  mkdirSync(work);
  const containers = projects.map((project, index) => `c${index}\\t${project}-back\\t${project}`).join("\\n");
  const volumes = projects.map(project => `${project}-db-data\\t${project}`).join("\\n");
  writeFileSync(join(bin, "docker"), `#!/bin/sh
case "$1 $2" in
  "buildx du") printf 'Reclaimable:\\t1GB\\nTotal:\\t2GB\\n' ;;
  "ps -a") [ -z "${containers}" ] || printf '${containers}\\n' ;;
  "volume ls") [ -z "${volumes}" ] || printf '${volumes}\\n' ;;
esac
`);
  chmodSync(join(bin, "docker"), 0o755);
  const run = () => spawnSync("sh", [script, "qa", "2514"], {
    encoding: "utf8",
    env: {
      ...process.env,
      PATH: `${bin}${process.platform === "win32" ? ";" : ":"}${process.env.PATH}`,
      AETHEUS_BUILDX_BUILDER: "aetheus-test",
      AETHEUS_AGENT_WORK_DIRECTORY: work.replace(/\\/g, "/")
    }
  });
  return { run, cleanup: () => rmSync(root, { recursive: true, force: true }) };
}

test("a newer QA run still in progress is not a residual of this run", () => {
  const env = setUp(["aetheus-qa-rollback-2515", "aetheus-nightly"]);
  try {
    const result = env.run();
    assert.equal(result.status, 0, result.stderr);
    assert.match(result.stderr, /Not counted: 1 resource\(s\) of a newer QA run/);
    assert.match(result.stdout, /residualContainers=0 residualVolumes=0/);
  } finally { env.cleanup(); }
});

for (const project of ["aetheus-qa-rollback-2514", "aetheus-qa-2513"]) {
  test(`a surviving stack of this run or an older one (${project}) is refused`, () => {
    const env = setUp([project]);
    try {
      const result = env.run();
      assert.equal(result.status, 1);
      assert.match(result.stderr, /Exact residual QA Compose resources detected/);
      assert.match(result.stderr, new RegExp(project));
    } finally { env.cleanup(); }
  });
}
