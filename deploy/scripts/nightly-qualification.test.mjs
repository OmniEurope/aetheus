// SPDX-License-Identifier: EUPL-1.2
// The nightly verdict is the only thing that carries the checks a light candidate does not run all
// the way to the deployment boundary, so both of its failure directions matter: it must refuse a
// real finding, and it must never hold a deployment hostage to a missing or stale nightly.
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const directory = fileURLToPath(new URL(".", import.meta.url));
const recorder = join(directory, "record-nightly-qualification.mjs");
const checker = join(directory, "check-nightly-qualification.mjs");

const summary = ({ commit, critical = 0, high = 0, grade = "B", observed = true }) => ({
  pipelineRunId: 42,
  status: "Passed",
  grade: {
    overallGrade: grade,
    completeness: "Complete",
    commitHash: commit,
    domains: [{
      domain: "Security",
      grade,
      measures: [
        { key: "grade.security.critical", severity: "Critical", observed, observedValue: observed ? critical : null },
        { key: "grade.security.high", severity: "High", observed, observedValue: observed ? high : null }
      ]
    }]
  }
});

const withRepository = body => {
  const root = mkdtempSync(join(tmpdir(), "nightly-qual-"));
  try {
    const git = (...args) => execFileSync("git", args, { cwd: root, stdio: "pipe" });
    git("init", "-q", "-b", "main");
    git("config", "user.email", "test@example.invalid");
    git("config", "user.name", "Test");
    writeFileSync(join(root, "a.txt"), "one\n");
    git("add", "-A");
    git("commit", "-q", "-m", "first");
    const first = execFileSync("git", ["rev-parse", "HEAD"], { cwd: root, encoding: "utf8" }).trim();
    writeFileSync(join(root, "a.txt"), "two\n");
    git("add", "-A");
    git("commit", "-q", "-m", "second");
    const second = execFileSync("git", ["rev-parse", "HEAD"], { cwd: root, encoding: "utf8" }).trim();
    return body({ root, first, second, git });
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
};

const record = (root, commit, options = {}) => {
  const securityDirectory = join(root, "security");
  mkdirSync(securityDirectory, { recursive: true });
  writeFileSync(join(securityDirectory, "security-summary.json"), `${JSON.stringify(summary({ commit, ...options }))}\n`);
  const output = join(root, "nightly-qualification.json");
  const result = spawnSync(process.execPath, [recorder, output, commit, securityDirectory], { encoding: "utf8" });
  assert.equal(result.status, 0, result.stderr);
  return output;
};

const check = (root, path, commit, extra = []) =>
  spawnSync(process.execPath, [checker, path, commit, ...extra], { cwd: root, encoding: "utf8" });

const reseal = (path, mutate) => {
  const value = JSON.parse(readFileSync(path, "utf8"));
  mutate(value);
  delete value.seal;
  const { createHash } = globalThisCrypto;
  value.seal = { algorithm: "sha256", digest: createHash("sha256").update(JSON.stringify(value)).digest("hex") };
  writeFileSync(path, `${JSON.stringify(value, null, 2)}\n`);
};
const globalThisCrypto = await import("node:crypto");

test("a clean nightly on an ancestor lets the deployment through", () => withRepository(({ root, first, second }) => {
  const path = record(root, first);
  const result = check(root, path, second);
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /counted no critical or high findings/);
}));

test("a nightly counting a high finding refuses the deployment", () => withRepository(({ root, first, second }) => {
  const path = record(root, first, { high: 2, grade: "D" });
  const result = check(root, path, second);
  assert.equal(result.status, 1);
  assert.match(result.stderr, /REFUSES this deployment/);
  assert.match(result.stderr, /0 critical and 2 high/);
}));

test("a nightly counting a critical finding refuses the deployment", () => withRepository(({ root, first, second }) => {
  const path = record(root, first, { critical: 1, grade: "F" });
  const result = check(root, path, second);
  assert.equal(result.status, 1);
  assert.match(result.stderr, /1 critical/);
}));

