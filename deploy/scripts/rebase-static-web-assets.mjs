// SPDX-License-Identifier: EUPL-1.2
import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";

const backendMarker = "/src/Aetheus.Back/";

export function rebaseStaticWebAssets(sourceRoot) {
  const resolvedSourceRoot = path.resolve(sourceRoot);
  const manifestPath = path.join(
    resolvedSourceRoot,
    "tests",
    "Aetheus.Back.IntegrationTests",
    "bin",
    "Release",
    "net10.0",
    "Aetheus.Back.staticwebassets.runtime.json");

  const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
  if (!Array.isArray(manifest.ContentRoots) || manifest.ContentRoots.length === 0) {
    throw new Error(`Static web assets manifest has no content roots: ${manifestPath}`);
  }

  manifest.ContentRoots = manifest.ContentRoots.map(contentRoot => {
    const normalized = contentRoot.replaceAll("\\", "/");
    const markerIndex = normalized.toLowerCase().lastIndexOf(backendMarker.toLowerCase());
    if (markerIndex < 0) {
      throw new Error(`Unexpected static web assets content root: ${contentRoot}`);
    }

    const relativePath = normalized.slice(markerIndex + 1);
    const rebasedRoot = path.resolve(resolvedSourceRoot, ...relativePath.split("/"));
    const sourcePrefix = `${resolvedSourceRoot}${path.sep}`;
    if (!rebasedRoot.startsWith(sourcePrefix)) {
      throw new Error(`Static web assets content root escapes the restored source: ${contentRoot}`);
    }

    fs.mkdirSync(rebasedRoot, { recursive: true });
    return `${rebasedRoot}${path.sep}`;
  });

  fs.writeFileSync(manifestPath, JSON.stringify(manifest));
  return { manifestPath, contentRoots: manifest.ContentRoots };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const sourceRoot = process.argv[2];
  if (!sourceRoot) {
    throw new Error("source root is required");
  }

  const result = rebaseStaticWebAssets(sourceRoot);
  console.log(`Rebased ${result.contentRoots.length} static web assets content root(s) in ${result.manifestPath}.`);
}
