#!/bin/sh
#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2

set -eu

NODE="$(sh deploy/scripts/ensure-node-runtime.sh)"
NODE_DIRECTORY="$(dirname "$NODE")"
NPM="$NODE_DIRECTORY/npm"
export PATH="$NODE_DIRECTORY:$PATH"

test -x "$NPM" || {
  echo "The pinned Node.js runtime does not provide npm."
  exit 2
}
test -f package-lock.json || {
  echo "JavaScript analysis requires the committed root package-lock.json."
  exit 2
}

attempt=1
while ! "$NPM" ci --ignore-scripts --no-audit --no-fund --prefer-offline \
  --fetch-retries=2 \
  --fetch-retry-mintimeout=1000 \
  --fetch-retry-maxtimeout=5000 \
  --fetch-timeout=60000
do
  if [ "$attempt" -ge 3 ]; then
    echo "npm ci failed after $attempt bounded attempts."
    exit 1
  fi
  echo "npm ci attempt $attempt failed; retrying after a bounded backoff."
  attempt=$((attempt + 1))
  sleep $((attempt * 2))
done
