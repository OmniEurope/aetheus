// SPDX-License-Identifier: EUPL-1.2
import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

const MaxEvidenceBytes = 100 * 1024 * 1024;

function readBounded(path, label) {
  const size = statSync(path).size;
  if (size <= 0 || size > MaxEvidenceBytes) {
    throw new Error(`${label} must contain between 1 and ${MaxEvidenceBytes} bytes.`);
  }
  return readFileSync(path, "utf8");
}

function attributes(fragment) {
  return Object.fromEntries([...fragment.matchAll(/([A-Za-z][A-Za-z0-9-]*)="([^"]*)"/g)]
    .map(match => [match[1], match[2]]));
}

function counter(counters, name) {
  const value = counters[name];
  if (!/^[0-9]+$/.test(value ?? "")) throw new Error(`TRX counter '${name}' is missing or invalid.`);
  return Number(value);
}

function validateCoverage(directory) {
  const matches = [];
  const visit = path => {
    for (const entry of readdirSync(path, { withFileTypes: true })) {
      const candidate = join(path, entry.name);
      if (entry.isDirectory()) visit(candidate);
      else if (entry.isFile() && entry.name === "coverage.cobertura.xml") matches.push(candidate);
    }
  };
  visit(directory);
  if (matches.length === 0) throw new Error("Expected at least one Cobertura report, found 0.");
  for (const match of matches) {
    const xml = readBounded(match, "Cobertura evidence");
    const root = xml.match(/<coverage\b([^>]*)>/)?.[1];
    if (!root) throw new Error("Cobertura evidence has no coverage root.");
    const values = attributes(root);
    const valid = Number(values["lines-valid"]);
    const covered = Number(values["lines-covered"]);
    const rate = Number(values["line-rate"]);
    if (!Number.isInteger(valid) || valid <= 0 || !Number.isInteger(covered)
        || covered < 0 || covered > valid || !Number.isFinite(rate) || rate < 0 || rate > 1) {
      throw new Error("Cobertura line counters are missing or invalid.");
    }
  }
}

export function classifyDotnetTestResult(rawExitCode, trxPath, coverageDirectory = "-") {
  if (!Number.isSafeInteger(rawExitCode) || rawExitCode < 0 || rawExitCode > 255) {
    throw new Error("The raw dotnet test exit code is invalid.");
  }
  const trx = readBounded(trxPath, "TRX evidence");
  const summaries = [...trx.matchAll(/<ResultSummary\b([^>]*)>/g)];
  const counterNodes = [...trx.matchAll(/<Counters\b([^>]*)\/?\s*>/g)];
  if (summaries.length !== 1 || counterNodes.length !== 1) {
    throw new Error("TRX evidence must contain exactly one result summary and one counter set.");
  }
  const summary = attributes(summaries[0][1]);
  const counters = attributes(counterNodes[0][1]);
  const total = counter(counters, "total");
  const executed = counter(counters, "executed");
  const passed = counter(counters, "passed");
  const failed = counter(counters, "failed");
  const technicalFailures = ["error", "timeout", "aborted", "disconnected"]
    .reduce((sum, name) => sum + counter(counters, name), 0);
  if (total <= 0 || executed <= 0 || executed > total || passed + failed > executed) {
    throw new Error("TRX execution counters are inconsistent or prove no executed test.");
  }
  if (summary.outcome !== "Completed" && summary.outcome !== "Failed") {
    throw new Error(`TRX outcome '${summary.outcome ?? "missing"}' is incomplete.`);
  }
  if (technicalFailures > 0) throw new Error("TRX records a technical test-host failure.");
  if (coverageDirectory !== "-") validateCoverage(coverageDirectory);

  if (rawExitCode === 0 && failed === 0) return 0;
  if (rawExitCode !== 0 && failed > 0 && summary.outcome === "Failed") return 1;
  throw new Error("dotnet test exit code and TRX counters are inconsistent.");
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const [rawExitText, trxPath, coverageDirectory = "-"] = process.argv.slice(2);
  const rawExitCode = Number(rawExitText);
  process.stdout.write(String(classifyDotnetTestResult(rawExitCode, trxPath, coverageDirectory)));
}
