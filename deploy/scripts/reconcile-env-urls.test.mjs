// SPDX-License-Identifier: EUPL-1.2
// reconcile-env-urls.sh rewrites lines of the production secret-zero file. What it must never do is
// touch anything but the URL lines it is given, or leave the file half-written; both are observed here
// on the real script.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import test from "node:test";

const script = resolve("deploy/scripts/reconcile-env-urls.sh");
const original = "APPNAME=aetheus\nDB_PASSWORD=0a1b2c\nAPI_BASE_URL=https://aetheus-api.example.org\nFRONT_URL=https://app.example.org\n";

function run(content, ...pairs) {
  const root = mkdtempSync(join(tmpdir(), "aetheus-env-"));
  const file = join(root, ".env-prod");
  writeFileSync(file, content);
  const result = spawnSync("sh", [script, file.replace(/\\/g, "/"), ...pairs], { encoding: "utf8" });
  const after = readFileSync(file, "utf8");
  const files = readdirSync(root);
  rmSync(root, { recursive: true, force: true });
  return { result, after, files };
}

test("a renamed host rewrites that line only, after a dated copy", () => {
  const { result, after, files } = run(original,
    "API_BASE_URL=https://api.example.org", "FRONT_URL=https://app.example.org");

  assert.equal(result.status, 0, result.stderr);
  assert.equal(after, original.replace("https://aetheus-api.example.org", "https://api.example.org"));
  assert.equal(files.filter(name => /^\.env-prod-\d{8}T\d{6}Z$/.test(name)).length, 1);
  assert.equal(files.length, 2, `no temporary file may remain: ${files}`);
});

test("nothing to change leaves the file and makes no copy", () => {
  const { result, after, files } = run(original, "FRONT_URL=https://app.example.org");

  assert.equal(result.status, 0, result.stderr);
  assert.equal(after, original);
  assert.deepEqual(files, [".env-prod"]);
});

test("a missing key is appended", () => {
  const { result, after } = run("APPNAME=aetheus\n", "FRONT_URL=https://app.example.org");

  assert.equal(result.status, 0, result.stderr);
  assert.equal(after, "APPNAME=aetheus\nFRONT_URL=https://app.example.org\n");
});

test("a value that is not a bare https origin is refused and the file is untouched", () => {
  for (const bad of ["API_BASE_URL=http://api.example.org", "API_BASE_URL=https://api.example.org/x",
    "API_BASE_URL=https://api.example.org;rm", "db_password=https://x.example.org"]) {
    const { result, after } = run(original, bad);
    assert.equal(result.status, 1, bad);
    assert.equal(after, original, bad);
  }
});