test("no nightly at all is reported, not blocking", () => withRepository(({ root, second }) => {
  const result = check(root, "-", second);
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /no nightly evidence was restored/);
  assert.match(result.stdout, /not blocked/);
}));

test("a nightly on a commit this release does not contain is reported, not blocking",
  () => withRepository(({ root, first, second }) => {
    // Recorded on the newer commit, checked against the older one: not an ancestor.
    const path = record(root, second, { high: 5 });
    const result = check(root, path, first);
    assert.equal(result.status, 0, result.stderr);
    assert.match(result.stdout, /which this release does not contain/);
  }));

test("a nightly older than the window is reported, not blocking", () => withRepository(({ root, first, second }) => {
  const path = record(root, first, { high: 5 });
  reseal(path, value => { value.generatedAt = new Date(Date.now() - 20 * 86_400_000).toISOString(); });
  const result = check(root, path, second);
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /past the 7-day window/);
}));

test("counts the nightly never measured are reported, not blocking", () => withRepository(({ root, first, second }) => {
  const path = record(root, first, { observed: false });
  const recorded = JSON.parse(readFileSync(path, "utf8"));
  assert.equal(recorded.security.criticalFindings, null);
  const result = check(root, path, second);
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /measured no critical or high security counts/);
}));

test("a tampered file is ignored rather than trusted or fatal", () => withRepository(({ root, first, second }) => {
  const path = record(root, first, { high: 4 });
  const value = JSON.parse(readFileSync(path, "utf8"));
  value.security.highFindings = 0;
  writeFileSync(path, `${JSON.stringify(value, null, 2)}\n`);
  const result = check(root, path, second);
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /does not match its own seal/);
}));

test("a summary belonging to another commit is refused at recording time",
  () => withRepository(({ root, first, second }) => {
    const securityDirectory = join(root, "mismatched");
    mkdirSync(securityDirectory, { recursive: true });
    writeFileSync(join(securityDirectory, "security-summary.json"),
      `${JSON.stringify(summary({ commit: second }))}\n`);
    const result = spawnSync(process.execPath,
      [recorder, join(root, "out.json"), first, securityDirectory], { encoding: "utf8" });
    assert.notEqual(result.status, 0);
    assert.match(result.stderr, /belongs to another commit/);
  }));

// PLAN-007 lot 2: the two nightly extensions and the sealed grade are recorded in their own sections,
// and a missing one reads as unavailable rather than clean.
test("the extension summaries and the sealed grade are recorded, a missing one as unavailable",
  () => withRepository(({ root, first }) => {
    const write = (name, value) => {
      const path = join(root, name);
      mkdirSync(path, { recursive: true });
      writeFileSync(join(path, "security-summary.json"), `${JSON.stringify(value)}\n`);
      return path;
    };
    const security = write("security", summary({ commit: first }));
    const history = write("history", summary({ commit: first, high: 2, grade: "C" }));
    const output = join(root, "qualification.json");
    const result = spawnSync(process.execPath,
      [recorder, output, first, security, join(root, "no-quality"), history, join(root, "no-extended")],
      { encoding: "utf8", env: { ...process.env, CANDIDATE_ASSURANCE_GRADE: "C" } });
    assert.equal(result.status, 0, result.stderr);
    const recorded = JSON.parse(readFileSync(output, "utf8"));
    assert.equal(recorded.securityHistory.grade, "C");
    assert.equal(recorded.securityHistory.highFindings, 2);
    assert.equal(recorded.extendedDynamicSecurity.status, "Unavailable");
    assert.equal(recorded.extendedDynamicSecurity.highFindings, null);
    assert.equal(recorded.assuranceGrade, "C");
    const unsealed = spawnSync(process.execPath, [recorder, output, first, security],
      { encoding: "utf8", env: { ...process.env, CANDIDATE_ASSURANCE_GRADE: "" } });
    assert.equal(unsealed.status, 0, unsealed.stderr);
    assert.equal(JSON.parse(readFileSync(output, "utf8")).assuranceGrade, null);
  }));
