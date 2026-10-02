# SPDX-License-Identifier: EUPL-1.2
#
# Sourced, never run: the names a deployment script would otherwise write as literals (PLAN-003 2.1,
# "zéro nom en dur"). Everything here is derived from what the run already knows.
#
# Inputs, read from the environment:
#   BUILD_PROJECTNAME  the project name the control plane injects into every run (for the images)
#   AGENT_HELPERS_DIR  published by the agent to every task on Linux (optional, see below)
#
# Defines AGENT_HELPERS_DIR, and the function deploy_image_repos, which defines PROJECT_SLUG,
# BACK_IMAGE_REPO and FRONT_IMAGE_REPO or ends the script.

# The images CI builds are named after the product, lower-cased: aetheus-back, aetheus-front. Same
# derivation as qa-stack-identity.sh, so a second project gets its own names without an edit.
deploy_image_repos() {
  PROJECT_SLUG="$(printf '%s' "${BUILD_PROJECTNAME:-}" | tr '[:upper:]' '[:lower:]' \
    | sed -e 's/[^a-z0-9][^a-z0-9]*/-/g' -e 's/^-//' -e 's/-$//')"
  if [ -z "$PROJECT_SLUG" ]; then
    echo "BUILD_PROJECTNAME is missing or has no usable character; the images cannot be named." >&2
    exit 1
  fi
  BACK_IMAGE_REPO="$PROJECT_SLUG-back"
  FRONT_IMAGE_REPO="$PROJECT_SLUG-front"
}

# The root-owned helpers (certbot, Apache reload) live where the installer put them, and the agent
# says where that is. An agent older than the release that publishes AGENT_HELPERS_DIR does not; its
# helpers are in the installer's historical directory, which is therefore the only fallback.
AGENT_HELPERS_DIR="${AGENT_HELPERS_DIR:-/usr/local/lib/aetheus}"
case "$AGENT_HELPERS_DIR" in
  *..*) echo "AGENT_HELPERS_DIR must not contain a traversal: $AGENT_HELPERS_DIR" >&2; exit 1 ;;
  /*) ;;
  *) echo "AGENT_HELPERS_DIR must be an absolute path: $AGENT_HELPERS_DIR" >&2; exit 1 ;;
esac
