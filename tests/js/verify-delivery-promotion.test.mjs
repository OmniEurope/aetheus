// SPDX-License-Identifier: EUPL-1.2
// Exercises the promotion gate as the pipeline runs it: a real contract on disk, a real schema
// listing, the real script in a child process. Reading the code is not evidence that it refuses what
// it must refuse.
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdtempSync, writeFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import assert from "node:assert/strict";

const here = dirname(fileURLToPath(import.meta.url));
const script = join(here, "..", "..", "deploy", "scripts", "verify-delivery-promotion.mjs");

const sha256 = value => createHash("sha256").update(value).digest("hex");
const canonicalize = value => {
  if (Array.isArray(value)) return value.map(canonicalize);
  if (value && typeof value === "object")
    return Object.fromEntries(Object.entries(value)
      .sort(([l], [r]) => l.localeCompare(r))
      .map(([k, v]) => [k, canonicalize(v)]));
  return value;
};
const fingerprint = migrations => ({
  hash: sha256(migrations.join("\n")),
  migrations
});

const BEFORE = ["20260420134123_InitialCreate", "20260611124145_AddPipelineRunYamlSnapshot"];
const AFTER = [...BEFORE, "20260815060150_AddPipelineBuildNumber"];
const SOURCE = "a".repeat(40);

/** Seals a contract exactly as generate-delivery-contract.mjs does, so candidateId verifies. */
const seal = baseline => {
  const identity = canonicalize({
    schema: 2,
    sourceSha: SOURCE,
    adapter: { name: "test", version: "1" },
    baseline,
    injection: { manifestSha256: sha256("{}"), packages: [] },
    artifacts: {}
  });
  const candidateId = sha256(JSON.stringify(identity));
  return canonicalize({ ...identity, candidateId, candidateVersion: `c-${SOURCE}` });
};

const run = (contract, schemaLines) => {
  const dir = mkdtempSync(join(tmpdir(), "promotion-"));
  try {
    const contractPath = join(dir, "delivery-contract.json");
    writeFileSync(contractPath, JSON.stringify(contract, null, 2));
    let schemaArg = "-";
    if (schemaLines !== null) {
      schemaArg = join(dir, "schema");
      writeFileSync(schemaArg, schemaLines.join("\n") + "\n");
    }
    const result = spawnSync(process.execPath, [script, contractPath, schemaArg, dir], {
      encoding: "utf8"
    });
    return { code: result.status, out: result.stdout ?? "", err: result.stderr ?? "" };
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
};

const migrating = seal({
  bootstrap: false,
  schemaBefore: fingerprint(BEFORE),
  schemaAfter: fingerprint(AFTER)
});

test("accepts production standing on the proven pre-migration schema", () => {
  const { code, out } = run(migrating, BEFORE);
  assert.equal(code, 0);
  assert.match(out, /proven pre-migration schema/);
  assert.match(out, /Verified immutable candidate/);
});

test("accepts production already migrated, deploying binaries only", () => {
  // The case the old gate refused outright: redeploy, retry after a switch, cross-deploy with no
  // migration. Nothing in the schema moves, so nothing unproven is crossed.
  const { code, out } = run(migrating, AFTER);
  assert.equal(code, 0);
  assert.match(out, /already stands on the proven post-migration schema/);
  assert.match(out, /no-op/);
});

test("refuses a schema that is neither proven state, naming all three", () => {
  const unknown = [...AFTER, "20260901000000_SomethingNobodyQualified"];
  const { code, err } = run(migrating, unknown);
  assert.notEqual(code, 0);
  assert.match(err, /neither state this candidate was proven against/);
  assert.match(err, new RegExp(fingerprint(BEFORE).hash));
  assert.match(err, new RegExp(fingerprint(AFTER).hash));
  assert.match(err, new RegExp(fingerprint(unknown).hash));
});

test("refuses a non-bootstrap candidate when no production schema is supplied", () => {
  const { code, err } = run(migrating, null);
  assert.notEqual(code, 0);
  assert.match(err, /production schema state required/);
});

test("accepts a bootstrap onto an empty database", () => {
  const bootstrap = seal({ bootstrap: true, schemaAfter: fingerprint(AFTER) });
  const { code, out } = run(bootstrap, null);
  assert.equal(code, 0);
  assert.match(out, /Bootstrap delivery/);
});

test("refuses a bootstrap over an existing foreign schema", () => {
  const bootstrap = seal({ bootstrap: true, schemaAfter: fingerprint(AFTER) });
  const { code, err } = run(bootstrap, BEFORE);
  assert.notEqual(code, 0);
  assert.match(err, /cannot replace an existing schema/);
});

test("refuses an old identity-sealed contract instead of passing it through", () => {
  // Schema 1 carries no fingerprints. Letting it through would promote a delivery this gate never
  // actually checked.
  const legacy = {
    schema: 1,
    candidateId: "0".repeat(64),
    candidateVersion: `c-${SOURCE}`,
    sourceSha: SOURCE,
    baseline: { bootstrap: false, candidateVersion: "c-old", sourceSha: "b".repeat(40) }
  };
  const { code, err } = run(legacy, BEFORE);
  assert.notEqual(code, 0);
  assert.match(err, /schema 1/);
  assert.match(err, /Requalify/);
});

test("still refuses a tampered contract identity", () => {
  // The identity check must survive the baseline rework untouched.
  const tampered = { ...migrating, sourceSha: "c".repeat(40) };
  const { code, err } = run(tampered, BEFORE);
  assert.notEqual(code, 0);
  assert.match(err, /identity is invalid/);
});
