// SPDX-License-Identifier: EUPL-1.2
// bluegreen-host-reset.sh deletes containers, volumes and deployment state, once at the end of every
// nightly and once a day before the demo is redeployed (recette R-523). It is run here for real, with
// docker replaced by a stand-in that lists a project's objects next to production's and records
// every call, so what it would delete, and what it refuses to touch, is observed on the script itself.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import test from "node:test";

const script = resolve("deploy/scripts/bluegreen-host-reset.sh");

// The stand-in answers like a host holding the demo next to production: one demo container and
// volume, one production container and volume. A container answers to its label query until removed.
function setUp({ containerName = "aetheus-demo-blue-back" } = {}) {
  const root = mkdtempSync(join(tmpdir(), "aetheus-reset-"));
  const bin = join(root, "bin");
  mkdirSync(bin);
  const log = join(root, "docker.log").replace(/\\/g, "/");
  writeFileSync(join(bin, "docker"), `#!/bin/sh
echo "$*" >> "${log}"
case "$1 $2" in
  "ps -aq") case "$*" in *project=aetheus-demo*) [ -f "${log}.removed" ] || echo c1 ;; esac ;;
  "ps -a") echo "c1 ${containerName}"; echo "c9 aetheus-prod-blue-back" ;;
  "inspect --format") echo "/${containerName}" ;;
  "rm -f") touch "${log}.removed" ;;
  "volume ls") case "$*" in *label*) ;; *) echo aetheus-demo-db-data; echo aetheus-prod-db-data ;; esac ;;
  "image ls") echo img-smoke ;;
esac
exit 0
`);
  chmodSync(join(bin, "docker"), 0o755);
  const run = (environment = {}) => spawnSync("sh", [script], {
    encoding: "utf8",
    env: {
      ...process.env,
      PATH: `${bin}${process.platform === "win32" ? ";" : ":"}${process.env.PATH}`,
      WORKSPACE: "/w/run/s",
      STATE_DIR: "/w/run/s/.demo-state",
      ENV_FILE: "/w/run/s/.demo-state/.env",
      SECRETS_FILE: "/w/run/s/.demo-state/.secrets",
      COMPOSE_PROJECT: "aetheus-demo",
      REMOVE_BROWSER_SMOKE_IMAGES: "",
      ...environment
    }
  });
  const calls = () => (existsSync(log) ? readFileSync(log, "utf8").trim().split("\n").filter(Boolean) : []);
  return { run, calls, cleanup: () => rmSync(root, { recursive: true, force: true }) };
}

test("the reset removes the project's containers and volumes and nothing of production", () => {
  const env = setUp();
  try {
    const result = env.run();
    assert.equal(result.status, 0, result.stderr);
    assert.ok(env.calls().includes("rm -f c1"));
    assert.ok(env.calls().includes("volume rm aetheus-demo-db-data"));
    assert.ok(!env.calls().some(call => call.includes("c9") || call.includes("aetheus-prod")));
  } finally { env.cleanup(); }
});

test("the smoke images stay unless the caller asks for them, as the demo must keep the one it built", () => {
  const kept = setUp();
  const removed = setUp();
  try {
    assert.equal(kept.run().status, 0);
    assert.ok(!kept.calls().some(call => call.startsWith("image")));
    assert.equal(removed.run({ REMOVE_BROWSER_SMOKE_IMAGES: "true" }).status, 0);
    assert.ok(removed.calls().includes("image rm -f img-smoke"));
  } finally { kept.cleanup(); removed.cleanup(); }
});

for (const [label, environment, reason] of [
  ["a project that is not disposable", { COMPOSE_PROJECT: "aetheus" }, /Only a disposable project/],
  ["an identity naming production", { COMPOSE_PROJECT: "aetheus-prod-demo" }, /production token/],
  ["a state root deeper than the workspace's direct child", { STATE_DIR: "/w/run/s/a/b", ENV_FILE: "/w/run/s/a/b/.e", SECRETS_FILE: "/w/run/s/a/b/.s" }, /direct child of the run workspace/],
  // Only production has a directory on the host (2026-10-02): the former /var/lib root is refused.
  ["a state root on the host", { STATE_DIR: "/var/lib/aetheus-demo", ENV_FILE: "/var/lib/aetheus-demo/.e", SECRETS_FILE: "/var/lib/aetheus-demo/.s" }, /must live in the run workspace/],
  ["an environment file outside the state root", { ENV_FILE: "/etc/.env-demo" }, /environment file must live/],
  ["a project name with other characters", { COMPOSE_PROJECT: "Aetheus_Demo" }, /Not a Compose project name/]
]) {
  test(`${label} is refused before docker is called`, () => {
    const env = setUp();
    try {
      const result = env.run(environment);
      assert.equal(result.status, 1);
      assert.match(result.stderr, reason);
      assert.deepEqual(env.calls(), []);
    } finally { env.cleanup(); }
  });
}

test("a selected container that names production stops the reset before anything is removed", () => {
  const env = setUp({ containerName: "aetheus-demo-prod-copy" });
  try {
    const result = env.run();
    assert.equal(result.status, 1);
    assert.match(result.stderr, /names production/);
    assert.ok(!env.calls().some(call => call.startsWith("rm") || call.startsWith("volume rm")));
  } finally { env.cleanup(); }
});
