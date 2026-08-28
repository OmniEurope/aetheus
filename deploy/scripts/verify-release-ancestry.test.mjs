// SPDX-License-Identifier: EUPL-1.2
//
// Behavioural tests for the gate that refuses a deployment whose release the deploy branch does not
// contain. The pipeline test only asserts that the gate is invoked; these assert what it decides.
// Every case builds a throwaway repository, so nothing here touches the working tree.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync, mkdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const script = join(dirname(fileURLToPath(import.meta.url)), 'verify-release-ancestry.sh');

function git(cwd, ...args) {
    return execFileSync('git', args, { cwd, encoding: 'utf8' }).trim();
}

/** A repository with one commit on the deploy branch and one commit that was never merged into it. */
function buildRepository() {
    const root = mkdtempSync(join(tmpdir(), 'ancestry-gate-'));
    git(root, 'init', '--quiet', '--initial-branch', 'main');
    git(root, 'config', 'user.email', 'test@example.invalid');
    git(root, 'config', 'user.name', 'Ancestry Gate Test');
    writeFileSync(join(root, 'a.txt'), 'a');
    git(root, 'add', '.');
    git(root, 'commit', '--quiet', '-m', 'on the deploy branch');
    const onBranch = git(root, 'rev-parse', 'HEAD');

    git(root, 'checkout', '--quiet', '-b', 'unmerged');
    writeFileSync(join(root, 'b.txt'), 'b');
    git(root, 'add', '.');
    git(root, 'commit', '--quiet', '-m', 'never promoted');
    const offBranch = git(root, 'rev-parse', 'HEAD');
    git(root, 'checkout', '--quiet', 'main');

    return { root, onBranch, offBranch };
}

function runGate(root, contents, { write = true } = {}) {
    const provenanceDir = join(root, '.pipeline-artifacts');
    mkdirSync(provenanceDir, { recursive: true });
    const provenance = join(provenanceDir, 'source-commit');
    if (write) writeFileSync(provenance, contents);
    const result = spawnSync('sh', [script, provenance, 'HEAD'], { cwd: root, encoding: 'utf8' });
    return { code: result.status, stderr: result.stderr ?? '', stdout: result.stdout ?? '' };
}

test('accepts a release the deploy branch contains', () => {
    const repo = buildRepository();
    try {
        const { code, stdout } = runGate(repo.root, `${repo.onBranch}\n`);
        assert.equal(code, 0, 'a promoted release must deploy');
        assert.match(stdout, /contains release commit/);
    } finally {
        rmSync(repo.root, { recursive: true, force: true });
    }
});

test('refuses a release the deploy branch never received', () => {
    const repo = buildRepository();
    try {
        // This is the deploy 1995 shape: a real, valid commit that main simply does not carry.
        const { code, stderr } = runGate(repo.root, `${repo.offBranch}\n`);
        assert.equal(code, 1, 'an unpromoted release must not deploy');
        assert.match(stderr, /does not contain this release/);
        assert.match(stderr, new RegExp(repo.offBranch));
    } finally {
        rmSync(repo.root, { recursive: true, force: true });
    }
});

test('refuses an abbreviated commit id rather than guessing which commit it means', () => {
    const repo = buildRepository();
    try {
        const { code, stderr } = runGate(repo.root, `${repo.onBranch.slice(0, 8)}\n`);
        assert.equal(code, 1);
        assert.match(stderr, /not a full commit id/);
    } finally {
        rmSync(repo.root, { recursive: true, force: true });
    }
});

test('refuses a provenance file that is missing, empty, or not a commit id', () => {
    const repo = buildRepository();
    try {
        assert.equal(runGate(repo.root, '', { write: false }).code, 1, 'missing file must refuse');
        assert.equal(runGate(repo.root, '\n').code, 1, 'empty file must refuse');
        assert.equal(runGate(repo.root, 'not-a-sha\n').code, 1, 'non-hex content must refuse');
    } finally {
        rmSync(repo.root, { recursive: true, force: true });
    }
});

test('refuses a commit this checkout has never heard of instead of claiming it was not promoted', () => {
    const repo = buildRepository();
    try {
        const unknown = 'a'.repeat(40);
        const { code, stderr } = runGate(repo.root, `${unknown}\n`);
        assert.equal(code, 1);
        // The distinction matters: telling an operator to promote a commit that is already promoted
        // sends them chasing the wrong fix.
        assert.match(stderr, /cannot be shown to contain it/);
    } finally {
        rmSync(repo.root, { recursive: true, force: true });
    }
});
