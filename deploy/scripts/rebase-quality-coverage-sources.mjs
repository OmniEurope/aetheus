// SPDX-License-Identifier: EUPL-1.2
import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";

function findReports(directory) {
  if (!fs.existsSync(directory)) return [];
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const file = path.join(directory, entry.name);
    if (entry.isDirectory()) return findReports(file);
    return entry.isFile() && entry.name === "coverage.cobertura.xml" ? [file] : [];
  });
}

function xmlEscape(value) {
  return value.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;");
}

export function rebaseQualityCoverageSources(workspace, outputDirectory) {
  const sourceDirectory = path.resolve(workspace, "src");
  if (!fs.statSync(sourceDirectory).isDirectory()) {
    throw new Error(`Source directory is unavailable: ${sourceDirectory}`);
  }

  const coverageDirectory = path.resolve(workspace, "coverage");
  const reports = findReports(coverageDirectory).sort();
  if (reports.length === 0) throw new Error("No raw Cobertura report was found under coverage/.");

  const sourceRoot = sourceDirectory.replaceAll("\\", "/");
  const rebasedReports = reports.map(report => {
    const original = fs.readFileSync(report, "utf8");
    let sourceCount = 0;
    const rebased = original.replace(/<source>([^<]*)<\/source>/g, (_, source) => {
      // Coverlet writes the deepest directory common to the covered files: src/ when a suite covers
      // several projects, src/<Project>/ when it covers one (the agent suite, run 2420). Only the part
      // below the CI workspace's src/ is kept.
      const normalized = source.replaceAll("\\", "/").replace(/\/?$/, "/");
      const srcIndex = normalized.lastIndexOf("/src/");
      if (!/^\/|^[A-Za-z]:\//.test(normalized) || srcIndex < 0) {
        throw new Error(`Unexpected Cobertura source root in ${report}: ${source}`);
      }
      sourceCount++;
      return `<source>${xmlEscape(sourceRoot + normalized.slice(srcIndex + "/src".length))}</source>`;
    });
    if (sourceCount === 0) throw new Error(`Cobertura report has no source root: ${report}`);

    const destination = path.join(outputDirectory, path.relative(coverageDirectory, report));
    fs.mkdirSync(path.dirname(destination), { recursive: true });
    fs.writeFileSync(destination, rebased);
    return destination;
  });

  if (rebasedReports.some(report => report.includes(";"))) {
    throw new Error("Report path contains a semicolon and cannot be passed to ReportGenerator.");
  }
  return rebasedReports.join(";");
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const [, , workspace, outputDirectory] = process.argv;
  if (!workspace || !outputDirectory) throw new Error("Workspace and output directory are required.");
  console.log(rebaseQualityCoverageSources(workspace, outputDirectory));
}
