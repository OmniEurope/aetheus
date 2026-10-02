// SPDX-License-Identifier: EUPL-1.2
import assert from "node:assert/strict";
import test from "node:test";
import {
  isSecurityRule,
  isSuppressed,
  mergeSecurityFindings
} from "./merge-roslyn-security-sarif.mjs";

function result(ruleId, extra = {}) {
  return { ruleId, message: { text: `${ruleId} finding` }, ...extra };
}

function report(results, rules = []) {
  return { version: "2.1.0", runs: [{ tool: { driver: { name: "x", rules } }, results }] };
}

test("keeps the security families and nothing else", () => {
  for (const id of ["CA2100", "CA2101", "CA2300", "CA3001", "CA5359", "CA5392", "SEC004", "SEC1"])
    assert.equal(isSecurityRule(id), true, id);
  // Style, performance and compiler diagnostics are the thousand-odd results this filter exists to
  // keep out of a security report.
  for (const id of ["CA1859", "CA1816", "SYSLIB1045", "CS0164", "PRM004", "CA1068", ""])
    assert.equal(isSecurityRule(id), false, id);
});

test("an empty suppressions array is not a suppression", () => {
  // SARIF distinguishes "the tool looked and found none" from "this was suppressed"; reading the
  // first as the second would silently drop findings from tools that always emit the field.
  assert.equal(isSuppressed({ suppressions: [] }), false);
  assert.equal(isSuppressed({}), false);
});

test("a suppression in source, or accepted, is honoured", () => {
  assert.equal(isSuppressed({ suppressions: [{ kind: "inSource" }] }), true);
  assert.equal(isSuppressed({ suppressions: [{ state: "accepted" }] }), true);
});

test("a rejected or unreviewed suppression leaves the finding open", () => {
  assert.equal(isSuppressed({ suppressions: [{ state: "rejected" }] }), false);
  assert.equal(isSuppressed({ suppressions: [{ state: "underReview" }] }), false);
});

test("merges only open security findings across projects", () => {
  const merged = mergeSecurityFindings([
    report([result("CA5359"), result("CA1859"), result("CS0164")]),
    report([
      result("CA2100"),
      result("SEC004", { suppressions: [{ kind: "inSource", justification: "documented" }] })
    ])
  ]);

  assert.deepEqual(merged.runs[0].results.map(r => r.ruleId), ["CA5359", "CA2100"]);
  assert.equal(merged.version, "2.1.0");
});

test("carries each rule's definition across and drops the stale index", () => {
  // ruleIndex points into the run it came from. Kept as-is after merging, it would describe a
  // different rule; the rule object travels with the finding instead.
  const merged = mergeSecurityFindings([
    report([result("CA5359", { ruleIndex: 0 })], [{ id: "CA5359", helpUri: "https://example.test" }])
  ]);

  const [finding] = merged.runs[0].results;
  assert.equal(finding.ruleIndex, undefined);
  assert.deepEqual(merged.runs[0].tool.driver.rules, [{ id: "CA5359", helpUri: "https://example.test" }]);
});

test("declares a rule once even when several projects report it", () => {
  const merged = mergeSecurityFindings([
    report([result("CA5392", { ruleIndex: 0 })], [{ id: "CA5392" }]),
    report([result("CA5392", { ruleIndex: 0 })], [{ id: "CA5392" }])
  ]);

  assert.equal(merged.runs[0].results.length, 2);
  assert.equal(merged.runs[0].tool.driver.rules.length, 1);
});

test("a clean solution produces a valid empty report, not a missing one", () => {
  const merged = mergeSecurityFindings([report([result("CA1859")])]);

  assert.deepEqual(merged.runs[0].results, []);
  assert.equal(merged.runs[0].tool.driver.name, "Roslyn security analyzers");
});

test("tolerates a report with no runs or no results", () => {
  assert.deepEqual(mergeSecurityFindings([{}, { runs: [] }, { runs: [{}] }]).runs[0].results, []);
});
