# SPDX-License-Identifier: EUPL-1.2
#
# The deployment gate. Verifies the candidate's sealed delivery and assurance contracts, confronts
# the schema states they declare with the schema production is actually on. Nothing is deployed
# before this passes. The nightly is independent of the deployment and is not read here (PLAN-007).
#
# This was 48 lines inlined in .pipeline/aetheus-deploy-prod.yaml, which is the last place a check
# this consequential should live: a definition is not versioned, not tested and not reusable. The
# body is relocated unchanged.
#
# Inputs, all read from the environment (none positional):
#   WORKSPACE                       the run's checkout (required, used through the working directory)
#   POSTGRES_DB                     the production database this deployment migrates
#   POSTGRES_USER                   the role used to read __EFMigrationsHistory
#   COMPOSE_PROJECT                 names the running database container to read the live schema from
#   AETHEUS_DEPLOY_MINIMUM_GRADE    the worst assurance grade this deployment accepts
#
# The last two were written `$(...)` in the YAML, which the control plane substitutes inside a
# `shell:` block. In a script that same text is a shell command substitution, so the caller passes
# them by name instead.
set -eu
# The gate no longer pins the candidate to the identity of the release it was qualified
# against; it checks that production stands on one of the two schema states its QA actually
# exercised. So read the live schema here, before any Migrate step has touched it.
# DB_USER and DB_NAME are resolved inside the container, so no credential reaches the log.
PROD_SCHEMA="-"
if docker ps --format '{{.Names}}' | grep -qx "${COMPOSE_PROJECT}-database"; then
  docker exec "${COMPOSE_PROJECT}-database" sh -c \
    'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atc '"'"'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId"'"'"'' \
    > .prod-schema-state 2>/dev/null || {
      echo "The production database is up but its migration history could not be read." >&2
      exit 1
    }
  PROD_SCHEMA=".prod-schema-state"
  echo "Live production schema: $(wc -l < .prod-schema-state | tr -d ' ') migrations applied."
else
  echo "No production database container is running; treating this as a first deployment."
fi
# The image comes from the release; these deployment instructions come from this branch.
# Refuse the run when the branch does not contain the release commit, because everything the
# release added to the contract (a BG_COMPOSE_ENV name, a Compose variable, a migration step)
# would be dropped while the run still reported success.
sh deploy/scripts/verify-release-ancestry.sh .pipeline-artifacts/source-commit HEAD
NODE="$(sh deploy/scripts/ensure-node-runtime.sh)"
BROWSER_SMOKE_ARCHIVE=".pipeline-artifacts/aetheus-browser-smoke.tar.gz"
# Restored whole (BrowserRuntime-artifacts) into its own directory, so the candidate's provenance
# files already in .pipeline-artifacts are not overwritten by the copies it carries.
mv .browser-runtime/.pipeline-artifacts/aetheus-browser-smoke.tar.gz "$BROWSER_SMOKE_ARCHIVE"
# No `gzip -t` here any more (PLAN-007 lot 6): verify-delivery-promotion.mjs below recomputes the
# SHA-256 of this very archive against the digest sealed in CI, where the archive was fully
# decompressed and listed. Byte identity is strictly stronger than a second decompression.
# The contract of the release production runs, restored from `current-deployed`. It is what lets the
# gate accept the release deployed before it (PLAN-007 lot 5); absent on a first deployment, or when
# the live release left no payload, and then that branch simply does not apply.
LIVE_CONTRACT=".delivery-live/.pipeline-artifacts/delivery-contract.json"
test -s "$LIVE_CONTRACT" || LIVE_CONTRACT="-"
"$NODE" deploy/scripts/verify-delivery-promotion.mjs \
  .pipeline-artifacts/delivery-contract.json \
  "$PROD_SCHEMA" \
  .pipeline-artifacts \
  "$LIVE_CONTRACT"
"$NODE" deploy/scripts/verify-candidate-assurance-contract.mjs \
  .pipeline-artifacts/assurance-contract.json \
  .pipeline-artifacts/source-commit \
  "${AETHEUS_DEPLOY_MINIMUM_GRADE}" \
  --require-deployment-readiness
