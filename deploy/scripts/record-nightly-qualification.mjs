// SPDX-License-Identifier: EUPL-1.2
// One small, stable file describing what the complete nightly qualification found, so a deployment
// can read it without querying the control plane it is about to replace.
//
// It exists because the candidate that reaches production runs the LIGHT profile: it proves the
// package it builds, and leaves the expensive dynamic and historical work to the nightly. That
// division is only honest if the nightly's verdict actually reaches the deployment boundary, which
// is what this file and check-nightly-qualification.mjs carry.
//
// Since PLAN-007 lot 1 the deployment no longer restores or reads this file: it stays the nightly's
// own verdict, published in PublishNightlyEvidence-artifacts.
//
// Deliberately tiny and self-describing: the deployment reads a JSON file restored from an artifact,
// not an API, so it needs no credential and no reachable backend at the moment it decides.
import { createHash } from "node:crypto";
import { mkdirSync, readFileSync, readdirSync, statSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";

const [outputPath, sourceSha, securityDirectory, qualityDirectory, securityHistoryDirectory, extendedDynamicDirectory] =
  process.argv.slice(2);
if (!outputPath || !sourceSha || !securityDirectory) {
  throw new Error("Usage: record-nightly-qualification.mjs <output> <source-sha> <security-directory> [quality-directory] [security-history-directory] [extended-dynamic-security-directory]");
}
if (!/^[0-9a-f]{40}$|^[0-9a-f]{64}$/i.test(sourceSha)) {
  throw new Error("The nightly source SHA must be a full hexadecimal commit identifier.");
}

function findSummary(root, fileName) {
  if (!root) return null;
  try {
    if (!statSync(root).isDirectory()) return null;
  } catch {
    return null;
  }
  const matches = [];
  const visit = directory => {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name);
      if (entry.isDirectory()) visit(path);
      else if (entry.isFile() && entry.name === fileName) matches.push(path);
    }
  };
  visit(root);
  if (matches.length !== 1) return null;
  try {
    return JSON.parse(readFileSync(matches[0], "utf8"));
  } catch {
    return null;
  }
}

// A count the nightly did not observe is reported as null, never as zero: "no finding" and "not
// measured" are different answers, and only one of them may let a deployment through.
function severityCount(summary, severity) {
  const domains = summary?.grade?.domains;
  if (!Array.isArray(domains)) return null;
  let total = null;
  for (const domain of domains) {
    for (const measure of domain?.measures ?? []) {
      if (String(measure?.severity ?? "").toLowerCase() !== severity) continue;
      if (measure?.observed !== true || typeof measure.observedValue !== "number") continue;
      total = (total ?? 0) + measure.observedValue;
    }
  }
  return total;
}

function describe(summary) {
  if (!summary) return { status: "Unavailable", grade: null, completeness: null, pipelineRunId: null };
  return {
    status: String(summary.status ?? "Unknown"),
    grade: /^[A-F]$/.test(summary?.grade?.overallGrade ?? "") ? summary.grade.overallGrade : null,
    completeness: summary?.grade?.completeness ?? null,
    pipelineRunId: Number.isSafeInteger(summary?.pipelineRunId) ? summary.pipelineRunId : null,
    commitHash: summary?.grade?.commitHash ?? null
  };
}

const security = findSummary(securityDirectory, "security-summary.json");
const quality = findSummary(qualityDirectory, "quality-summary.json");
// The two nightly extensions (PLAN-007 lot 2). Each is its own section with its own counts: folding
// them into `security` would change what that section has always meant.
const securityHistory = findSummary(securityHistoryDirectory, "security-summary.json");
const extendedDynamicSecurity = findSummary(extendedDynamicDirectory, "security-summary.json");

for (const [label, summary] of [
  ["security", security], ["quality", quality],
  ["security history", securityHistory], ["extended dynamic security", extendedDynamicSecurity]
]) {
  const commit = summary?.grade?.commitHash;
  if (summary && typeof commit === "string" && commit.toLowerCase() !== sourceSha.toLowerCase()) {
    throw new Error(`The nightly ${label} summary belongs to another commit.`);
  }
}

const qualification = {
  schema: 1,
  profile: "full",
  sourceCommit: sourceSha.toLowerCase(),
  generatedAt: new Date().toISOString(),
  security: {
    ...describe(security),
    criticalFindings: severityCount(security, "critical"),
    highFindings: severityCount(security, "high")
  },
  quality: describe(quality),
  securityHistory: {
    ...describe(securityHistory),
    criticalFindings: severityCount(securityHistory, "critical"),
    highFindings: severityCount(securityHistory, "high")
  },
  extendedDynamicSecurity: {
    ...describe(extendedDynamicSecurity),
    criticalFindings: severityCount(extendedDynamicSecurity, "critical"),
    highFindings: severityCount(extendedDynamicSecurity, "high")
  },
  // The grade AssuranceSeal published for this nightly, or null when the seal did not run: an
  // absent grade is reported as absent, never as a pass.
  assuranceGrade: /^[A-F]$/.test(process.env.CANDIDATE_ASSURANCE_GRADE ?? "")
    ? process.env.CANDIDATE_ASSURANCE_GRADE
    : null
};
qualification.seal = {
  algorithm: "sha256",
  digest: createHash("sha256").update(JSON.stringify(qualification)).digest("hex")
};

mkdirSync(dirname(outputPath), { recursive: true });
writeFileSync(outputPath, `${JSON.stringify(qualification, null, 2)}\n`);
console.log(
  `Nightly qualification recorded for ${qualification.sourceCommit}: security ${qualification.security.status}`
  + ` grade ${qualification.security.grade ?? "-"}, critical ${qualification.security.criticalFindings ?? "not measured"},`
  + ` high ${qualification.security.highFindings ?? "not measured"}; security history`
  + ` ${qualification.securityHistory.status}, extended dynamic security`
  + ` ${qualification.extendedDynamicSecurity.status}; assurance grade ${qualification.assuranceGrade ?? "not sealed"}.`);
