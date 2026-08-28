// SPDX-License-Identifier: EUPL-1.2
import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import test from 'node:test';

const script = fileURLToPath(new URL('./nightly-evidence.mjs', import.meta.url));
const source = '0123456789abcdef0123456789abcdef01234567';

const run = (...args) => spawnSync(process.execPath, [script, ...args], { encoding: 'utf8' });

test('creates deterministic evidence and rejects altered bytes or source', async () => {
  const root = await mkdtemp(join(tmpdir(), 'aetheus-nightly-evidence-'));
  try {
    const first = join(root, 'a.bin');
    const second = join(root, 'b.bin');
    const evidence = join(root, 'evidence.json');
    await writeFile(first, 'first');
    await writeFile(second, 'second');

    assert.equal(run('create', source, evidence, second, first).status, 0);
    const parsed = JSON.parse(await readFile(evidence, 'utf8'));
    assert.equal(parsed.kind, 'nightly-demo-payload');
    assert.equal(parsed.sourceSha, source);
    assert.deepEqual(parsed.artifacts.map(item => item.name), ['a.bin', 'b.bin']);
    assert.equal(run('verify', source, evidence, first, second).status, 0);

    await writeFile(first, 'altered');
    assert.notEqual(run('verify', source, evidence, first, second).status, 0);
    assert.notEqual(run('verify', 'f'.repeat(40), evidence, first, second).status, 0);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});
