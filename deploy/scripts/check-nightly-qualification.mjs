// SPDX-License-Identifier: EUPL-1.2
// The deployment boundary's reading of the nightly verdict (PLAN-006 lot 9).
// No pipeline calls it since PLAN-007 lot 1: the deployment no longer reads the nightly. It is kept,
// with nightly-qualification.test.mjs, as the reader of the file record-nightly-qualification.mjs
// writes; the rest of this header describes the gate it was.
//
// The candidate that produced the release ran the LIGHT profile, so the eight OpenAPI DAST
// partitions, the active scan, the full Gitleaks history and the complete V-1 suites were the
// nightly's job. This is where that work is allowed to stop a deployment.
//
// It refuses exactly one thing: a nightly, on an ancestor of the commit being deployed and recent
// enough to still describe it, that counted a critical or high security finding. Everything else -
// no nightly at all, a nightly too old, a nightly on an unrelated commit, a nightly red for a
// reason other than those counts - is reported and lets the deployment proceed, because a
// deployment that cannot ship without a green nightly is a deployment hostage to a scheduled job.
//
// Usage:
//   check-nightly-qualification.mjs <qualification.json|-> <release-commit> [--max-age-days N]
// Exit 0 with a verdict on stdout, or exit 1 with the refusal on stderr.
import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";

const argv = process.argv.slice(2);
const [qualificationPath, releaseCommit] = argv;
if (!qualificationPath || !releaseCommit) {
  throw new Error("Usage: check-nightly-qualification.mjs <qualification.json|-> <release-commit> [--max-age-days N]");
}
if (!/^[0-9a-f]{40}$|^[0-9a-f]{64}$/i.test(releaseCommit)) {
  throw new Error("The release commit must be a full hexadecimal commit identifier.");
}
const maxAgeIndex = argv.indexOf("--max-age-days");
const maxAgeDays = maxAgeIndex === -1 ? 7 : Number(argv[maxAgeIndex + 1]);
if (!Number.isFinite(maxAgeDays) || maxAgeDays <= 0) throw new Error("--max-age-days must be a positive number.");

const advisory = message => {
  console.log(`Nightly qualification: ${message} The deployment is not blocked by it.`);
  process.exit(0);
};

if (qualificationPath === "-") advisory("no nightly evidence was restored for this deployment.");

let qualification;
try {
  qualification = JSON.parse(readFileSync(qualificationPath, "utf8"));
} catch (error) {
  advisory(`the restored evidence could not be read (${error.message}).`);
}

if (qualification.schema !== 1) advisory(`unsupported evidence schema ${qualification.schema}.`);
const seal = qualification.seal;
const withoutSeal = { ...qualification };
delete withoutSeal.seal;
const digest = createHash("sha256").update(JSON.stringify(withoutSeal)).digest("hex");
if (seal?.algorithm !== "sha256" || seal.digest !== digest) {
  // A tampered or truncated file is worth saying out loud, but it is still not a security finding:
  // refusing here would let a corrupted artifact block every deployment.
  advisory("the restored evidence does not match its own seal, so it is ignored.");
}

const nightlyCommit = String(qualification.sourceCommit ?? "");
if (!/^[0-9a-f]{40}$|^[0-9a-f]{64}$/.test(nightlyCommit)) advisory("the evidence names no usable commit.");

const ageMs = Date.now() - Date.parse(qualification.generatedAt ?? "");
if (!Number.isFinite(ageMs)) advisory("the evidence carries no usable timestamp.");
const ageDays = ageMs / 86_400_000;
if (ageDays > maxAgeDays) {
  advisory(`the newest nightly is ${ageDays.toFixed(1)} days old, past the ${maxAgeDays}-day window.`);
}

let ancestor = false;
try {
  execFileSync("git", ["merge-base", "--is-ancestor", nightlyCommit, releaseCommit], { stdio: "ignore" });
  ancestor = true;
} catch {
  // Not an ancestor, or git could not tell: either way the release does not contain the nightly.
}
if (!ancestor) {
  advisory(`the newest nightly ran on ${nightlyCommit.slice(0, 12)}, which this release does not contain.`);
}

const critical = qualification.security?.criticalFindings;
const high = qualification.security?.highFindings;
if (typeof critical !== "number" || typeof high !== "number") {
  advisory("the nightly measured no critical or high security counts.");
}
if (critical > 0 || high > 0) {
  console.error(
    `Nightly qualification REFUSES this deployment: the nightly of ${nightlyCommit.slice(0, 12)}`
    + ` (${ageDays.toFixed(1)} days old, an ancestor of this release) counted ${critical} critical and`
    + ` ${high} high security findings. Adjudicate them, or deploy a commit whose nightly is clean.`);
  process.exit(1);
}
console.log(
  `Nightly qualification: the nightly of ${nightlyCommit.slice(0, 12)} (${ageDays.toFixed(1)} days old,`
  + ` security grade ${qualification.security?.grade ?? "-"}) counted no critical or high findings.`);
