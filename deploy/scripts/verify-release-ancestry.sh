#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
#
# Refuse a deployment whose release was built from a commit the deploy branch does not contain.
#
# A deploy run mixes two sources: the image comes from the candidate release, while the deployment
# instructions (this pipeline's YAML, the Compose file, BG_COMPOSE_ENV) are read from the branch the
# deploy runs on. Nothing used to check that the two agree. Deploy 1995 shipped the image built from
# 39b751bb while reading the contract from 9afff625, which still lacked the AETHEUS_WEB_ANALYTICS_*
# names: the run reported success, the corrected code reached production, and the variables it needed
# never did. The panels stayed empty and the deployment looked healthy.
set -eu

# This step deliberately receives no reusable Git credential, so a fetch that needs one must fail
# rather than sit on a credential prompt until the step's timeout expires.
export GIT_TERMINAL_PROMPT=0
export GIT_ASKPASS=/bin/false

SOURCE_COMMIT_FILE="${1:?usage: verify-release-ancestry.sh <source-commit-file> [deploy-ref]}"
DEPLOY_REF="${2:-HEAD}"

if [ ! -f "$SOURCE_COMMIT_FILE" ]; then
    echo "Release provenance file '$SOURCE_COMMIT_FILE' is missing; cannot prove the deploy branch carries this release." >&2
    exit 1
fi

RELEASE_SHA=$(tr -d ' \t\r\n' < "$SOURCE_COMMIT_FILE")
case "$RELEASE_SHA" in
    *[!0-9a-fA-F]*)
        echo "Release provenance file '$SOURCE_COMMIT_FILE' does not hold a commit id." >&2
        exit 1
        ;;
esac
# An abbreviated id could resolve to a different commit than the one the release recorded, so require
# a full object name the way prod-deploy-prepare.sh does.
case "${#RELEASE_SHA}" in
    40 | 64) ;;
    *)
        echo "Release provenance '$RELEASE_SHA' is not a full commit id; refusing to guess which commit it means." >&2
        exit 1
        ;;
esac

# The deploy checkout is shallow by construction (AETHEUS_GIT_HISTORY_DEPTH), so the release commit may
# be absent, and - the subtler case - present as a grafted object while the history between it and HEAD
# is not. Ancestry cannot be decided on a shallow history: deepen first, and only then answer.
if [ "$(git rev-parse --is-shallow-repository)" = true ]; then
    git fetch --quiet --unshallow origin 2>/dev/null || true
fi
if ! git cat-file -e "${RELEASE_SHA}^{commit}" 2>/dev/null; then
    git fetch --quiet origin "$RELEASE_SHA" 2>/dev/null || true
fi

DEPLOY_SHA=$(git rev-parse --short "$DEPLOY_REF")

# Distinguish "proved absent" from "could not be proved": only the first justifies telling the operator
# to promote the commit. Claiming the branch lacks a release we simply could not see would send them
# chasing a promotion that already happened.
if ! git cat-file -e "${RELEASE_SHA}^{commit}" 2>/dev/null; then
    echo "Release commit $RELEASE_SHA is unknown to this checkout, so the deploy branch cannot be shown to contain it." >&2
    echo "Deepen the deploy checkout (AETHEUS_GIT_HISTORY_DEPTH) or confirm the release was published to this remote." >&2
    exit 1
fi

if git merge-base --is-ancestor "$RELEASE_SHA" "$DEPLOY_REF"; then
    echo "Deploy branch ($DEPLOY_SHA) contains release commit $(git rev-parse --short "$RELEASE_SHA")."
    exit 0
fi

if [ "$(git rev-parse --is-shallow-repository)" = true ]; then
    echo "Release commit $RELEASE_SHA is not reachable from $DEPLOY_SHA, but this checkout is still shallow," >&2
    echo "so the deploy branch cannot be shown either to contain or to lack it. Deepen the checkout and retry." >&2
    exit 1
fi

cat >&2 <<MSG
Refusing to deploy: the deploy branch does not contain this release.

  release commit : $RELEASE_SHA
  deploy branch  : $DEPLOY_SHA ($DEPLOY_REF)

The image would be the one this release built, but the deployment instructions would come from an
older branch state. Anything the release added to the deployment contract - a new BG_COMPOSE_ENV
entry, a new Compose variable, a changed migration step - would be silently dropped, and the run
would still report success.

Promote the release commit to the deploy branch first, then run this deployment again.
MSG
exit 1
