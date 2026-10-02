# SPDX-License-Identifier: EUPL-1.2
#
# Destroys the disposable QA stacks this run created and removes the run-scoped state files it left
# in the workspace. Every QA environment is per-run and must not outlive it: a leaked Compose project
# holds ports and volumes that the next run's port derivation assumes are free.
#
# This was 31 lines inlined in .pipeline/aetheus-qa.yaml. The body is relocated unchanged.
#
# Inputs, all read from the environment (none positional):
#   BUILD_PROJECTNAME  the project name the QA stack, database and user are derived from
#   WORKSPACE            the run's checkout, holding the run-scoped marker files
#   BUILD_BUILDID        the run id every disposable Compose project name is derived from
#   BUILD_SOURCEVERSION  the revision this run qualified
set -eu
RUN="${BUILD_BUILDID:-}"
case "$RUN" in ''|*[!0-9]*) echo "BUILD_BUILDID is invalid." >&2; exit 1 ;; esac
. deploy/scripts/qa/qa-stack-identity.sh
STACK="$(qa_stack "$RUN")"
if [ -f "$WORKSPACE/.env-qa-rollback" ]; then
  # Compose interpolates every build argument even for `down`. The value is not consumed
  # during teardown, but keeping it defined prevents a misleading configuration warning.
  export SOURCE_COMMIT="${BUILD_SOURCEVERSION:-teardown}"
  docker compose -p "$STACK" --env-file "$WORKSPACE/.env-qa-rollback" \
    -f deploy/compose/remote.compose.yml down -v --remove-orphans
fi
PROJECT="$STACK"
test -z "$(docker ps -aq --filter "label=com.docker.compose.project=${PROJECT}")"
test -z "$(docker network ls -q --filter "label=com.docker.compose.project=${PROJECT}")"
test -z "$(docker volume ls -q --filter "label=com.docker.compose.project=${PROJECT}")"
# The two browser-smoke images this run imported (current and N-1) are single-use: drop
# them before the marker files that name them disappear, then bound what earlier runs
# that never reached teardown left behind. Removal tolerates an image already gone; the
# retention script is the strict pass.
for MARKER in "$WORKSPACE/.qa-current-commit" "$WORKSPACE/.qa-previous-browser-commit"; do
  if [ -s "$MARKER" ]; then
    docker image rm "aetheus-browser-smoke:$(cat "$MARKER")" >/dev/null 2>&1 || true
  fi
done
sh deploy/scripts/retain-docker-images.sh aetheus-browser-smoke
rm -rf "$WORKSPACE/.nminus1" "$WORKSPACE/.nminus1-source" "$WORKSPACE/.nminus1-agent-release" \
  "$WORKSPACE/.qa-test-results" "$WORKSPACE/.qa-e2e-context-"*
rm -f "$WORKSPACE/.env-qa-rollback" "$WORKSPACE/.qa-admin-pwd" \
  "$WORKSPACE/.qa-current-commit" "$WORKSPACE/.qa-previous-commit" \
  "$WORKSPACE/.qa-previous-browser-commit" "$WORKSPACE/.qa-bootstrap-mode" \
  "$WORKSPACE/.qa-previous-payload-only" \
  "$WORKSPACE/.qa-schema-v" "$WORKSPACE/.qa-schema-after-rollback" \
  "$WORKSPACE/.qa-schema-after-previous-e2e" "$WORKSPACE/.qa-schema-after-bootstrap"
