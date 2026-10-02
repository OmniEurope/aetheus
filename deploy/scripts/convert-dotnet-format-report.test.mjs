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

test("reports the full path, a low severity and one identity per file", () => {
  const sarif = convertDotnetFormatReport([
    { FileName: "Example.cs", FilePath: `${process.cwd()}/src/A/Example.cs`, FileChanges: [{ LineNumber: 1, CharNumber: 1, DiagnosticId: "WHITESPACE" }] },
    { FileName: "Example.cs", FilePath: `${process.cwd()}/src/B/Example.cs`, FileChanges: [{ LineNumber: 1, CharNumber: 1, DiagnosticId: "WHITESPACE" }] }
  ]);
  const [first, second] = sarif.runs[0].results;
  assert.equal(first.locations[0].physicalLocation.artifactLocation.uri, "src/A/Example.cs");
  assert.equal(second.locations[0].physicalLocation.artifactLocation.uri, "src/B/Example.cs");
  assert.equal(first.level, "note");
  assert.notDeepEqual(first.partialFingerprints, second.partialFingerprints);
});

test("preserves an honest empty report", () => {
  const sarif = convertDotnetFormatReport([]);
  assert.deepEqual(sarif.runs[0].results, []);
});

test("rejects malformed producer output", () => {
  assert.throws(() => convertDotnetFormatReport({}), /JSON array/);
});
