#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
#
# F4: after a successful production deployment, fast-forward `main` to the exact commit that was just
# proven deployable and deployed (the release's own sourceSha, read from the sealed assurance contract's
# provenance file, NOT the develop checkout's possibly-newer HEAD). `main` no longer leads a deployment -
# it trails it, so this is the ONLY place that ever advances it, and it only ever advances to a commit
# that is already live.
#
# Deliberately best-effort: called AFTER Commit/MarkDeployed already succeeded, so a failure here must
# never mark the deployment itself as failed (the deploy is done; this is a consequence of it, not a
# condition for it) and must never force-push. A plain (non --force) push is refused by git itself unless
# it is a fast-forward, which is the whole safety property this step relies on - no extra flag needed.
set -eu

fail_soft() {
  echo "WARNING: main was not advanced: $*" >&2
  echo "This does NOT affect the deployment that just succeeded; advance main by hand or on the next successful deploy." >&2
  exit 0
}

WORKSPACE="${WORKSPACE:-}"
[ -d "$WORKSPACE" ] || fail_soft "WORKSPACE is not a directory."

SOURCE_COMMIT_FILE="$WORKSPACE/.pipeline-artifacts/source-commit"
[ -s "$SOURCE_COMMIT_FILE" ] || fail_soft "release provenance file '$SOURCE_COMMIT_FILE' is missing."
DEPLOYED_SHA="$(tr -d ' \t\r\n' < "$SOURCE_COMMIT_FILE")"
case "$DEPLOYED_SHA" in
  *[!0-9a-fA-F]*) fail_soft "release provenance is not a commit id." ;;
esac
case "${#DEPLOYED_SHA}" in
  40 | 64) ;;
  *) fail_soft "release provenance is not a full commit id." ;;
esac

REPO_URL="${BUILD_REPOSITORY_URI:-}"
[ -n "$REPO_URL" ] || fail_soft "BUILD_REPOSITORY_URI is unavailable."

# Secret-zero convention (same file as DB_PASSWORD/ADMIN_PASSWORD/etc, see prod-deploy-prepare.sh):
# a dedicated service-account Personal Access Token with Write permission on this project, scoped to
# nothing else. Absent by default - advancing main is a convenience, not a deployment requirement, so an
# unprovisioned token is a soft skip, not a failure.
ENV_FILE="${PROD_ENV_FILE:-}"
if [ -z "$ENV_FILE" ] || [ ! -r "$ENV_FILE" ]; then
  fail_soft "PROD_ENV_FILE is unavailable; cannot read the advance-main credential."
fi
ADVANCE_MAIN_USER="$(sed -n 's/^AETHEUS_ADVANCE_MAIN_USER=//p' "$ENV_FILE" | tail -n1)"
ADVANCE_MAIN_TOKEN="$(sed -n 's/^AETHEUS_ADVANCE_MAIN_TOKEN=//p' "$ENV_FILE" | tail -n1)"
if [ -z "$ADVANCE_MAIN_USER" ] || [ -z "$ADVANCE_MAIN_TOKEN" ]; then
  fail_soft "AETHEUS_ADVANCE_MAIN_USER/AETHEUS_ADVANCE_MAIN_TOKEN are not provisioned in $ENV_FILE."
fi

AUTH_HEADER="Authorization: Basic $(printf '%s:%s' "$ADVANCE_MAIN_USER" "$ADVANCE_MAIN_TOKEN" | base64 | tr -d '\n')"

cd "$WORKSPACE"
export GIT_TERMINAL_PROMPT=0
export GIT_ASKPASS=/bin/false

# Deepen first: a shallow checkout may not even have DEPLOYED_SHA as a real commit object yet.
if [ "$(git rev-parse --is-shallow-repository)" = true ]; then
  git fetch --quiet -c http.extraHeader="$AUTH_HEADER" origin "$DEPLOYED_SHA" 2>/dev/null || true
fi
git cat-file -e "${DEPLOYED_SHA}^{commit}" 2>/dev/null \
  || fail_soft "deployed commit $DEPLOYED_SHA is unknown to this checkout."

# Plain push, deliberately never --force: git itself refuses a non-fast-forward update to an existing
# branch, which is the entire safety property this relies on. Push output can carry the token in a
# redirected URL on some git versions' error paths, so the header form above is used instead and nothing
# here echoes the header value.
if git -c http.extraHeader="$AUTH_HEADER" push "$REPO_URL" "${DEPLOYED_SHA}:refs/heads/main" 2>&1; then
  echo ">>> main fast-forwarded to $DEPLOYED_SHA (the commit just deployed)."
else
  fail_soft "the push was refused (likely not a fast-forward, or the credential lacks Write permission)."
fi
