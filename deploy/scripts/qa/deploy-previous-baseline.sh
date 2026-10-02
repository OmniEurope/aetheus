# SPDX-License-Identifier: EUPL-1.2
#
# Brings up the V-1 stack the N/N-1 qualification runs against, health-gates it, and captures the
# migration history of the live V-1 database. That capture is the point of the step: the delivery
# contract DECLARES schemaBefore from the source tree, and reading it back from a running V-1
# database is what lets ValidateSchemaContract prove the declaration instead of trusting it.
#
# In bootstrap mode there is no compatible V-1, so there is nothing to deploy and it attests that.
#
# This was 28 lines inlined in .pipeline/aetheus-qa.yaml. The body is relocated unchanged apart from
# QA_PORT_BACK, noted below.
#
# Inputs, all read from the environment (none positional):
#   BUILD_PROJECTNAME  the project name the QA stack, database and user are derived from
#   WORKSPACE      the run's checkout, holding .qa-bootstrap-mode, .qa-previous-commit, .env-qa-rollback
#   BUILD_BUILDID  the run id the disposable QA project names are derived from
#   QA_PORT_BACK   the run-scoped backend port this QA stack listens on. It was written `$(...)` in
#                  the YAML, which the control plane substitutes in a `shell:` block; inside a script
#                  that same text is a shell command substitution, so the caller passes it by name.
set -eu
RUN="$BUILD_BUILDID"
. deploy/scripts/qa/qa-stack-identity.sh
STACK="$(qa_stack "$RUN")"
if [ "$(cat "$WORKSPACE/.qa-bootstrap-mode")" = 1 ]; then
  echo "Bootstrap attestation: no compatible V-1 exists, so there is no baseline binary to deploy."
  echo "The next stage will create a clean database directly with V."
  exit 0
fi
PREVIOUS="$(cat "$WORKSPACE/.qa-previous-commit")"
export AETHEUS_BACK_IMAGE="aetheus-back:${PREVIOUS}"
export AETHEUS_FRONT_IMAGE="aetheus-front:${PREVIOUS}"
export SOURCE_COMMIT="$PREVIOUS"
compose() {
  docker compose -p "$STACK" --env-file "$WORKSPACE/.env-qa-rollback" \
    -f deploy/compose/remote.compose.yml "$@"
}
if ! compose up -d --wait; then
  compose ps || true
  compose logs --no-color --tail=200 back database front || true
  exit 1
fi
curl -fsS "http://127.0.0.1:${QA_PORT_BACK}/health/live" >/dev/null
# The state the forward migration is about to run from. The delivery contract declares this
# as schemaBefore from the source tree; capturing it here from a live V-1 database is what
# lets ValidateSchemaContract prove the declaration instead of trusting it.
docker exec "${STACK}-database" psql -U "$QA_DB_USER" -d "$QA_DB_NAME" -Atc \
  'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId"' > "$WORKSPACE/.qa-schema-before"
test -s "$WORKSPACE/.qa-schema-before"
