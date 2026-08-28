// SPDX-License-Identifier: EUPL-1.2
import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { classifyDotnetTestResult } from "./classify-dotnet-test-result.mjs";

function evidence({ failed = 0, error = 0, outcome = "Completed", coverage = true } = {}) {
  const root = mkdtempSync(join(tmpdir(), "aetheus-test-proof-"));
  const trx = join(root, "result.trx");
  const passed = failed || error ? 1 : 2;
  const total = passed + failed + error;
  writeFileSync(trx, `<TestRun><ResultSummary outcome="${outcome}"><Counters total="${total}" executed="${total}" passed="${passed}" failed="${failed}" error="${error}" timeout="0" aborted="0" disconnected="0" /></ResultSummary></TestRun>`);
  const coverageDirectory = join(root, "coverage");
  mkdirSync(coverageDirectory);
  if (coverage) writeFileSync(join(coverageDirectory, "coverage.cobertura.xml"), "<coverage lines-valid=\"10\" lines-covered=\"8\" line-rate=\"0.8\" />");
  return { trx, coverageDirectory };
}

test("classifies a passing execution as passing evidence", () => {
  const proof = evidence();
  assert.equal(classifyDotnetTestResult(0, proof.trx, proof.coverageDirectory), 0);
});

test("validates every report emitted by a multi-assembly coverage run", () => {
  const proof = evidence();
  const second = join(proof.coverageDirectory, "second");
  mkdirSync(second);
  writeFileSync(join(second, "coverage.cobertura.xml"), "<coverage lines-valid=\"4\" lines-covered=\"3\" line-rate=\"0.75\" />");
  assert.equal(classifyDotnetTestResult(0, proof.trx, proof.coverageDirectory), 0);
});

test("classifies failed assertions as advisory evidence", () => {
  const proof = evidence({ failed: 2, outcome: "Failed" });
  assert.equal(classifyDotnetTestResult(1, proof.trx, proof.coverageDirectory), 1);
});

test("rejects an aborted test run as incomplete evidence", () => {
  const proof = evidence({ outcome: "Aborted" });
  assert.throws(() => classifyDotnetTestResult(1, proof.trx, proof.coverageDirectory), /incomplete/);
});

test("rejects failed counters with a completed outcome", () => {
  const proof = evidence({ failed: 1 });
  assert.throws(() => classifyDotnetTestResult(1, proof.trx, proof.coverageDirectory), /inconsistent/);
});

test("rejects a technical test-host failure", () => {
  const proof = evidence({ error: 1 });
  assert.throws(() => classifyDotnetTestResult(1, proof.trx, proof.coverageDirectory), /technical test-host failure/);
});

test("rejects a command failure without a failed assertion", () => {
  const proof = evidence();
  assert.throws(() => classifyDotnetTestResult(1, proof.trx, proof.coverageDirectory), /inconsistent/);
});

test("rejects missing coverage proof", () => {
  const proof = evidence({ coverage: false });
  assert.throws(() => classifyDotnetTestResult(0, proof.trx, proof.coverageDirectory), /at least one Cobertura/);
});

test("rejects one malformed report among multiple coverage reports", () => {
  const proof = evidence();
  const second = join(proof.coverageDirectory, "second");
  mkdirSync(second);
  writeFileSync(join(second, "coverage.cobertura.xml"), "<not-coverage />");
  assert.throws(() => classifyDotnetTestResult(0, proof.trx, proof.coverageDirectory), /no coverage root/);
});
