#!/bin/sh

# Keep long-running CI commands readable without losing liveness feedback.
# Usage: run-with-progress.sh <label> <command> [args...]

set -u

if [ "$#" -lt 2 ]; then
  echo "Usage: $0 <label> <command> [args...]" >&2
  exit 2
fi

LABEL="$1"
shift
INTERVAL="${AETHEUS_PROGRESS_INTERVAL_SECONDS:-30}"

case "$INTERVAL" in
  ''|*[!0-9]*|0)
    echo "AETHEUS_PROGRESS_INTERVAL_SECONDS must be a positive integer." >&2
    exit 2
    ;;
esac

STARTED_AT="$(date +%s)"
COMMAND_PID=""
PROGRESS_PID=""

if ! command -v setsid >/dev/null 2>&1; then
  echo "setsid is required to isolate and terminate the command process group." >&2
  exit 1
fi

stop_progress() {
  if [ -n "$PROGRESS_PID" ]; then
    kill "$PROGRESS_PID" 2>/dev/null || true
    wait "$PROGRESS_PID" 2>/dev/null || true
    PROGRESS_PID=""
  fi
}

terminate() {
  EXIT_CODE="$1"
  trap - HUP INT TERM
  if [ -n "$COMMAND_PID" ]; then
    kill -TERM -- "-$COMMAND_PID" 2>/dev/null || true
    ATTEMPT=0
    while kill -0 "$COMMAND_PID" 2>/dev/null && [ "$ATTEMPT" -lt 10 ]; do
      sleep 1
      ATTEMPT=$((ATTEMPT + 1))
    done
    if kill -0 "$COMMAND_PID" 2>/dev/null; then
      kill -KILL -- "-$COMMAND_PID" 2>/dev/null || true
    fi
    wait "$COMMAND_PID" 2>/dev/null || true
    COMMAND_PID=""
  fi
  stop_progress
  exit "$EXIT_CODE"
}

trap 'terminate 129' HUP
trap 'terminate 130' INT
trap 'terminate 143' TERM

echo "==> $LABEL started"
setsid "$@" &
COMMAND_PID="$!"

(
  while sleep "$INTERVAL"; do
    if ! kill -0 "$COMMAND_PID" 2>/dev/null; then
      exit 0
    fi
    NOW="$(date +%s)"
    echo "==> $LABEL still running ($((NOW - STARTED_AT))s elapsed)"
  done
) &
PROGRESS_PID="$!"

if wait "$COMMAND_PID"; then
  RESULT=0
else
  RESULT="$?"
fi
COMMAND_PID=""
stop_progress

FINISHED_AT="$(date +%s)"
echo "==> $LABEL finished in $((FINISHED_AT - STARTED_AT))s (exit $RESULT)"
exit "$RESULT"
