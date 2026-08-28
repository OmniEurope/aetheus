#!/usr/bin/env node
// SPDX-License-Identifier: EUPL-1.2
import { createHash } from "node:crypto";
import { readFile } from "node:fs/promises";
import { join } from "node:path";
import { fingerprint, migrationsFromHistory } from "./schema-fingerprint.mjs";

const [candidatePath, productionSchemaPath, artifactDirectory] = process.argv.slice(2);
if (!candidatePath || !productionSchemaPath || !artifactDirectory)
  throw new Error(
    "Usage: verify-delivery-promotion.mjs <candidate-contract> <production-schema|-> <artifact-directory>");

const sha256 = value => createHash("sha256").update(value).digest("hex");
const canonicalize = value => {
  if (Array.isArray(value))
    return value.map(canonicalize);
  if (value && typeof value === "object")
    return Object.fromEntries(
      Object.entries(value)
        .sort(([left], [right]) => left.localeCompare(right))
        .map(([key, item]) => [key, canonicalize(item)]));
  return value;
};

const candidateBytes = await readFile(candidatePath);
const candidate = JSON.parse(candidateBytes);
// Schema 1 sealed the identity of the production release instead of the schema states it was proven
// against. Such a contract carries no fingerprints, so it cannot be checked under this gate at all;
// accepting it silently would mean promoting a delivery nothing has verified.
if (candidate.schema === 1)
  throw new Error(
    "This candidate was sealed under the old identity-based contract (schema 1) and carries no "
    + "schema fingerprints. Requalify it to obtain a schema-sealed contract.");
if (candidate.schema !== 2 || !candidate.candidateId)
  throw new Error("The candidate delivery contract is invalid.");
const { candidateId, candidateVersion, ...identity } = candidate;
const calculatedId = sha256(JSON.stringify(canonicalize(identity)));
if (candidateId !== calculatedId)
  throw new Error("The candidate delivery contract identity is invalid.");
const expectedSourceVersion = `c-${candidate.sourceSha}`;
if (candidateVersion !== expectedSourceVersion
    && candidateVersion !== `${expectedSourceVersion}-${candidateId.slice(0, 12)}`)
  throw new Error("The candidate release version does not match its immutable identity.");

for (const [name, expected] of Object.entries(candidate.artifacts ?? {})) {
  const actual = sha256(await readFile(join(artifactDirectory, name)));
  if (actual !== expected)
    throw new Error(`Candidate artifact '${name}' differs from the validated digest.`);
}

// The gate: production must stand on one of the two schema states this delivery was proven against.
//
//   schemaBefore - the state the forward migration was actually run from; a normal migrating deploy.
//   schemaAfter  - the state that migration produces, on which both V and V-1 binaries were exercised;
//                  the migration is a no-op and only binaries move. This covers redeploying the
//                  current release, retrying after a switch, deploying across releases that carry no
//                  migration, and rolling back to any release whose schemaAfter is the live state.
//
// Anything else means crossing a migration path no QA ever ran, which stays refused.
const { baseline } = candidate;
const describe = state => `${state.hash} (${state.migrations.length} migrations)`;

if (baseline?.bootstrap === true) {
  // A first release has no prior state: either the database is empty, or it already carries exactly
  // what this delivery produces (a retried bootstrap).
  if (productionSchemaPath === "-")
    process.stdout.write("Bootstrap delivery onto a host with no database yet.\n");
  else {
    const live = fingerprint(migrationsFromHistory(await readFile(productionSchemaPath, "utf8")));
    if (live.migrations.length !== 0 && live.hash !== baseline.schemaAfter.hash)
      throw new Error(
        "A bootstrap candidate cannot replace an existing schema.\n"
        + `  live         : ${describe(live)}\n`
        + `  schemaAfter  : ${describe(baseline.schemaAfter)}`);
  }
} else {
  if (productionSchemaPath === "-")
    throw new Error("The production schema state required to verify this candidate is missing.");
  const live = fingerprint(migrationsFromHistory(await readFile(productionSchemaPath, "utf8")));
  if (live.hash === baseline.schemaBefore.hash)
    process.stdout.write(
      `Production stands on the proven pre-migration schema ${describe(live)}; migrating deploy.\n`);
  else if (live.hash === baseline.schemaAfter.hash)
    process.stdout.write(
      `Production already stands on the proven post-migration schema ${describe(live)}; `
      + "the migration is a no-op and only the binaries move.\n");
  else
    throw new Error(
      "The production schema is neither state this candidate was proven against.\n"
      + `  live         : ${describe(live)}\n`
      + `  schemaBefore : ${describe(baseline.schemaBefore)}\n`
      + `  schemaAfter  : ${describe(baseline.schemaAfter)}\n`
      + "Requalify the candidate against the current production schema.");
}

process.stdout.write(
  `Verified immutable candidate ${candidate.candidateVersion} (${candidate.candidateId}).\n`);
