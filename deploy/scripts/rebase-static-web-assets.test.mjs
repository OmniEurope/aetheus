// SPDX-License-Identifier: EUPL-1.2
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { rebaseStaticWebAssets } from "./rebase-static-web-assets.mjs";

test("rebases restored static web asset roots and creates their directories", () => {
  const sourceRoot = fs.mkdtempSync(path.join(os.tmpdir(), "aetheus-static-assets-"));
  try {
    const manifestDirectory = path.join(
      sourceRoot,
      "tests",
      "Aetheus.Back.IntegrationTests",
      "bin",
      "Release",
      "net10.0");
    fs.mkdirSync(manifestDirectory, { recursive: true });
    const manifestPath = path.join(manifestDirectory, "Aetheus.Back.staticwebassets.runtime.json");
    fs.writeFileSync(manifestPath, JSON.stringify({
      ContentRoots: [
        "/tmp/ci-source/src/Aetheus.Back/wwwroot/",
        "/tmp/ci-source/src/Aetheus.Back/obj/Release/net10.0/compressed/"
      ],
      Root: { Children: null, Asset: null, Patterns: null }
    }));

    const result = rebaseStaticWebAssets(sourceRoot);
    const expectedRoots = [
      path.join(sourceRoot, "src", "Aetheus.Back", "wwwroot") + path.sep,
      path.join(sourceRoot, "src", "Aetheus.Back", "obj", "Release", "net10.0", "compressed") + path.sep
    ];

    assert.deepEqual(result.contentRoots, expectedRoots);
    assert.deepEqual(JSON.parse(fs.readFileSync(manifestPath, "utf8")).ContentRoots, expectedRoots);
    assert.ok(expectedRoots.every(root => fs.statSync(root).isDirectory()));
  } finally {
    fs.rmSync(sourceRoot, { recursive: true, force: true });
  }
});

test("rejects a content root outside the backend source tree", () => {
  const sourceRoot = fs.mkdtempSync(path.join(os.tmpdir(), "aetheus-static-assets-"));
  try {
    const manifestDirectory = path.join(
      sourceRoot,
      "tests",
      "Aetheus.Back.IntegrationTests",
      "bin",
      "Release",
      "net10.0");
    fs.mkdirSync(manifestDirectory, { recursive: true });
    fs.writeFileSync(
      path.join(manifestDirectory, "Aetheus.Back.staticwebassets.runtime.json"),
      JSON.stringify({ ContentRoots: ["/tmp/unexpected/content/"] }));

    assert.throws(() => rebaseStaticWebAssets(sourceRoot), /Unexpected static web assets content root/);
  } finally {
    fs.rmSync(sourceRoot, { recursive: true, force: true });
  }
});
