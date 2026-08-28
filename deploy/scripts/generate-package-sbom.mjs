#!/usr/bin/env node
// SPDX-License-Identifier: EUPL-1.2
import { readFile, writeFile } from "node:fs/promises";
import { resolve } from "node:path";

const [ecosystem, inputPath, productName, version, sourceCommit, outputPath] = process.argv.slice(2);
if (!["nuget", "npm"].includes(ecosystem)
    || !inputPath
    || !productName
    || !version
    || !/^[0-9a-f]{40}([0-9a-f]{24})?$/i.test(sourceCommit ?? "")
    || !outputPath) {
  throw new Error(
    "Usage: generate-package-sbom.mjs <nuget|npm> <input> <name> <version> <source-commit> <output>");
}

const created = new Date(
  Number.parseInt(process.env.SOURCE_DATE_EPOCH ?? "0", 10) * 1000).toISOString();
if (created === "1970-01-01T00:00:00.000Z") {
  throw new Error("SOURCE_DATE_EPOCH must contain the immutable source commit timestamp.");
}

const input = JSON.parse(await readFile(resolve(inputPath), "utf8"));
const dependencies = ecosystem === "nuget"
  ? readNuGetPackages(input)
  : readNpmPackages(input);
const rootId = spdxId(productName, version);
const packages = [
  packageEntry(productName, version, rootId, ecosystem),
  ...dependencies
    .filter((item) => !(item.name === productName && item.version === version))
    .map((item) => packageEntry(item.name, item.version, spdxId(item.name, item.version), ecosystem))
];
const uniquePackages = [...new Map(packages.map((item) => [item.SPDXID, item])).values()]
  .sort((left, right) => left.SPDXID.localeCompare(right.SPDXID));

const document = {
  spdxVersion: "SPDX-2.3",
  dataLicense: "CC0-1.0",
  SPDXID: "SPDXRef-DOCUMENT",
  name: `${productName}-${version}`,
  documentNamespace:
    `https://aetheus.local/sbom/${encodeURIComponent(productName)}/${encodeURIComponent(version)}/${sourceCommit}`,
  creationInfo: {
    created,
    creators: ["Tool: Aetheus deterministic package SBOM generator 1.0"]
  },
  packages: uniquePackages,
  relationships: [
    {
      spdxElementId: "SPDXRef-DOCUMENT",
      relationshipType: "DESCRIBES",
      relatedSpdxElement: rootId
    },
    ...uniquePackages
      .filter((item) => item.SPDXID !== rootId)
      .map((item) => ({
        spdxElementId: rootId,
        relationshipType: "DEPENDS_ON",
        relatedSpdxElement: item.SPDXID
      }))
  ]
};

await writeFile(resolve(outputPath), `${JSON.stringify(document, null, 2)}\n`, "utf8");

function readNuGetPackages(value) {
  const packagesByKey = new Map();
  for (const project of value.projects ?? []) {
    for (const framework of project.frameworks ?? []) {
      for (const item of [...framework.topLevelPackages ?? [], ...framework.transitivePackages ?? []]) {
        const versionValue = item.resolvedVersion ?? item.requestedVersion;
        if (item.id && versionValue) {
          packagesByKey.set(
            `${item.id.toLowerCase()}@${versionValue}`,
            { name: item.id, version: versionValue });
        }
      }
    }
  }
  return [...packagesByKey.values()];
}

function readNpmPackages(value) {
  return Object.entries({
    ...value.dependencies,
    ...value.optionalDependencies,
    ...value.peerDependencies
  }).map(([name, dependencyVersion]) => ({
    name,
    version: String(dependencyVersion).replace(/^[~^]/, "")
  }));
}

function packageEntry(name, packageVersion, id, packageEcosystem) {
  const purlType = packageEcosystem === "nuget" ? "nuget" : "npm";
  return {
    name,
    SPDXID: id,
    versionInfo: packageVersion,
    downloadLocation: "NOASSERTION",
    filesAnalyzed: false,
    licenseConcluded: "NOASSERTION",
    licenseDeclared: "NOASSERTION",
    externalRefs: [
      {
        referenceCategory: "PACKAGE-MANAGER",
        referenceType: "purl",
        referenceLocator: `pkg:${purlType}/${encodeURIComponent(name)}@${encodeURIComponent(packageVersion)}`
      }
    ]
  };
}

function spdxId(name, packageVersion) {
  const safe = `${name}-${packageVersion}`.replace(/[^A-Za-z0-9.-]/g, "-");
  return `SPDXRef-Package-${safe}`;
}
