import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../../..");

test("generates a deterministic SPDX document from NuGet dependency JSON", () => {
  const directory = mkdtempSync(join(tmpdir(), "aetheus-sbom-"));
  try {
    const input = join(directory, "dependencies.json");
    const output = join(directory, "package.spdx.json");
    writeFileSync(input, JSON.stringify({
      projects: [{
        frameworks: [{
          topLevelPackages: [{
            id: "OpenTelemetry",
            resolvedVersion: "1.17.0"
          }],
          transitivePackages: []
        }]
      }]
    }));
    execFileSync(process.execPath, [
      resolve(repositoryRoot, "deploy/scripts/generate-package-sbom.mjs"),
      "nuget",
      input,
      "Aetheus.Telemetry",
      "0.1.0",
      "0123456789abcdef0123456789abcdef01234567",
      output
    ], {
      env: { ...process.env, SOURCE_DATE_EPOCH: "1784793600" }
    });

    const document = JSON.parse(readFileSync(output, "utf8"));
    assert.equal(document.spdxVersion, "SPDX-2.3");
    assert.equal(document.creationInfo.created, "2026-07-23T08:00:00.000Z");
    assert.equal(document.packages.length, 2);
    assert.equal(
      document.relationships.some((item) => item.relationshipType === "DEPENDS_ON"),
      true);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});
