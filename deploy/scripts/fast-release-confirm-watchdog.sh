#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
#
# The host's own countdown for aetheus-release-fast, the shell counterpart of the agent's blue-green
# confirmation window (BG_CONFIRM_MINUTES, BlueGreenConfirmation) that aetheus-deploy-prod uses.
#
# The pipeline's Confirm stage asks a human for 10 minutes and its rollback stage puts the previous
# colour back when nobody confirms. But the backend being deployed is the one that hands out those
# tasks: if it is too broken to dispatch them, nothing would ever go back. So release-fast-deploy.sh
# starts this script detached (setsid, its own log, no inherited output) right after the go-live.
# It waits, then rolls back ONLY if the fast transaction of its own commit is still waiting for
# confirmation. A committed deployment (the transaction is gone) or a rollback that already ran
# leaves nothing to do, and a newer deployment is another commit: both are no-ops.
#
# Usage (from release-fast-deploy.sh only): fast-release-confirm-watchdog.sh WATCH_DIR MINUTES
# WATCH_DIR holds copies of what the rollback needs, so it works after the pipeline workspace is gone:
#   watch.env                                   paths, ports and names (no secret)
#   deploy/scripts/finalize-fast-release-transaction.sh, deploy/scripts/deploy-identity.sh
#   deploy/compose/remote-bluegreen.compose.yml
set -u

WATCH_DIR="${1:?watch directory is required}"
MINUTES="${2:?minutes are required}"
case "$MINUTES" in ''|*[!0-9]*) echo "Invalid confirmation window: $MINUTES" >&2; exit 2 ;; esac

# shellcheck source=/dev/null
. "$WATCH_DIR/watch.env"

# 30 s of grace after the window, like the agent's watchdog: with a healthy backend the approval
# expires first and the pipeline's own rollback stage runs; this only acts when that did not happen.
sleep $((MINUTES * 60 + 30))

TRANSACTION_DIR="$(dirname "$ENV_FILE")/fast-deployment-transaction"
STATE="$(cat "$TRANSACTION_DIR/state" 2>/dev/null || true)"
CANDIDATE="$(cat "$TRANSACTION_DIR/source-commit" 2>/dev/null || true)"
if [ "$STATE" = HOST_COMMITTED_PENDING_RELEASE ] && [ "$CANDIDATE" = "$BUILD_SOURCEVERSION" ]; then
  echo "$(date -u +%FT%TZ) No confirmation of $BUILD_SOURCEVERSION within $MINUTES minutes: returning to the previous colour."
  if sh "$WATCH_DIR/deploy/scripts/finalize-fast-release-transaction.sh" rollback; then
    echo "$(date -u +%FT%TZ) Rolled back."
  else
    echo "$(date -u +%FT%TZ) FATAL: the rollback failed; the host stays on the candidate colour." >&2
  fi
else
  echo "$(date -u +%FT%TZ) Nothing to do for $BUILD_SOURCEVERSION (transaction state: ${STATE:-none}, candidate: ${CANDIDATE:-none})."
fi
rm -rf "$WATCH_DIR"
