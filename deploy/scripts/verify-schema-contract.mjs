#!/usr/bin/env node
// SPDX-License-Identifier: EUPL-1.2
// Proves the schema states a delivery contract declares.
//
// The contract derives schemaAfter from the migration files in the source tree, and inherits
// schemaBefore from the contract currently deployed. Neither is evidence on its own. QA runs V-1,
// then migrates to V, reading the real migration history at both points; this compares those two
// readings to what the contract claims. A mismatch means the contract describes a schema path the
// code does not actually produce, and the candidate must not be sealed.
import { readFile } from "node:fs/promises";
import { fingerprint, migrationsFromHistory } from "./schema-fingerprint.mjs";

const [contractPath, beforePath, afterPath] = process.argv.slice(2);
if (!contractPath || !afterPath)
  throw new Error(
    "Usage: verify-schema-contract.mjs <delivery-contract> <schema-before|-> <schema-after>");

const contract = JSON.parse(await readFile(contractPath, "utf8"));
const baseline = contract.baseline ?? {};
const describe = state => `${state.hash} (${state.migrations.length} migrations)`;
const readState = async path =>
  fingerprint(migrationsFromHistory(await readFile(path, "utf8")));

const observedAfter = await readState(afterPath);
if (observedAfter.hash !== baseline.schemaAfter?.hash)
  throw new Error(
    "The schema produced by migrating to V is not the one the contract declares.\n"
    + `  observed : ${describe(observedAfter)}\n`
    + `  declared : ${describe(baseline.schemaAfter ?? { hash: "none", migrations: [] })}`);

if (baseline.bootstrap === true) {
  process.stdout.write(
    `Bootstrap: schemaAfter proven against a live database ${describe(observedAfter)}.\n`);
} else {
  if (beforePath === "-")
    throw new Error("A non-bootstrap candidate must prove the schema it migrates from.");
  const observedBefore = await readState(beforePath);
  if (observedBefore.hash !== baseline.schemaBefore?.hash)
    throw new Error(
      "The schema V-1 actually runs on is not the one the contract declares as its starting state.\n"
      + `  observed : ${describe(observedBefore)}\n`
      + `  declared : ${describe(baseline.schemaBefore ?? { hash: "none", migrations: [] })}\n`
      + "Production has moved since this candidate started; requalify it.");
  process.stdout.write(
    `Schema path proven on a live database: ${describe(observedBefore)} -> ${describe(observedAfter)}.\n`);
}
