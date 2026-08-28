#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
# SPDX-FileCopyrightText: 2026 Aetheus contributors
#
# `docker buildx build --load` against a docker-container builder, retried when and ONLY when the
# build failed while handing the finished image back to the daemon.
#
# Why this exists. With the docker-container driver, `--load` does not place the image in the daemon
# directly: BuildKit exports it to an OCI tarball and streams it to the docker CLI over the build
# session, and the CLI feeds it to the daemon. On a busy runner the CLI can be starved long enough
# for BuildKit to drop that session, and the build dies with
#   ERROR: failed to solve: DeadlineExceeded: no active session for <id>: context deadline exceeded
# after the export itself has already succeeded - the mirror logs show
# `exporting layers/manifest/config done` immediately before the error. Nothing about the image is
# wrong; the handoff is. Mirror runs 1106 and 1108 both died there, at the same layer, one on a cold
# NuGet cache and one on a warm one, so it is the transfer and not the build that is fragile.
#
# What this is NOT. It is not a way to make a failing build pass. Only the transport failure above is
# retried, matched on BuildKit's own wording; a compile error, a failed test, a refused gate or any
# other message fails on the first attempt exactly as before. The retry re-runs the same command with
# every layer already cached, so it repeats the export and nothing else, and the image must still
# genuinely load - if it does not, the step still fails.
set -eu

fail() { echo "$*" >&2; exit 1; }

[ "$#" -ge 1 ] || fail "Usage: buildx-build-load.sh <docker buildx build arguments...>"

ATTEMPTS="${AETHEUS_BUILDX_LOAD_ATTEMPTS:-3}"
case "$ATTEMPTS" in ''|*[!0-9]*) fail "AETHEUS_BUILDX_LOAD_ATTEMPTS must be a whole number." ;; esac
[ "$ATTEMPTS" -ge 1 ] || fail "AETHEUS_BUILDX_LOAD_ATTEMPTS must be at least 1."

WORK="$(mktemp -d)"
# Never leave the captured build log behind, whatever the outcome.
trap 'rm -rf "$WORK"' EXIT INT TERM
LOG="$WORK/build.log"
STATUS_FILE="$WORK/status"

attempt=1
while :; do
    # `tee` keeps the build streaming into the run log; docker's own exit code is carried out of the
    # pipeline through a file, because POSIX sh has no PIPESTATUS and tee's status would mask it.
    { docker buildx build "$@" 2>&1; echo "$?" > "$STATUS_FILE"; } | tee "$LOG"
    status="$(cat "$STATUS_FILE" 2>/dev/null || echo 1)"

    [ "$status" -eq 0 ] && exit 0

    if ! grep -q "no active session for .*context deadline exceeded" "$LOG"; then
        echo ">>> Build failed for a reason other than the image handoff; not retrying." >&2
        exit "$status"
    fi

    if [ "$attempt" -ge "$ATTEMPTS" ]; then
        echo ">>> The image handoff failed on all $ATTEMPTS attempt(s); failing the step." >&2
        exit "$status"
    fi

    echo ">>> BuildKit dropped the build session while handing the image to the daemon" \
         "(attempt $attempt of $ATTEMPTS). Every layer is cached, so the retry repeats the export only."
    attempt=$((attempt + 1))
    sleep 5
done
