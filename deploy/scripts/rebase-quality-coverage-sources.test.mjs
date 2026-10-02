// SPDX-License-Identifier: EUPL-1.2
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { rebaseQualityCoverageSources } from "./rebase-quality-coverage-sources.mjs";

function workspaceWith(reports) {
  const workspace = fs.mkdtempSync(path.join(os.tmpdir(), "aetheus-coverage-rebase-"));
  fs.mkdirSync(path.join(workspace, "src"));
  for (const [name, source] of Object.entries(reports)) {
    const directory = path.join(workspace, "coverage", name);
    fs.mkdirSync(directory, { recursive: true });
    fs.writeFileSync(
      path.join(directory, "coverage.cobertura.xml"),
      `<coverage><sources><source>${source}</source></sources></coverage>`);
  }
  return workspace;
}

function rebasedSource(workspace, output, name) {
  const xml = fs.readFileSync(path.join(output, name, "coverage.cobertura.xml"), "utf8");
  return xml.match(/<source>([^<]*)<\/source>/)[1];
}

test("rebases a src root and a single-project root onto this workspace's src", () => {
  const workspace = workspaceWith({
    backend: "/var/lib/aetheus-agent/w/0632e383/s/src/",
    agent: "/var/lib/aetheus-agent/w/0632e383/s/src/Aetheus.Agent.Core/"
  });
  const output = path.join(workspace, "rebased");
  try {
    rebaseQualityCoverageSources(workspace, output);
    const src = path.join(workspace, "src").replaceAll("\\", "/");
    assert.equal(rebasedSource(workspace, output, "backend"), `${src}/`);
    assert.equal(rebasedSource(workspace, output, "agent"), `${src}/Aetheus.Agent.Core/`);
  } finally {
    fs.rmSync(workspace, { recursive: true, force: true });
  }
});

test("refuses a source root outside any src directory", () => {
  const workspace = workspaceWith({ agent: "/var/lib/aetheus-agent/w/0632e383/s/tests/" });
  try {
    assert.throws(
      () => rebaseQualityCoverageSources(workspace, path.join(workspace, "rebased")),
      /Unexpected Cobertura source root/);
  } finally {
    fs.rmSync(workspace, { recursive: true, force: true });
  }
});
