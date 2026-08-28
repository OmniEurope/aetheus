#!/usr/bin/env sh
# SPDX-License-Identifier: EUPL-1.2
set -eu

image="${1:?backend image is required}"
output_directory="${2:?output directory is required}"
expected_commit="${3:?expected source commit is required}"
node_binary="${4:-node}"
output_mode="${5:-full}"
protocol_policy="${6:-upgrade-bridge}"

case "$output_mode" in
  full|manifest-only) ;;
  *) echo "Output mode must be full or manifest-only." >&2; exit 2 ;;
esac

case "$protocol_policy" in
  upgrade-bridge|historical-compatible) ;;
  *) echo "Protocol policy must be upgrade-bridge or historical-compatible." >&2; exit 2 ;;
esac

case "$expected_commit" in
  *[!0-9a-fA-F]*|'') echo "Expected source commit must be hexadecimal." >&2; exit 2 ;;
esac

rm -rf "$output_directory"
mkdir -p "$output_directory"
container_id="$(docker create "$image")"
cleanup() {
  docker rm -f "$container_id" >/dev/null 2>&1 || true
}
trap cleanup EXIT HUP INT TERM
docker cp "$container_id:/app/wwwroot/downloads/." "$output_directory"
cleanup
trap - EXIT HUP INT TERM

"$node_binary" - "$output_directory" "$expected_commit" "$protocol_policy" <<'NODE'
const crypto = require("node:crypto");
const fs = require("node:fs");
const path = require("node:path");
const [directory, expectedCommit, protocolPolicy] = process.argv.slice(2);
const manifestPath = path.join(directory, "agent-release-manifest.json");
const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
if (manifest.commit !== expectedCommit) {
  throw new Error(`Agent manifest commit ${manifest.commit} does not match ${expectedCommit}.`);
}
const exposesUpgradeBridge =
  manifest.minimumSupportedProtocol === 1 && manifest.maximumSupportedProtocol === 2;
const exposesHistoricalCurrentOnly =
  manifest.minimumSupportedProtocol === 2 && manifest.maximumSupportedProtocol === 2;
if (manifest.protocolVersion !== 2 ||
    (protocolPolicy === "upgrade-bridge" && !exposesUpgradeBridge)) {
  throw new Error("Agent manifest does not expose the required protocol 1-2 upgrade bridge.");
}
if (protocolPolicy === "historical-compatible" &&
    !exposesUpgradeBridge && !exposesHistoricalCurrentOnly) {
  throw new Error("Historical agent manifest is not compatible with protocol 2.");
}
if (!manifest.softwareCapabilities.includes("ai.run")) {
  throw new Error("Agent manifest does not publish the ai.run capability.");
}
for (const archive of manifest.archives) {
  const archivePath = path.join(directory, archive.fileName);
  const bytes = fs.readFileSync(archivePath);
  const sha256 = crypto.createHash("sha256").update(bytes).digest("hex");
  if (bytes.length !== archive.sizeBytes || sha256 !== archive.sha256.toLowerCase()) {
    throw new Error(`Agent archive integrity mismatch: ${archive.fileName}.`);
  }
}
NODE

if [ "$output_mode" = manifest-only ]; then
  find "$output_directory" -type f ! -name agent-release-manifest.json -delete
  find "$output_directory" -depth -type d -empty -delete
  test -s "$output_directory/agent-release-manifest.json"
fi
