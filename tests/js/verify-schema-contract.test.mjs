// SPDX-License-Identifier: EUPL-1.2
// The QA side of the seal: it must refuse a contract whose declared schema path the live database
// does not reproduce, otherwise the deploy gate would rest on an unchecked derivation.
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdtempSync, writeFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import assert from "node:assert/strict";

const here = dirname(fileURLToPath(import.meta.url));
const script = join(here, "..", "..", "deploy", "scripts", "verify-schema-contract.mjs");
const sha256 = value => createHash("sha256").update(value).digest("hex");
const fingerprint = migrations => ({ hash: sha256(migrations.join("\n")), migrations });

const BEFORE = ["20260420134123_InitialCreate"];
const AFTER = [...BEFORE, "20260815060150_AddPipelineBuildNumber"];

const run = (baseline, beforeLines, afterLines) => {
  const dir = mkdtempSync(join(tmpdir(), "schema-contract-"));
  try {
    const contractPath = join(dir, "delivery-contract.json");
    writeFileSync(contractPath, JSON.stringify({ schema: 2, baseline }));
    let beforeArg = "-";
    if (beforeLines !== null) {
      beforeArg = join(dir, "before");
      writeFileSync(beforeArg, beforeLines.join("\n") + "\n");
    }
    const afterArg = join(dir, "after");
    writeFileSync(afterArg, afterLines.join("\n") + "\n");
    const r = spawnSync(process.execPath, [script, contractPath, beforeArg, afterArg], {
      encoding: "utf8"
    });
    return { code: r.status, out: r.stdout ?? "", err: r.stderr ?? "" };
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
};

const honest = {
  bootstrap: false,
  schemaBefore: fingerprint(BEFORE),
  schemaAfter: fingerprint(AFTER)
};

test("accepts a contract whose declared path the database reproduces", () => {
  const { code, out } = run(honest, BEFORE, AFTER);
  assert.equal(code, 0);
  assert.match(out, /Schema path proven on a live database/);
});

test("refuses a contract declaring a schemaAfter the migration does not produce", () => {
  const lying = {
    bootstrap: false,
    schemaBefore: fingerprint(BEFORE),
    schemaAfter: fingerprint([...AFTER, "20261231000000_NeverExisted"])
  };
  const { code, err } = run(lying, BEFORE, AFTER);
  assert.notEqual(code, 0);
  assert.match(err, /not the one the contract declares/);
});

test("refuses when V-1 runs on a different schema than the contract inherited", () => {
  // Production moved after the candidate started: the inherited schemaBefore is stale.
  const { code, err } = run(honest, ["20260101000000_SomethingElse"], AFTER);
  assert.notEqual(code, 0);
  assert.match(err, /starting state/);
  assert.match(err, /requalify/i);
});

test("bootstrap proves only schemaAfter", () => {
  const bootstrap = { bootstrap: true, schemaAfter: fingerprint(AFTER) };
  const { code, out } = run(bootstrap, null, AFTER);
  assert.equal(code, 0);
  assert.match(out, /Bootstrap/);
});

test("refuses a non-bootstrap candidate that proves no starting state", () => {
  const { code, err } = run(honest, null, AFTER);
  assert.notEqual(code, 0);
  assert.match(err, /must prove the schema it migrates from/);
});
