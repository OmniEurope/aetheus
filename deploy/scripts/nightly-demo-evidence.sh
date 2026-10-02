#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
# Bounded result evidence for one Nightly run, recorded after the cutover is committed.
#
# It runs after `bluegreen-commit`, deliberately: nothing here can undo a deployment that is already
# serving. That is the same reason the response-time budget in the smoke step is advisory - a growth
# comparison is a signal about the build, not a verdict on the environment.
#
# The comparison is against the previous SUCCESSFUL Nightly, whose metrics live in the environment
# state directory. A first run has no baseline and says so rather than inventing one.
set -eu

fail() {
  echo "FATAL: $*" >&2
  exit 1
}

# Shape, not value: the exact path belongs to the aetheus.demo library, and pinning it here made that
# library decorative. What must hold is that this is a demo state directory, not production's. The
# NIGHTLY_DEPLOY_TARGET check that stood here went with the variable (PLAN-003 2.1): a deployment
# deploys, and this production-token guard is what keeps the demo's evidence away from production.
case "${STATE_DIR:-}" in
  *prod*) fail "The demo state directory names production: $STATE_DIR" ;;
  /var/lib/*/*) fail "The demo state directory must be a direct child of /var/lib: $STATE_DIR" ;;
  /var/lib/?*) ;;
  *) fail "The demo state directory must live under /var/lib: '${STATE_DIR:-}'" ;;
esac
COMPOSE_PROJECT="${COMPOSE_PROJECT:?COMPOSE_PROJECT is required}"
APP_HOST="${APP_HOST:?APP_HOST is required}"
WORKSPACE="${WORKSPACE:?WORKSPACE is required}"
SOURCE_SHA="${BUILD_SOURCEVERSION:?BUILD_SOURCEVERSION is required}"
STARTED_EPOCH="${NIGHTLY_STARTED_EPOCH:?NIGHTLY_STARTED_EPOCH is required}"
case "$STARTED_EPOCH" in ''|*[!0-9]*) fail "NIGHTLY_STARTED_EPOCH is invalid." ;; esac

ARTIFACT_DIR="$WORKSPACE/.pipeline-artifacts"
EVIDENCE_DIR="$WORKSPACE/.nightly-evidence"
METRICS_FILE="$STATE_DIR/nightly-metrics"
[ -d "$EVIDENCE_DIR" ] || fail "The nightly evidence directory is missing."

FINISHED_EPOCH="$(date -u +%s)"
DURATION_SECONDS=$((FINISHED_EPOCH - STARTED_EPOCH))
[ "$DURATION_SECONDS" -ge 0 ] || fail "Nightly duration is invalid."
BACK_BYTES="$(stat -c %s "$ARTIFACT_DIR/aetheus-back.tar.gz")"
FRONT_BYTES="$(stat -c %s "$ARTIFACT_DIR/aetheus-front.tar.gz")"
BROWSER_BYTES="$(stat -c %s "$ARTIFACT_DIR/aetheus-browser-smoke.tar.gz")"
VITRINE_BYTES="$(stat -c %s "$ARTIFACT_DIR/aetheus-vitrine.tar.gz")"

previous_metric() {
  sed -n "s/^$1=//p" "$METRICS_FILE" 2>/dev/null | tail -n 1
}

compare_metric() {
  label="$1"
  current="$2"
  previous="$3"
  case "$previous" in ''|*[!0-9]*) return 0 ;; esac
  [ "$previous" -gt 0 ] || return 0
  if [ "$current" -gt $((previous * 2)) ]; then
    fail "$label more than doubled compared with the previous successful Nightly."
  fi
  if [ "$current" -gt $((previous * 115 / 100)) ]; then
    echo "WARNING: $label increased by more than 15% compared with the previous successful Nightly." >&2
  fi
}

compare_metric duration_seconds "$DURATION_SECONDS" "$(previous_metric duration_seconds)"
compare_metric back_bytes "$BACK_BYTES" "$(previous_metric back_bytes)"
compare_metric front_bytes "$FRONT_BYTES" "$(previous_metric front_bytes)"
compare_metric browser_bytes "$BROWSER_BYTES" "$(previous_metric browser_bytes)"
compare_metric vitrine_bytes "$VITRINE_BYTES" "$(previous_metric vitrine_bytes)"

METRICS_TMP="$METRICS_FILE.tmp.$$"
cat > "$METRICS_TMP" <<EOF
schema=2
source_sha=$SOURCE_SHA
duration_seconds=$DURATION_SECONDS
back_bytes=$BACK_BYTES
front_bytes=$FRONT_BYTES
browser_bytes=$BROWSER_BYTES
vitrine_bytes=$VITRINE_BYTES
EOF
chmod 600 "$METRICS_TMP"
mv "$METRICS_TMP" "$METRICS_FILE"
cp "$METRICS_FILE" "$EVIDENCE_DIR/demo-metrics.txt"

# The colour and the deployed revision are read back from the environment the cutover committed, not
# from what this run intended. Reporting the intention would make this file agree with itself even
# when the cutover landed somewhere else.
ACTIVE_COLOR="$(cat "$STATE_DIR/live-color" 2>/dev/null || echo unknown)"
DEPLOYED_REVISION="$(tr -d '\r\n' < "$STATE_DIR/source-commit" 2>/dev/null || echo unknown)"
[ "$DEPLOYED_REVISION" = "$SOURCE_SHA" ] \
  || fail "The committed demo revision is $DEPLOYED_REVISION, not the $SOURCE_SHA this run built."
case "$ACTIVE_COLOR" in blue|green) ;; *) fail "The committed demo colour is unusable: $ACTIVE_COLOR" ;; esac

cat > "$EVIDENCE_DIR/demo-deployment.txt" <<EOF
schema=2
source_sha=$SOURCE_SHA
compose_project=$COMPOSE_PROJECT
host=$APP_HOST
active_color=$ACTIVE_COLOR
cutover=native
release_mutation=none
EOF

# The image the preparation loaded and published (AETHEUS_BROWSER_SMOKE_IMAGE, a step output).
docker image rm "${AETHEUS_BROWSER_SMOKE_IMAGE:?AETHEUS_BROWSER_SMOKE_IMAGE is required}" >/dev/null 2>&1 || true
# A nightly that failed before this step leaves its 4 GB browser-smoke image behind, and nothing
# else on the host ever removes it. Bounding the repository here keeps those leaks to the retention
# floor instead of letting them accumulate run after run.
# shellcheck source=deploy-identity.sh
. "$WORKSPACE/deploy/scripts/deploy-identity.sh"
deploy_image_repos
sh "$WORKSPACE/deploy/scripts/retain-docker-images.sh" \
  "$BACK_IMAGE_REPO" "$FRONT_IMAGE_REPO" "$COMPOSE_PROJECT-browser-smoke"
echo "Independent Nightly recorded $SOURCE_SHA serving on $ACTIVE_COLOR."
