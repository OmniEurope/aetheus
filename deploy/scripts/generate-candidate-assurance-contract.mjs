// SPDX-License-Identifier: EUPL-1.2
import { createHash } from "node:crypto";
import { copyFileSync, mkdirSync, readFileSync, readdirSync, statSync, writeFileSync } from "node:fs";
import { join, relative } from "node:path";

// The two trailing directories are the nightly extensions (PLAN-007 lot 2): aetheus-security-history
// and aetheus-qa-extended each grade their own run, and a `full` qualification seals both grades
// beside the three every contract carries. They come together or not at all.
const [outputPath, evidenceDirectory, sourceSha, qualityDirectory, securityDirectory, qaDirectory, provenancePath,
  securityHistoryDirectory, extendedQaDirectory] = process.argv.slice(2);
if (!outputPath || !evidenceDirectory || !sourceSha || !qualityDirectory || !securityDirectory || !qaDirectory || !provenancePath
    || (securityHistoryDirectory === undefined) !== (extendedQaDirectory === undefined)) {
  throw new Error("Usage: generate-candidate-assurance-contract.mjs <output> <evidence-directory> <source-sha> <quality-directory> <security-directory> <qa-directory> <artifact-provenance> [<security-history-directory> <extended-qa-directory>]");
}
if (!/^[0-9a-f]{40}$|^[0-9a-f]{64}$/i.test(sourceSha)) {
  throw new Error("The candidate source SHA must be a full hexadecimal commit identifier.");
}

mkdirSync(evidenceDirectory, { recursive: true });

