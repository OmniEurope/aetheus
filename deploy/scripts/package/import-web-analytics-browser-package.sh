# SPDX-License-Identifier: EUPL-1.2
#
# Installs the exact tarball this run produced into a throwaway project and imports it. A package
# that packs cleanly can still be unimportable (a wrong `exports` map, a missing file), and only
# actually importing it proves otherwise.
#
# This was 14 lines inlined in .pipeline/package-aetheus-web-analytics-browser.yaml. The body is
# relocated unchanged apart from PACKAGE_VERSION, noted below.
#
# Inputs, all read from the environment (none positional):
#   WORKSPACE        the run's checkout, holding .package-candidate/web-analytics-browser
#   PACKAGE_VERSION  the version this run packaged. It was written `$(...)` in the YAML, which the
#                    control plane substitutes in a `shell:` block; inside a script that same text is
#                    a shell command substitution, so the caller passes it by name.
set -eu
# The pinned, checksummed runtime, not whatever the host happens to have: a simulator
# rebuild removed a hand-installed /usr/local/bin/node and every package step that calls
# node or npm bare died with "not found" (publisher run 1236).
NODE="$(sh deploy/scripts/ensure-node-runtime.sh)"
PATH="$(dirname "$NODE"):$PATH"
export PATH
TEMP="$(mktemp -d)"
trap 'rm -rf "$TEMP"' EXIT HUP INT TERM
cd "$TEMP"
npm init -y >/dev/null
npm install "$WORKSPACE/.package-candidate/web-analytics-browser/aetheus-web-analytics-${PACKAGE_VERSION}.tgz" \
  --ignore-scripts --no-audit --no-fund
node --input-type=module -e "await import('@aetheus/web-analytics')"
