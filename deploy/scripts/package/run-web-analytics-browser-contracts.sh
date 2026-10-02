# SPDX-License-Identifier: EUPL-1.2
#
# Runs the browser package's own contract suite on the pinned Node runtime.
#
# This was 8 lines inlined in .pipeline/package-aetheus-web-analytics-browser.yaml. The body is
# relocated unchanged.
#
# Inputs: none.
set -eu
# The pinned, checksummed runtime, not whatever the host happens to have: a simulator
# rebuild removed a hand-installed /usr/local/bin/node and every package step that calls
# node or npm bare died with "not found" (publisher run 1236).
NODE="$(sh deploy/scripts/ensure-node-runtime.sh)"
PATH="$(dirname "$NODE"):$PATH"
export PATH
npm test --prefix packages/aetheus-web-analytics
