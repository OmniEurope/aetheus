#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
set -eu

SCOPE="${1:-qa}"
RUN_ID="${2:-${BUILD_BUILDID:-}}"
BUILDER="${AETHEUS_BUILDX_BUILDER:-${BUILDX_BUILDER:-}}"
WORK_DIRECTORY="${AETHEUS_AGENT_WORK_DIRECTORY:-}"
REQUIRED_SAMPLES=5
MAX_CACHE_GIB="${AETHEUS_STORAGE_MAX_CACHE_GIB:-80}"
PLATEAU_GROWTH_GIB="${AETHEUS_STORAGE_PLATEAU_GROWTH_GIB:-5}"

case "$SCOPE" in
  *[!a-zA-Z0-9_.-]*|'') echo "Storage evidence scope is invalid." >&2; exit 1 ;;
esac
case "$RUN_ID" in
  *[!a-zA-Z0-9_.-]*|'') echo "Storage evidence run id is missing or invalid." >&2; exit 1 ;;
esac
case "$MAX_CACHE_GIB:$PLATEAU_GROWTH_GIB" in
  *[!0-9:]*|:*|*:) echo "Storage evidence thresholds must be non-negative integers." >&2; exit 1 ;;
esac
if [ -z "$BUILDER" ]; then
  echo "AETHEUS_BUILDX_BUILDER is missing; refusing to attribute cache evidence to an unknown builder." >&2
  exit 1
fi
if [ -z "$WORK_DIRECTORY" ] || [ ! -d "$WORK_DIRECTORY" ]; then
  echo "AETHEUS_AGENT_WORK_DIRECTORY is missing or unavailable; evidence would not survive workspace cleanup." >&2
  exit 1
fi

size_to_bytes() {
  printf '%s\n' "$1" | awk '
    {
      value = toupper($1)
      gsub(/\*/, "", value)
      gsub(/,/, ".", value)
      if (match(value, /^[0-9]+([.][0-9]+)?/) == 0) exit 1
      number = substr(value, RSTART, RLENGTH) + 0
      unit = substr(value, RLENGTH + 1)
      factor = 1
      if (unit == "K" || unit == "KB") factor = 1000
      else if (unit == "KIB") factor = 1024
      else if (unit == "M" || unit == "MB") factor = 1000000
      else if (unit == "MIB") factor = 1048576
      else if (unit == "G" || unit == "GB") factor = 1000000000
      else if (unit == "GIB") factor = 1073741824
      else if (unit == "T" || unit == "TB") factor = 1000000000000
      else if (unit == "TIB") factor = 1099511627776
      else if (unit != "" && unit != "B" && unit != "BYTE" && unit != "BYTES") exit 1
      printf "%.0f", number * factor
    }'
}

BUILDX_DU="$(docker buildx du --builder "$BUILDER")" || {
  echo "Could not inventory Buildx builder '$BUILDER'." >&2
  exit 1
}
CACHE_TOTAL_RAW="$(printf '%s\n' "$BUILDX_DU" | awk -F: 'tolower($1) == "total" { gsub(/^[[:space:]]+/, "", $2); print $2; exit }')"
CACHE_RECLAIMABLE_RAW="$(printf '%s\n' "$BUILDX_DU" | awk -F: 'tolower($1) == "reclaimable" { gsub(/^[[:space:]]+/, "", $2); print $2; exit }')"
CACHE_TOTAL_BYTES="$(size_to_bytes "$CACHE_TOTAL_RAW")" || { echo "Could not parse Buildx total '$CACHE_TOTAL_RAW'." >&2; exit 1; }
CACHE_RECLAIMABLE_BYTES="$(size_to_bytes "$CACHE_RECLAIMABLE_RAW")" || { echo "Could not parse Buildx reclaimable '$CACHE_RECLAIMABLE_RAW'." >&2; exit 1; }

DISK_USED_PERCENT="$(df -Pk / | awk 'NR == 2 { gsub(/%/, "", $(NF - 1)); print $(NF - 1) }')"
DISK_FREE_BYTES="$(df -Pk / | awk 'NR == 2 { printf "%.0f", $(NF - 2) * 1024 }')"
CONTAINER_INVENTORY="$(docker ps -a --filter 'label=com.docker.compose.project' \
  --format '{{.ID}}\t{{.Names}}\t{{.Label "com.docker.compose.project"}}')" || {
  echo "Could not inventory Compose containers." >&2; exit 1;
}
VOLUME_INVENTORY="$(docker volume ls --filter 'label=com.docker.compose.project' \
  --format '{{.Name}}\t{{.Label "com.docker.compose.project"}}')" || {
  echo "Could not inventory Compose volumes." >&2; exit 1;
}
# A residual is a QA stack of this run or of an older one: prune-stale-qa-compose.sh removed the older
# ones and this run tore its own down. A newer run's stack is another QA still running on the host
# (a nightly beside a candidate), which the prune protects on purpose; counting it made a successful
# QA fail on its neighbour's live stack (candidate 2505, QA 2514 beside nightly QA 2515, 2026-10-02).
# With a non-numeric run id every QA stack still counts, as before.
qa_residuals() {
  awk -F '\t' -v column="$1" -v run="$RUN_ID" '
    $column ~ /^aetheus-qa-(rollback-)?[0-9]+$/ {
      id = $column
      sub(/^aetheus-qa-(rollback-)?/, "", id)
      if (run ~ /^[0-9]+$/ && id + 0 > run + 0) { concurrent++; next }
      print
    }
    END { if (concurrent) printf "Not counted: %d resource(s) of a newer QA run still in progress.\n", concurrent > "/dev/stderr" }'
}
RESIDUAL_CONTAINER_DETAILS="$(printf '%s\n' "$CONTAINER_INVENTORY" | qa_residuals 3)"
RESIDUAL_VOLUME_DETAILS="$(printf '%s\n' "$VOLUME_INVENTORY" | qa_residuals 2)"
RESIDUAL_CONTAINERS="$(printf '%s\n' "$RESIDUAL_CONTAINER_DETAILS" | awk 'NF { count++ } END { print count + 0 }')"
RESIDUAL_VOLUMES="$(printf '%s\n' "$RESIDUAL_VOLUME_DETAILS" | awk 'NF { count++ } END { print count + 0 }')"
COLLECTED_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

