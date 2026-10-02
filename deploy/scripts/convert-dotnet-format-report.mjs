// SPDX-License-Identifier: EUPL-1.2
import { readFileSync, writeFileSync } from "node:fs";
import { isAbsolute, relative } from "node:path";
import { pathToFileURL } from "node:url";

export function convertDotnetFormatReport(report) {
  if (!Array.isArray(report)) throw new Error("The dotnet format report must be a JSON array.");
  const results = [];
  for (const document of report) {
    if (!document || !Array.isArray(document.FileChanges)) throw new Error("The dotnet format report contains an invalid document.");
    // FilePath is the full path; FileName is the bare file name, which cannot be opened or told apart
    // from a namesake in another folder.
    const reportedPath = document.FilePath ?? document.FileName;
    if (typeof reportedPath !== "string" || reportedPath.length === 0) throw new Error("A dotnet format finding has no file name.");
    const uri = (isAbsolute(reportedPath) ? relative(process.cwd(), reportedPath) : reportedPath).replaceAll("\\", "/");
    for (const change of document.FileChanges) {
      if (!change || typeof change !== "object") throw new Error("The dotnet format report contains an invalid finding.");
      results.push({
        ruleId: String(change.DiagnosticId ?? "DOTNET_FORMAT"),
        // Layout is never more than a low-severity finding, and one finding is one file under one rule:
        // without the path in its identity, every file sharing a rule collapsed into a single finding.
        level: "note",
        partialFingerprints: { "dotnetFormatFile/v1": uri },
        message: { text: String(change.FormatDescription ?? "dotnet format would change this source file.") },
        locations: [{ physicalLocation: {
          artifactLocation: { uri },
          region: {
            startLine: Math.max(1, Number(change.LineNumber) || 1),
            startColumn: Math.max(1, Number(change.CharNumber) || 1)
          }
        } }]
      });
    }
  }
  return {
    version: "2.1.0",
    $schema: "https://json.schemastore.org/sarif-2.1.0.json",
    runs: [{ tool: { driver: { name: "dotnet format", version: "1" } }, results }]
  };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const [input, output] = process.argv.slice(2);
  if (!input || !output) throw new Error("Usage: convert-dotnet-format-report.mjs <input> <output>");
  const sarif = convertDotnetFormatReport(JSON.parse(readFileSync(input, "utf8")));
  writeFileSync(output, `${JSON.stringify(sarif)}\n`);
  process.stdout.write(String(sarif.runs[0].results.length));
}
