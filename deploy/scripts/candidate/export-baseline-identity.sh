# SPDX-License-Identifier: EUPL-1.2
#
# Reads the delivery baseline the deployed release carries, and publishes the identity the rest of
# the candidate is judged against: the source revision it must be an ancestor of, and the migration
# set it must be compatible with. When no release is deployed, declares the bootstrap mode instead.
#
# This was 86 lines inlined in .pipeline/aetheus-candidate.yaml, including a JavaScript heredoc that
# nothing could run, lint or test. The body is relocated unchanged; the heredoc stays a heredoc here
# for now, which is honest about what this commit did rather than mixing a move with a rewrite.
#
# Inputs, all read from the environment (none positional):
#   WORKSPACE  the run's checkout (required, used through the working directory)
#
# Publishes DELIVERY_BASELINE_SOURCE_SHA, DELIVERY_BASELINE_MIGRATIONS and
# DELIVERY_BASELINE_BOOTSTRAP as run output variables.
set -eu
# The pinned, checksummed runtime, not whatever the host happens to have. Candidate run 1258
# died here on "node: command not found", exit 127, before a single child could start: the
# simulator had been rebuilt from the repository and lost the system node an earlier session
# had installed by hand. Nothing in the repository ever installed one.
NODE="$(sh deploy/scripts/ensure-node-runtime.sh)"
PATH="$(dirname "$NODE"):$PATH"
export PATH
CONTRACT=".delivery-baseline/.pipeline-artifacts/delivery-contract.json"
if test -s "$CONTRACT"; then
  node - "$CONTRACT" <<'NODE'
const fs = require("node:fs");
const crypto = require("node:crypto");
const path = process.argv[2];
const bytes = fs.readFileSync(path);
const contract = JSON.parse(bytes);
// The schema state production stands on becomes the state this candidate migrates from.
// Chaining the deployed contract's schemaAfter is what lets the candidate be qualified
// against a schema rather than against the identity of one particular release.
let deployed = contract.baseline?.schemaAfter;
if (!deployed?.hash) {
  // Transition off schema 1. Those contracts predate schema fingerprints, and production
  // cannot leave schema 1 without deploying a schema-2 candidate that cannot itself be
  // qualified: a closed loop. The old contract still names the deployed sourceSha, and the
  // migrations of that commit are in git, so the state is reconstructed rather than
  // assumed. Nothing is weakened: the deploy gate still reads the live database and
  // refuses anything that is not one of the two proven states.
  if (!/^[0-9a-f]{40}$/i.test(contract.sourceSha ?? "")) {
    console.error("The deployed contract names no source revision; nothing to reconstruct from.");
    process.exit(1);
  }
  // The pipeline checkout is shallow, so the deployed revision is usually absent from it:
  // run 1963 died on "fatal: not a tree object". Fetch just that commit before reading it.
  const git = require("node:child_process");
  try {
    git.execFileSync("git", ["cat-file", "-e", `${contract.sourceSha}^{commit}`], { stdio: "ignore" });
  } catch {
    // Fetching it here cannot work: the workspace holds no git credentials of its own, and
    // run 1966 failed on "could not read Username". Depth is a checkout concern, so it is
    // solved by AETHEUS_GIT_HISTORY_DEPTH above rather than by a fetch at this point.
    console.error(
      `The deployed revision ${contract.sourceSha} is not in this checkout, so the schema `
      + "state of the legacy contract cannot be reconstructed. Raise "
      + "AETHEUS_GIT_HISTORY_DEPTH until it reaches that revision.");
    process.exit(1);
  }
  const listed = git
    .execFileSync("git", ["ls-tree", "--name-only", contract.sourceSha, "src/Aetheus.Back/Data/Migrations/"],
      { encoding: "utf8" })
    .split("\n")
    .map(line => line.trim().split("/").pop())
    .filter(name => name.endsWith(".cs"))
    .filter(name => !name.endsWith(".Designer.cs") && !name.endsWith("ModelSnapshot.cs"))
    .map(name => name.slice(0, -3))
    .sort();
  if (listed.length === 0) {
    console.error(`No migrations found at deployed revision ${contract.sourceSha}.`);
    process.exit(1);
  }
  deployed = {
    hash: crypto.createHash("sha256").update(listed.join("\n")).digest("hex"),
    migrations: listed
  };
  console.log(
    `Reconstructed the schema state of legacy contract schema ${contract.schema} from `
    + `deployed revision ${contract.sourceSha}: ${listed.length} migrations.`);
}
if (!Array.isArray(deployed.migrations) || deployed.migrations.length === 0) {
  console.error("The production schema state could not be established.");
  process.exit(1);
}
console.log("##aetheus[setvariable name=DELIVERY_BASELINE_BOOTSTRAP]false");
// Still exported, but no longer part of the seal: CI uses it to decide whether the source
// changed since the deployed baseline, which drives cache reuse. Dropping it would quietly
// make every build look changed.
console.log(`##aetheus[setvariable name=DELIVERY_BASELINE_SOURCE_SHA]${contract.sourceSha}`);
console.log(`##aetheus[setvariable name=DELIVERY_BASELINE_SCHEMA_HASH]${deployed.hash}`);
// Base64: a setvariable directive is read line by line, so the migration list has to
// travel as a single token with no newline and no separator of its own.
console.log(
  "##aetheus[setvariable name=DELIVERY_BASELINE_SCHEMA_MIGRATIONS_B64]"
  + Buffer.from(deployed.migrations.join("\n"), "utf8").toString("base64"));
NODE
else
  echo "##aetheus[setvariable name=DELIVERY_BASELINE_BOOTSTRAP]true"
fi
