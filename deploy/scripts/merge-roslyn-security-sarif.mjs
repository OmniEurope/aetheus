// SPDX-License-Identifier: EUPL-1.2
// Merges the per-project Roslyn SARIF reports into one security report the pipeline publishes.
//
// Two things it does, and both are the point:
//
// 1. It keeps ONLY the security rules. A solution-wide build emits well over a thousand analyzer
//    results, almost all of them style and performance advice. Publishing those as findings buries
//    the twenty that concern security, and a report nobody can read is a report nobody reads. The
//    families kept are the .NET security category (CA2100/CA21xx injection and P/Invoke marshaling,
//    CA23xx unsafe deserialization, CA3xxx XML/XSS/SQL injection, CA5xxx weak crypto, disabled
//    certificate validation, insecure TLS) plus this repository's own SEC analyzers.
//
// 2. It drops results already suppressed in source. The backend parser does this too, so this is
//    belt and braces, but it also keeps the published artifact honest: what it contains is what is
//    still open, not a list where half the entries were already answered.
//
// Usage: node merge-roslyn-security-sarif.mjs <input directory> <output file>
import { readdirSync, readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { pathToFileURL } from "node:url";

/// A rule id belongs to the security surface. Deliberately a whitelist of families rather than a
/// tag lookup: SARIF from Roslyn carries no category tag on the rule, so the id is the only signal.
export function isSecurityRule(ruleId) {
  if (typeof ruleId !== "string") return false;
  return /^SEC[0-9]+$/.test(ruleId)
    || /^CA21[0-9][0-9]$/.test(ruleId)
    || /^CA23[0-9][0-9]$/.test(ruleId)
    || /^CA3[0-9][0-9][0-9]$/.test(ruleId)
    || /^CA5[0-9][0-9][0-9]$/.test(ruleId);
}

/// SARIF 2.1.0 §3.27.23. An EMPTY suppressions array is the tool saying it found none, which is the
/// opposite of a suppression; only `accepted` or an absent state counts.
export function isSuppressed(result) {
  const suppressions = result?.suppressions;
  if (!Array.isArray(suppressions) || suppressions.length === 0) return false;
  return suppressions.some(suppression =>
    suppression?.state === undefined
    || String(suppression.state).toLowerCase() === "accepted");
}

export function mergeSecurityFindings(reports) {
  const results = [];
  const rules = new Map();

  for (const report of reports) {
    for (const run of report?.runs ?? []) {
      const declared = run?.tool?.driver?.rules ?? [];
      for (const result of run?.results ?? []) {
        if (!isSecurityRule(result?.ruleId) || isSuppressed(result)) continue;
        // ruleIndex points into THIS run's rule table, which is meaningless once merged; carry the
        // rule itself across instead and drop the index rather than leave it pointing elsewhere.
        const rule = declared[result.ruleIndex];
        if (rule?.id && !rules.has(rule.id)) rules.set(rule.id, rule);
        const { ruleIndex, ...rest } = result;
        results.push(rest);
      }
    }
  }

  return {
    $schema: "https://json.schemastore.org/sarif-2.1.0.json",
    version: "2.1.0",
    runs: [{
      tool: {
        driver: {
          name: "Roslyn security analyzers",
          informationUri: "https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/security-warnings",
          rules: [...rules.values()]
        }
      },
      results
    }]
  };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const [inputDirectory, output] = process.argv.slice(2);
  if (!inputDirectory || !output) {
    console.error("Usage: node merge-roslyn-security-sarif.mjs <input directory> <output file>");
    process.exit(2);
  }

  const files = readdirSync(inputDirectory).filter(name => name.endsWith(".sarif"));
  // No input at all means the build did not run with -p:AetheusRoslynSarif=true. Publishing an empty
  // report would then assert "no security findings" about an analysis nobody performed.
  if (files.length === 0) {
    console.error(`No SARIF report found in ${inputDirectory}; the build did not produce any.`);
    process.exit(1);
  }

  const reports = files.map(name =>
    JSON.parse(readFileSync(join(inputDirectory, name), "utf8")));
  const merged = mergeSecurityFindings(reports);
  mkdirSync(dirname(output), { recursive: true });
  writeFileSync(output, `${JSON.stringify(merged)}\n`);
  process.stdout.write(
    `${merged.runs[0].results.length} open security findings from ${files.length} projects\n`);
}
