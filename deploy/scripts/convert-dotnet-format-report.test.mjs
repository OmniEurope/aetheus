// SPDX-License-Identifier: EUPL-1.2
import assert from "node:assert/strict";
import test from "node:test";
import { convertDotnetFormatReport } from "./convert-dotnet-format-report.mjs";

test("converts real formatting findings to SARIF", () => {
  const sarif = convertDotnetFormatReport([{
    FileName: "src/Example.cs",
    FileChanges: [{ LineNumber: 4, CharNumber: 2, DiagnosticId: "WHITESPACE", FormatDescription: "Fix whitespace." }]
  }]);
  const finding = sarif.runs[0].results[0];
  assert.equal(finding.ruleId, "WHITESPACE");
  assert.equal(finding.locations[0].physicalLocation.artifactLocation.uri, "src/Example.cs");
});

test("preserves an honest empty report", () => {
  const sarif = convertDotnetFormatReport([]);
  assert.deepEqual(sarif.runs[0].results, []);
});

test("rejects malformed producer output", () => {
  assert.throws(() => convertDotnetFormatReport({}), /JSON array/);
});
