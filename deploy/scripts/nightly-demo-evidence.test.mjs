// SPDX-License-Identifier: EUPL-1.2
// nightly-demo-evidence.sh compares a Nightly with the previous successful one. Since nothing survives
// on the host between runs (2026-10-02), that baseline is the previous run's evidence bundle, restored
// by the pipeline into NIGHTLY_BASELINE_DIR. The script runs here for real on a fake workspace, with
// docker replaced by a recorder, so the comparison and its refusals are observed on the script itself.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { chmodSync, cpSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import test from "node:test";

const SHA = "a".repeat(40);

function write(path, content, executable = false) {
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, content);
  if (executable) chmodSync(path, 0o755);
}

function setUp({ backBytes = 1000, baseline = null } = {}) {
  const root = mkdtempSync(join(tmpdir(), "aetheus-evidence-"));
  const unix = path => path.replace(/\\/g, "/");
  const workspace = join(root, "ws");
  const state = join(workspace, ".qa-state");
  const bin = join(root, "bin");
  cpSync(resolve("deploy/scripts/nightly-demo-evidence.sh"), join(workspace, "deploy/scripts/nightly-demo-evidence.sh"), { recursive: true });
  write(join(workspace, "deploy/scripts/deploy-identity.sh"), "deploy_image_repos() { BACK_IMAGE_REPO=aetheus-back; FRONT_IMAGE_REPO=aetheus-front; }\n");
  write(join(workspace, "deploy/scripts/retain-docker-images.sh"), "exit 0\n");
  for (const [name, size] of [["aetheus-back", backBytes], ["aetheus-front", 1000], ["aetheus-browser-smoke", 1000], ["aetheus-vitrine", 1000]]) {
    write(join(workspace, ".pipeline-artifacts", `${name}.tar.gz`), "x".repeat(size));
  }
  mkdirSync(join(workspace, ".nightly-evidence"), { recursive: true });
  write(join(state, "live-color"), "green\n");
  write(join(state, "source-commit"), `${SHA}\n`);
  write(join(bin, "docker"), "#!/bin/sh\nexit 0\n", true);
  const baselineDir = join(workspace, ".nightly-baseline");
  if (baseline) write(join(baselineDir, ".nightly-evidence", "demo-metrics.txt"), baseline);

  const run = (environment = {}) => spawnSync("sh", [join(workspace, "deploy/scripts/nightly-demo-evidence.sh")], {
    encoding: "utf8",
    env: {
      ...process.env,
      PATH: `${bin}${process.platform === "win32" ? ";" : ":"}${process.env.PATH}`,
      WORKSPACE: unix(workspace),
      STATE_DIR: unix(state),
      NIGHTLY_BASELINE_DIR: unix(baselineDir),
      COMPOSE_PROJECT: "aetheus-nightly",
      APP_HOST: "qa.app.example.test",
      BUILD_SOURCEVERSION: SHA,
      NIGHTLY_STARTED_EPOCH: String(Math.floor(Date.now() / 1000)),
      AETHEUS_BROWSER_SMOKE_IMAGE: "aetheus-nightly-browser-smoke:x",
      ...environment
    }
  });
  return { run, cleanup: () => rmSync(root, { recursive: true, force: true }) };
}

const metrics = backBytes => `schema=2\nsource_sha=${"b".repeat(40)}\nduration_seconds=0\nback_bytes=${backBytes}\nfront_bytes=1000\nbrowser_bytes=1000\nvitrine_bytes=1000\n`;

test("a first Nightly has no baseline and says so", () => {
  const env = setUp();
  try {
    const result = env.run();
    assert.equal(result.status, 0, result.stderr);
    assert.match(result.stdout, /No previous successful Nightly evidence/);
  } finally { env.cleanup(); }
});

test("an artifact that more than doubled since the previous successful Nightly fails the run", () => {
  const env = setUp({ backBytes: 2500, baseline: metrics(1000) });
  try {
    const result = env.run();
    assert.equal(result.status, 1);
    assert.match(result.stdout, /Growth baseline: the previous successful Nightly/);
    assert.match(result.stderr, /back_bytes more than doubled/);
  } finally { env.cleanup(); }
});

test("a stable artifact passes against the restored baseline", () => {
  const env = setUp({ backBytes: 1000, baseline: metrics(1000) });
  try {
    const result = env.run();
    assert.equal(result.status, 0, result.stderr);
    assert.doesNotMatch(result.stderr, /WARNING/);
  } finally { env.cleanup(); }
});

test("a state directory on the host is refused", () => {
  const env = setUp();
  try {
    const result = env.run({ STATE_DIR: "/var/lib/aetheus-nightly" });
    assert.equal(result.status, 1);
    assert.match(result.stderr, /must live in the run workspace/);
  } finally { env.cleanup(); }
});
