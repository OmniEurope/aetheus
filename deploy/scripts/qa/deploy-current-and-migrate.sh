# SPDX-License-Identifier: EUPL-1.2
#
# Brings the V binaries up on the database the V-1 baseline left behind, which is what applies the
# forward migrations, health-gates the result, and captures the resulting migration history as the
# schema V snapshot the rest of the QA stage compares against.
#
# This was 20 lines inlined in .pipeline/aetheus-qa.yaml. The body is relocated unchanged apart from
# QA_PORT_BACK, noted below.
#
# Inputs, all read from the environment (none positional):
#   BUILD_PROJECTNAME  the project name the QA stack, database and user are derived from
#   WORKSPACE      the run's checkout, holding .qa-current-commit and .env-qa-rollback
#   BUILD_BUILDID  the run id the disposable QA project names are derived from
#   QA_PORT_BACK   the run-scoped backend port this QA stack listens on. It was written `$(...)` in
#                  the YAML, which the control plane substitutes in a `shell:` block; inside a script
#                  that same text is a shell command substitution, so the caller passes it by name.
set -eu
RUN="$BUILD_BUILDID"
. deploy/scripts/qa/qa-stack-identity.sh
STACK="$(qa_stack "$RUN")"
CURRENT="$(cat "$WORKSPACE/.qa-current-commit")"
export AETHEUS_BACK_IMAGE="aetheus-back:${CURRENT}"
export AETHEUS_FRONT_IMAGE="aetheus-front:${CURRENT}"
export SOURCE_COMMIT="$CURRENT"
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
docker exec "${STACK}-database" psql -U "$QA_DB_USER" -d "$QA_DB_NAME" -Atc \
  'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId"' > "$WORKSPACE/.qa-schema-v"
test -s "$WORKSPACE/.qa-schema-v"
