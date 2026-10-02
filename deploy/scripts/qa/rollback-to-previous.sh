# SPDX-License-Identifier: EUPL-1.2
#
# Puts the V-1 binaries back on the schema V produced, health-gates them, and proves the migration
# history did not move. Then confronts the two schema states it observed on a live database with what
# the sealed delivery contract declares, so the deployment gate rests on a proven schema path rather
# than on a derivation nobody checked. In bootstrap mode there is no V-1 to restore, and it attests
# that instead.
#
# This was 45 lines inlined in .pipeline/aetheus-qa.yaml. The body is relocated unchanged.
#
# Inputs, all read from the environment (none positional):
#   BUILD_PROJECTNAME  the project name the QA stack, database and user are derived from
#   WORKSPACE      the run's checkout, holding the schema snapshots this step compares
#   BUILD_BUILDID  the run id the disposable QA project names are derived from
#   QA_PORT_BACK   the run-scoped backend port this QA stack listens on. It was written `$(...)` in
#                  the YAML, which the control plane substitutes in a `shell:` block; inside a script
#                  that same text is a shell command substitution, so the caller passes it by name.
set -eu
RUN="$BUILD_BUILDID"
. deploy/scripts/qa/qa-stack-identity.sh
STACK="$(qa_stack "$RUN")"
if [ "$(cat "$WORKSPACE/.qa-bootstrap-mode")" = 1 ]; then
  docker exec "${STACK}-database" psql -U "$QA_DB_USER" -d "$QA_DB_NAME" -Atc \
    'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId"' > "$WORKSPACE/.qa-schema-after-bootstrap"
  cmp "$WORKSPACE/.qa-schema-v" "$WORKSPACE/.qa-schema-after-bootstrap" || {
    echo "Database migration history changed during bootstrap validation." >&2; exit 1;
  }
  echo "Bootstrap attestation: schema V stayed stable; no V-1 binary exists to restore on the first release."
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
docker exec "${STACK}-database" psql -U "$QA_DB_USER" -d "$QA_DB_NAME" -Atc \
  'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId"' > "$WORKSPACE/.qa-schema-after-rollback"
cmp "$WORKSPACE/.qa-schema-v" "$WORKSPACE/.qa-schema-after-rollback" || {
  echo "Database migration history changed during binary rollback." >&2; exit 1;
}
echo "Binary V-1 is healthy and database schema remains at V."
# Both schema states have now been read from a live database: the one V-1 ran on, and the
# one migrating to V produced. Confront them with what the sealed contract declares, so the
# deploy gate rests on a proven schema path rather than on a derivation nobody checked.
CONTRACT=".pipeline-artifacts/delivery-contract.json"
if test -s "$CONTRACT"; then
  NODE="$(sh deploy/scripts/ensure-node-runtime.sh)"
  BEFORE="-"
  if test -s "$WORKSPACE/.qa-schema-before"; then BEFORE="$WORKSPACE/.qa-schema-before"; fi
  "$NODE" deploy/scripts/verify-schema-contract.mjs \
    "$CONTRACT" "$BEFORE" "$WORKSPACE/.qa-schema-v"
else
  echo "No delivery contract in this workspace; schema states cannot be confronted." >&2
  exit 1
fi