if [ "$RESIDUAL_CONTAINERS" -ne 0 ] || [ "$RESIDUAL_VOLUMES" -ne 0 ]; then
  echo "Exact residual QA Compose resources detected:" >&2
  [ -z "$RESIDUAL_CONTAINER_DETAILS" ] || printf 'container\t%s\n' "$RESIDUAL_CONTAINER_DETAILS" >&2
  [ -z "$RESIDUAL_VOLUME_DETAILS" ] || printf 'volume\t%s\n' "$RESIDUAL_VOLUME_DETAILS" >&2
  exit 1
fi

STATE_DIRECTORY="$WORK_DIRECTORY/storage-evidence"
LEDGER="$STATE_DIRECTORY/$SCOPE-v2.tsv"
mkdir -p "$STATE_DIRECTORY"
TEMP_LEDGER="$STATE_DIRECTORY/.$SCOPE.$$.tmp"
RECENT="$STATE_DIRECTORY/.$SCOPE.$$.recent"
trap 'rm -f "$TEMP_LEDGER" "$RECENT"' EXIT HUP INT TERM

if [ -f "$LEDGER" ]; then
  awk -F '\t' -v run="$RUN_ID" -v builder="$BUILDER" '!($2 == run && $3 == builder)' "$LEDGER" > "$TEMP_LEDGER"
else
  : > "$TEMP_LEDGER"
fi
printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
  "$COLLECTED_AT" "$RUN_ID" "$BUILDER" "$CACHE_TOTAL_BYTES" "$CACHE_RECLAIMABLE_BYTES" \
  "$DISK_USED_PERCENT" "$DISK_FREE_BYTES" "$RESIDUAL_CONTAINERS" "$RESIDUAL_VOLUMES" >> "$TEMP_LEDGER"
tail -n 50 "$TEMP_LEDGER" > "$LEDGER"
chmod 600 "$LEDGER"

awk -F '\t' -v builder="$BUILDER" '$3 == builder' "$LEDGER" | tail -n "$REQUIRED_SAMPLES" > "$RECENT"
SAMPLE_COUNT="$(awk 'END { print NR + 0 }' "$RECENT")"
echo "Storage evidence recorded: scope=$SCOPE run=$RUN_ID builder=$BUILDER cache=$CACHE_TOTAL_BYTES reclaimable=$CACHE_RECLAIMABLE_BYTES diskUsed=$DISK_USED_PERCENT% free=$DISK_FREE_BYTES residualContainers=$RESIDUAL_CONTAINERS residualVolumes=$RESIDUAL_VOLUMES samples=$SAMPLE_COUNT/$REQUIRED_SAMPLES"

if [ "$SAMPLE_COUNT" -lt "$REQUIRED_SAMPLES" ]; then
  echo "Storage plateau evidence is waiting for $((REQUIRED_SAMPLES - SAMPLE_COUNT)) additional successful QA run(s)."
  exit 0
fi

MAX_CACHE_BYTES=$((MAX_CACHE_GIB * 1024 * 1024 * 1024))
PLATEAU_GROWTH_BYTES=$((PLATEAU_GROWTH_GIB * 1024 * 1024 * 1024))
if EVALUATION="$(awk -F '\t' -v maxCache="$MAX_CACHE_BYTES" -v maxGrowth="$PLATEAU_GROWTH_BYTES" '
  {
    if ($4 > maxCache) overBudget = 1
    if ($8 != 0 || $9 != 0) residual = 1
    if (NR >= 3) {
      if (!recentInitialized) { recentMin = $4; recentMax = $4; recentInitialized = 1 }
      if ($4 < recentMin) recentMin = $4
      if ($4 > recentMax) recentMax = $4
    }
  }
  END {
    growth = recentMax - recentMin
    printf "Storage plateau evaluation: recentRangeBytes=%.0f maxAllowedBytes=%.0f cacheBudgetBytes=%.0f residual=%d", growth, maxGrowth, maxCache, residual
    if (overBudget || residual || growth > maxGrowth) exit 1
  }' "$RECENT")"; then
  echo "$EVALUATION"
  echo "Storage plateau verified across five successful QA runs."
else
  echo "$EVALUATION" >&2
  echo "Storage plateau verification failed; inspect $LEDGER before any production rollout." >&2
  exit 1
fi
