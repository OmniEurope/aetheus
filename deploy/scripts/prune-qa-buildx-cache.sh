#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
set -eu

BUILDER="${AETHEUS_BUILDX_BUILDER:-${BUILDX_BUILDER:-}}"
MAX_CACHE_GIB="${AETHEUS_QA_BUILDX_MAX_CACHE_GIB:-32}"
RESERVED_CACHE_GIB="${AETHEUS_QA_BUILDX_RESERVED_CACHE_GIB:-20}"
MIN_FREE_GIB="${AETHEUS_QA_BUILDX_MIN_FREE_GIB:-20}"

if [ -z "$BUILDER" ]; then
  echo "AETHEUS_BUILDX_BUILDER is missing; refusing an unscoped Buildx prune." >&2
  exit 1
fi
for LIMIT in "$MAX_CACHE_GIB" "$RESERVED_CACHE_GIB" "$MIN_FREE_GIB"; do
  case "$LIMIT" in
    ''|*[!0-9]*|0) echo "QA Buildx cache limits must be positive integers." >&2; exit 1 ;;
  esac
done
if [ "$MAX_CACHE_GIB" -lt "$RESERVED_CACHE_GIB" ]; then
  echo "QA Buildx maximum cache must be greater than or equal to its reserved cache." >&2
  exit 1
fi

echo "Applying bounded QA Buildx maintenance: builder=$BUILDER max=${MAX_CACHE_GIB}GiB reserved=${RESERVED_CACHE_GIB}GiB minFree=${MIN_FREE_GIB}GiB"
docker buildx prune --builder "$BUILDER" --force \
  --reserved-space "${RESERVED_CACHE_GIB}GB" \
  --max-used-space "${MAX_CACHE_GIB}GB" \
  --min-free-space "${MIN_FREE_GIB}GB"
echo "Bounded QA Buildx maintenance completed for '$BUILDER'."
