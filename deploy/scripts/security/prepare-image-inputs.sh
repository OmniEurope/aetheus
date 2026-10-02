# SPDX-License-Identifier: EUPL-1.2
#
# Proves the images this security stage is about to scan are the exact images CI packaged, then
# unpacks them for the scanners. Without this, a scan could report on one build while a release
# shipped another, which is the class of gap the sealed provenance exists to close.
#
# This was 44 lines inlined in .pipeline/aetheus-security.yaml, including a JavaScript heredoc that
# nothing could run, lint or test. The body is relocated unchanged; the heredoc stays a heredoc here,
# which is honest about what this commit did rather than mixing a move with a rewrite.
#
# Inputs, all read from the environment (none positional):
#   WORKSPACE            the run's checkout, holding the restored .pipeline-artifacts
#   BUILD_SOURCEVERSION  the revision the restored artifacts must belong to
set -eu
EXPECTED="${BUILD_SOURCEVERSION}"
INPUT=".security-image-input/.pipeline-artifacts"
ACTUAL="$(tr -d '\r\n' < "$INPUT/source-commit")"
if [ "$ACTUAL" != "$EXPECTED" ]; then
  echo "Security image artifact revision $ACTUAL does not match pipeline revision $EXPECTED." >&2
  exit 1
fi
test -s "$INPUT/aetheus-back.tar.gz"
test -s "$INPUT/aetheus-front.tar.gz"
NODE="$(sh deploy/scripts/ensure-node-runtime.sh)"
CHECKSUMS="$INPUT/security-image-sha256s"
rm -f "$CHECKSUMS"
"$NODE" - "$EXPECTED" \
  "$INPUT/delivery-contract.json" \
  "$INPUT/aetheus-back.tar.gz" \
  "$INPUT/aetheus-front.tar.gz" > "$CHECKSUMS" <<'NODE'
const fs = require("node:fs");
const path = require("node:path");
const [expected, contractPath, ...artifacts] = process.argv.slice(2);
const contract = JSON.parse(fs.readFileSync(contractPath, "utf8"));
// Schema 2 since the seal moved from the deployed release identity to schema states
// (ADR-042). What this check cares about is unchanged: that the contract describes the very
// commit being scanned.
if (contract.schema !== 2 || contract.sourceSha !== expected) {
  throw new Error("The security delivery contract belongs to another source commit.");
}
for (const artifact of artifacts) {
  const name = path.basename(artifact);
  const expectedHash = contract.artifacts?.[name];
  if (!/^[0-9a-f]{64}$/.test(expectedHash ?? ""))
    throw new Error(`Security image '${name}' has no valid delivery contract digest.`);
  process.stdout.write(`${expectedHash}  ${artifact}\n`);
}
NODE
sha256sum -c "$CHECKSUMS"
rm -f "$CHECKSUMS"
rm -rf .analysis-image
mkdir -p .analysis-image
gzip -dc "$INPUT/aetheus-back.tar.gz" > .analysis-image/aetheus-back.tar
gzip -dc "$INPUT/aetheus-front.tar.gz" > .analysis-image/aetheus-front.tar
test -s .analysis-image/aetheus-back.tar
test -s .analysis-image/aetheus-front.tar
printf '%s\n' "$ACTUAL" > .analysis-image/source-commit