function findFiles(root, fileName) {
  try {
    if (!statSync(root).isDirectory()) return [];
  } catch {
    return [];
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
  return matches.sort();
}

function captureAnalysis(label, directory, fileName) {
  const files = findFiles(directory, fileName);
  if (files.length === 0) return unavailableAnalysis(label, "summary-missing");
  if (files.length !== 1) return unavailableAnalysis(label, "summary-ambiguous");

  let summary;
  let bytes;
  try {
    bytes = readFileSync(files[0]);
    summary = JSON.parse(bytes.toString("utf8"));
  } catch {
    return unavailableAnalysis(label, "summary-invalid-json");
  }

  const grade = summary?.grade?.overallGrade;
  const completeness = summary?.grade?.completeness;
  const commitHash = summary?.grade?.commitHash;
  const pipelineRunId = summary?.pipelineRunId ?? summary?.grade?.pipelineRunId;
  const gateStatus = String(summary.status ?? "Unknown");
  if (!Number.isSafeInteger(pipelineRunId) || pipelineRunId <= 0) throw new Error(`${label} pipeline run identity is invalid.`);
  if (typeof commitHash !== "string" || commitHash.toLowerCase() !== sourceSha.toLowerCase()) {
    throw new Error(`${label} summary belongs to another commit.`);
  }

  const destination = join(evidenceDirectory, `${label}-summary.json`);
  copyFileSync(files[0], destination);
  const gateUsable = gateStatus === "Passed" || gateStatus === "Warning";
  const gradeAvailable = /^[A-F]$/.test(grade ?? "")
    && completeness === "Complete"
    && gateUsable;
  return {
    status: gradeAvailable ? "Available" : "Unavailable",
    reason: gradeAvailable
      ? undefined
      : gateUsable ? "grade-incomplete-or-unavailable" : "gate-error-or-blocked",
    reportedGrade: /^[A-F]$/.test(grade ?? "") ? grade : null,
    effectiveGrade: gradeAvailable ? grade : "F",
    completeness,
    gateStatus,
    pipelineRunId,
    sourceSha: commitHash,
    sha256: createHash("sha256").update(bytes).digest("hex"),
    evidence: relative(join(outputPath, ".."), destination).replaceAll("\\", "/")
  };
}

function unavailableAnalysis(label, reason) {
  const destination = join(evidenceDirectory, `${label}-summary.json`);
  const evidence = {
    schema: 1,
    status: "Unavailable",
    reason,
    sourceSha: sourceSha.toLowerCase(),
    generatedBy: "candidate-assurance"
  };
  const bytes = Buffer.from(`${JSON.stringify(evidence, null, 2)}\n`, "utf8");
  writeFileSync(destination, bytes);
  return {
    status: "Unavailable",
    reason,
    reportedGrade: null,
    effectiveGrade: "F",
    completeness: "Incomplete",
    gateStatus: "Unavailable",
    pipelineRunId: null,
    sourceSha: sourceSha.toLowerCase(),
    sha256: createHash("sha256").update(bytes).digest("hex"),
    evidence: relative(join(outputPath, ".."), destination).replaceAll("\\", "/")
  };
}

function outcome(variableName) {
  const value = process.env[variableName];
  if (value === "0") return { status: "Passed", effectiveGrade: "A" };
  if (/^[1-9][0-9]*$/.test(value ?? "")) return { status: "Failed", effectiveGrade: "F", exitCode: Number(value) };
  return { status: "Unavailable", effectiveGrade: "F", reason: "output-variable-unavailable" };
}

function compatibilityOutcome(variableName, modeVariable, testedVariable) {
  const result = outcome(variableName);
  const mode = process.env[modeVariable];
  const tested = process.env[testedVariable];
  if (mode !== "NMinusOne" && mode !== "Bootstrap") {
    return { ...result, status: "Unavailable", effectiveGrade: "F", reason: "compatibility-mode-unavailable", mode: null, tested: null };
  }
  return { ...result, mode, tested: tested || null };
}

// Gates an adapting project genuinely cannot produce: Toto ships no Roslyn analyzers and no
// agent, so those two evidences do not exist for it. Naming one here records it as an
// unrequired NotRun instead of pretending it passed. The default is empty, so Aetheus keeps
// requiring all eight.
const inapplicableTests = new Set(
  (process.env.AETHEUS_ASSURANCE_INAPPLICABLE_TESTS ?? "")
    .split(",")
    .map(entry => entry.trim())
    .filter(entry => entry.length > 0));

function declared(name, build) {
  if (!inapplicableTests.has(name)) return build();
  return { required: false, status: "NotRun", effectiveGrade: null, reason: "not-applicable-to-project" };
}

// Qualification profile (PLAN-006 lot 9). `light` is what every push runs; it does not run the full
// V-1 suites nor the retained-agent contract, so those three are recorded NotRun and unrequired
// rather than read from output variables the run never published. The rollback is still proved, by
// previousSmoke below, which is required in BOTH profiles: a profile may remove depth, never the
// proof itself. The image analyses stay required in both, because they judge the very images this
// candidate packages.
const profile = process.env.AETHEUS_ASSURANCE_PROFILE ?? "full";
if (profile !== "light" && profile !== "full") {
  throw new Error(`AETHEUS_ASSURANCE_PROFILE must be 'light' or 'full', not '${profile}'.`);
}
// previousIntegration and previousE2E are NOT here, deliberately. They were, and that quietly moved
// the rollback proof off the commit being packaged onto whatever develop happened to be at 2 a.m.
// A candidate is what a deployment stands on, so it proves its own rollback. What the light profile
// still leaves to the nightly is the retained-agent contract: it exercises the AGENT protocol across
// versions, which is a property of the fleet rather than of the package.
const nightlyOnlyTests = profile === "light" ? new Set(["agentCompatibility"]) : new Set();

function profiled(name, build) {
  if (!nightlyOnlyTests.has(name)) return build();
  return { required: false, status: "NotRun", effectiveGrade: null, reason: "nightly-profile" };
}

const performanceSetting = process.env.AETHEUS_ASSURANCE_PERFORMANCE_ENABLED;
const performance = performanceSetting === "false"
  ? { required: false, status: "NotRun", effectiveGrade: null }
  : { required: true, ...outcome("QA_PERFORMANCE_GATE_STATUS") };
if (performanceSetting !== "true" && performanceSetting !== "false") {
  performance.reason = "performance-setting-unavailable";
  performance.status = "Unavailable";
}

const analyses = {
  quality: captureAnalysis("quality", qualityDirectory, "quality-summary.json"),
  security: captureAnalysis("security", securityDirectory, "security-summary.json"),
  dynamicSecurity: captureAnalysis("dynamic-security", qaDirectory, "security-summary.json")
};
if (securityHistoryDirectory !== undefined) {
  // A light contract carrying them would claim depth its profile excuses; the verifier refuses the
  // same combination, so refusing it here keeps the generator from producing what cannot be read.
  if (profile !== "full") throw new Error("The nightly extension analyses belong to a full qualification only.");
  analyses.securityHistory = captureAnalysis("security-history", securityHistoryDirectory, "security-summary.json");
  analyses.extendedDynamicSecurity = captureAnalysis(
    "extended-dynamic-security", extendedQaDirectory, "security-summary.json");
}
const tests = {
  unit: declared("unit", () => ({ required: true, ...outcome("UNIT_TEST_GATE_STATUS") })),
  analyzers: declared("analyzers", () => ({ required: true, ...outcome("ANALYZER_TEST_GATE_STATUS") })),
  currentIntegration: declared("currentIntegration",
    () => ({ required: true, ...outcome("QA_CURRENT_INTEGRATION_GATE_STATUS") })),
  currentE2E: declared("currentE2E", () => ({ required: true, ...outcome("QA_CURRENT_E2E_GATE_STATUS") })),
  previousSmoke: declared("previousSmoke", () => ({ required: true, ...compatibilityOutcome(
    "QA_PREVIOUS_SMOKE_GATE_STATUS", "CompatibilityMode", "PreviousVersionTested") })),
  agentCompatibility: declared("agentCompatibility", () => profiled("agentCompatibility",
    () => ({ required: true, ...compatibilityOutcome(
      "QA_AGENT_COMPATIBILITY_GATE_STATUS", "AgentCompatibilityMode", "PreviousAgentTested") }))),
  previousIntegration: declared("previousIntegration", () => profiled("previousIntegration",
    () => ({ required: true, ...compatibilityOutcome(
      "QA_PREVIOUS_INTEGRATION_GATE_STATUS", "CompatibilityMode", "PreviousVersionTested") }))),
  previousE2E: declared("previousE2E", () => profiled("previousE2E",
    () => ({ required: true, ...compatibilityOutcome(
      "QA_PREVIOUS_E2E_GATE_STATUS", "CompatibilityMode", "PreviousVersionTested") }))),
  performance
};
const provenanceBytes = readFileSync(provenancePath);
const artifactProvenance = JSON.parse(provenanceBytes.toString("utf8"));
if (artifactProvenance?.sourceCommit?.toLowerCase() !== sourceSha.toLowerCase()) {
  throw new Error("Artifact provenance belongs to another commit.");
}
const gradingScale = ["A", "B", "C", "D", "E", "F"];
const effectiveGrades = [
  ...Object.values(analyses).map(value => value.effectiveGrade),
  ...Object.values(tests).filter(value => value.required).map(value => value.effectiveGrade)
];
const overallGrade = effectiveGrades.reduce(
  (worst, grade) => gradingScale.indexOf(grade) > gradingScale.indexOf(worst) ? grade : worst,
  "A");

const contract = {
  schema: 2,
  sourceSha: sourceSha.toLowerCase(),
  profile,
  gradingScale,
  overallGrade,
  bootstrapAuthorized: process.env.AETHEUS_ASSURANCE_ALLOW_BOOTSTRAP === "true",
  checkpointResume: {
    sourceRunId: /^[1-9][0-9]*$/.test(process.env.AETHEUS_RESUME_SOURCE_RUN_ID ?? "")
      ? Number(process.env.AETHEUS_RESUME_SOURCE_RUN_ID)
      : null,
    reusedRuns: Object.fromEntries([
      ["ci", process.env.AETHEUS_CHECKPOINT_CI_RUN_ID],
      ["quality", process.env.AETHEUS_CHECKPOINT_QUALITY_RUN_ID],
      ["security", process.env.AETHEUS_CHECKPOINT_SECURITY_RUN_ID]
    ].filter(([, value]) => /^[1-9][0-9]*$/.test(value ?? "")).map(([key, value]) => [key, Number(value)]))
  },
  analyses,
  tests,
  artifactReferences: {
    sourceSha: artifactProvenance.sourceCommit,
    categories: artifactProvenance.categories,
    provenanceSha256: createHash("sha256").update(provenanceBytes).digest("hex")
  }
};
contract.seal = {
  algorithm: "sha256",
  digest: createHash("sha256").update(JSON.stringify(contract)).digest("hex")
};

mkdirSync(join(outputPath, ".."), { recursive: true });
writeFileSync(outputPath, `${JSON.stringify(contract, null, 2)}\n`);
console.log(`Candidate assurance contract written to ${outputPath} with grade ${overallGrade}.`);
console.log(`##aetheus[setvariable name=CANDIDATE_ASSURANCE_GRADE]${overallGrade}`);
