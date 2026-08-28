#!/usr/bin/env node
// SPDX-License-Identifier: EUPL-1.2
import { createHash } from "node:crypto";
import { createReadStream } from "node:fs";
import { readFile, stat, writeFile } from "node:fs/promises";
import { basename } from "node:path";

const [mode, expectedSource, evidencePath, ...artifactPaths] = process.argv.slice(2);
if (!['create', 'verify'].includes(mode)
    || !/^[0-9a-f]{40}$/i.test(expectedSource ?? '')
    || !evidencePath
    || artifactPaths.length === 0) {
  throw new Error(
    'Usage: nightly-evidence.mjs <create|verify> <source-sha> <evidence> <artifact>...');
}

const digestFile = async path => {
  const hash = createHash('sha256');
  for await (const chunk of createReadStream(path)) hash.update(chunk);
  return hash.digest('hex');
};
const describeArtifacts = async paths => {
  const entries = [];
  const names = new Set();
  for (const path of paths) {
    const name = basename(path);
    if (names.has(name))
      throw new Error(`Duplicate nightly artifact name: ${name}`);
    names.add(name);
    const metadata = await stat(path);
    if (!metadata.isFile() || metadata.size === 0)
      throw new Error(`Nightly artifact is empty: ${name}`);
    entries.push({ name, bytes: metadata.size, sha256: await digestFile(path) });
  }
  return entries.sort((left, right) => left.name.localeCompare(right.name));
};

const artifacts = await describeArtifacts(artifactPaths);
if (mode === 'create') {
  const evidence = {
    schema: 1,
    kind: 'nightly-demo-payload',
    sourceSha: expectedSource.toLowerCase(),
    artifacts
  };
  await writeFile(evidencePath, `${JSON.stringify(evidence, null, 2)}\n`, { mode: 0o644 });
  process.stdout.write(`Created independent nightly evidence for ${evidence.sourceSha}.\n`);
} else {
  const evidence = JSON.parse(await readFile(evidencePath, 'utf8'));
  if (evidence.schema !== 1 || evidence.kind !== 'nightly-demo-payload')
    throw new Error('The nightly evidence schema or kind is invalid.');
  if (evidence.sourceSha !== expectedSource.toLowerCase())
    throw new Error('The nightly evidence belongs to another source commit.');
  if (JSON.stringify(evidence.artifacts) !== JSON.stringify(artifacts))
    throw new Error('The nightly payload differs from its independent evidence.');
  process.stdout.write(`Verified independent nightly evidence for ${evidence.sourceSha}.\n`);
}
