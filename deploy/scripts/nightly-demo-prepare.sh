#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
# Everything the persistent demo environment needs BEFORE the native blue-green steps act on it.
#
# The cutover itself - migrate, start the idle colour, move traffic, probe, commit, roll back - now
# belongs to the `bluegreen-*` and `smoke` step types through the host-bluegreen-deploy template.
# What stays here is what those types deliberately do not know about, because it is specific to this
# environment rather than to blue-green deployment:
#
#   1. the demo identity contract (M-052), refused before anything is touched;
#   2. TLS and the HTTPS vhost, which must exist before traffic can be pointed anywhere;
#   3. the demo secrets, which come from the Vault and are materialised into the environment file the
#      typed steps then read;
#   4. the payload, verified against its independent evidence and loaded as images tagged by revision;
#   5. the run variables the Compose files interpolate, published for `compose_env:`.
#
# It performs no cutover and holds no transaction: a failure here leaves the live demo untouched.
set -eu

fail() {
  echo "FATAL: $*" >&2
  exit 1
}

# --- 1. Demo identity (M-052). A production token anywhere here ends the run. --------------------
[ "${NIGHTLY_DEPLOY_TARGET:-}" = demo ] || fail "Nightly can target only demo."
[ "${DEMO_STATE_DIR:-}" = /var/lib/aetheus-demo ] || fail "Unexpected demo state directory."
[ "${DEMO_ENV_FILE:-}" = /var/lib/aetheus-demo/.env-demo ] || fail "Unexpected demo environment file."
[ "${DEMO_SECRETS_FILE:-}" = /var/lib/aetheus-demo/.secrets-demo ] || fail "Unexpected demo secrets file."
[ "${DEMO_COMPOSE_PROJECT:-}" = aetheus-demo ] || fail "Unexpected demo Compose project."
[ "${DEMO_COMPOSE_BASE:-}" = deploy/compose/remote-bluegreen.compose.yml ] || fail "Unexpected demo base Compose file."
[ "${DEMO_COMPOSE_OVERRIDE:-}" = deploy/compose/demo-bluegreen.override.yml ] || fail "Unexpected demo Compose override."
[ "${DEMO_HOST:-}" = demo.aetheus.sonytumen.com ] || fail "Unexpected demo hostname."
[ "${PORT_FRONT_BLUE:-}" = 10029 ] || fail "Unexpected demo blue front port."
[ "${PORT_BACK_BLUE:-}" = 10030 ] || fail "Unexpected demo blue back port."
# 10031/10032 were given up to portfolio-prod-front, a different project holding 10031 on this host.
# These stay pinned: the guard exists so the demo can never reach for a production port by accident.
[ "${PORT_FRONT_GREEN:-}" = 10033 ] || fail "Unexpected demo green front port."
[ "${PORT_BACK_GREEN:-}" = 10034 ] || fail "Unexpected demo green back port."
[ "${DEMO_APACHE_HTTPS_CONF:-}" = /etc/apache2/sites-available/demo.aetheus.sonytumen.com-ssl.conf ] || fail "Unexpected demo Apache vhost."
case "${DEMO_STATE_DIR}|${DEMO_ENV_FILE}|${DEMO_COMPOSE_PROJECT}|${DEMO_HOST}" in
  *prod*) fail "A demo identity contains a production token." ;;
esac

WORKSPACE="${WORKSPACE:?WORKSPACE is required}"
SOURCE_SHA="${BUILD_SOURCEVERSION:?BUILD_SOURCEVERSION is required}"
[ "${#SOURCE_SHA}" -eq 40 ] || fail "Nightly source SHA must contain exactly 40 characters."
case "$SOURCE_SHA" in *[!0-9a-fA-F]*) fail "Nightly source SHA is not hexadecimal." ;; esac

for command_name in awk basename cat chmod cp curl date docker grep gzip install ln mv openssl rm sed \
  stat sudo tail tr; do
  command -v "$command_name" >/dev/null || fail "$command_name is required to prepare the demo."
done
docker --version
# No Compose probe here: this script never orchestrates Compose. The typed cutover steps check the
# daemon themselves before they touch the environment.
[ -f "$WORKSPACE/$DEMO_COMPOSE_BASE" ] || fail "Demo base Compose file is missing."
[ -f "$WORKSPACE/$DEMO_COMPOSE_OVERRIDE" ] || fail "Demo Compose override is missing."
DEMO_VHOST_TEMPLATE="$WORKSPACE/.pipeline/configs/apache/demo-https.conf"
[ -f "$DEMO_VHOST_TEMPLATE" ] || fail "Versioned demo HTTPS vhost template is missing."

if [ ! -d "$DEMO_STATE_DIR" ]; then
  install -d -m 700 "$DEMO_STATE_DIR"
  echo ">>> Created demo state directory $DEMO_STATE_DIR"
fi

# --- 2. TLS and the HTTPS vhost ------------------------------------------------------------------
CERT_LIVE_DIR="/etc/letsencrypt/live/$DEMO_HOST"
if [ ! -f "$CERT_LIVE_DIR/fullchain.pem" ]; then
  echo ">>> Provisioning TLS certificate for $DEMO_HOST via certbot helper"
  AETHEUS_CERTBOT_DOMAINS="$DEMO_HOST" \
    AETHEUS_CERTBOT_EMAIL="${AETHEUS_CERTBOT_EMAIL:-}" \
    AETHEUS_CERTBOT_MODE="production" \
    sudo -n /usr/local/lib/aetheus/aetheus-certbot-issue "$DEMO_HOST" \
    || fail "Certbot certificate issuance failed for $DEMO_HOST."
fi
if [ -f "$CERT_LIVE_DIR/fullchain.pem" ]; then
  CERT_EXPIRY_EPOCH="$(date -d "$(openssl x509 -enddate -noout -in "$CERT_LIVE_DIR/fullchain.pem" \
    | sed 's/notAfter=//')" +%s 2>/dev/null || echo 0)"
  NOW_EPOCH="$(date +%s)"
  CERT_DAYS_LEFT=$(( (CERT_EXPIRY_EPOCH - NOW_EPOCH) / 86400 ))
  if [ "$CERT_DAYS_LEFT" -lt 30 ]; then
    echo ">>> Certificate expires in $CERT_DAYS_LEFT days; renewing via certbot helper"
    sudo -n /usr/local/lib/aetheus/aetheus-certbot-manage renew "$DEMO_HOST" || true
  fi
fi

# The switch step renders the upstream template itself and substitutes only the port and colour
# placeholders, by design: it is generic and knows nothing about this host. The hostname is therefore
# resolved here, into a run-local file the step is then pointed at. The port placeholders MUST
# survive - a template with nothing left to substitute would make the switch reload a configuration
# already in place and report a cutover that never happened.
RUN_VHOST_TEMPLATE="$WORKSPACE/.nightly-demo-upstream.conf"
sed -e "s/#{DEMO_HOST}#/$DEMO_HOST/g" "$DEMO_VHOST_TEMPLATE" > "$RUN_VHOST_TEMPLATE"
grep -q '#{FRONT_PORT}#' "$RUN_VHOST_TEMPLATE" || fail "The demo vhost template lost its front-port placeholder."
grep -q '#{BACK_PORT}#' "$RUN_VHOST_TEMPLATE" || fail "The demo vhost template lost its back-port placeholder."
grep -q '#{DEMO_HOST}#' "$RUN_VHOST_TEMPLATE" && fail "The demo hostname was not resolved."

# Apache must already serve this vhost before any colour can be switched to, so the very first run
# bootstraps it on the blue ports. Later runs leave it alone: it is live state the switch owns.
if [ ! -f "$DEMO_APACHE_HTTPS_CONF" ]; then
  echo ">>> Rendering the initial demo HTTPS vhost on the blue ports"
  VHOST_TMP="$DEMO_APACHE_HTTPS_CONF.bootstrap.$$"
  sed \
    -e "s/#{FRONT_PORT}#/$PORT_FRONT_BLUE/g" \
    -e "s/#{BACK_PORT}#/$PORT_BACK_BLUE/g" \
    -e "s/#{COLOR}#/blue/g" \
    -e "s/#{ACTIVE_COLOR}#/blue/g" \
    "$RUN_VHOST_TEMPLATE" > "$VHOST_TMP"
  if grep -q '#{' "$VHOST_TMP"; then
    rm -f "$VHOST_TMP"
    fail "Demo Apache template contains an unresolved token."
  fi
  mv "$VHOST_TMP" "$DEMO_APACHE_HTTPS_CONF"
  DEMO_ENABLED_LINK="/etc/apache2/sites-enabled/$(basename "$DEMO_APACHE_HTTPS_CONF")"
  [ -L "$DEMO_ENABLED_LINK" ] || ln -s "$DEMO_APACHE_HTTPS_CONF" "$DEMO_ENABLED_LINK"
  sudo -n /usr/local/lib/aetheus/aetheus-apache-reload || fail "Apache rejected the initial demo vhost."
fi
[ ! -L "$DEMO_APACHE_HTTPS_CONF" ] || fail "The demo HTTPS vhost must not be symbolic."

# --- 3. Demo secrets come from the Vault, never from this host -----------------------------------
# Generating them here made the host the source of truth: the values existed nowhere else, could not
# be rotated without wiping the environment, and no other project could reuse the pattern. Missing or
# malformed values fail the run rather than falling back to a local `openssl rand`, because a silent
# fallback would recreate exactly the situation this replaces.
for vault_secret in DB_PASSWORD JWT_KEY ENCRYPTION_KEY ENCRYPTION_SALT; do
  eval "vault_value=\${$vault_secret:-}"
  [ -n "$vault_value" ] \
    || fail "$vault_secret is not provided by the aetheus-demo-secrets Vault; refusing to deploy the demo."
  case "$vault_value" in
    *[!0-9a-fA-F]*) fail "$vault_secret must be a hexadecimal string." ;;
  esac
done
[ "${#ENCRYPTION_KEY}" -ge 32 ] || fail "ENCRYPTION_KEY is too short."
[ "${#ENCRYPTION_SALT}" -ge 16 ] || fail "ENCRYPTION_SALT is too short."

# The persisted database is encrypted with the values it was created under, so a rotated Vault makes
# it unreadable. The demo is disposable and reseeded on every bootstrap, so it is recreated rather
# than left in a state nothing can open.
if [ -f "$DEMO_SECRETS_FILE" ]; then
  for vault_secret in DB_PASSWORD ENCRYPTION_KEY ENCRYPTION_SALT; do
    eval "vault_value=\${$vault_secret}"
    persisted_value="$(sed -n "s/^${vault_secret}=//p" "$DEMO_SECRETS_FILE" | tail -n 1)"
    if [ "$vault_value" != "$persisted_value" ]; then
      echo ">>> $vault_secret differs from the persisted demo secret; the existing demo data cannot be read."
      rm -f "$DEMO_SECRETS_FILE" "$DEMO_ENV_FILE"
      break
    fi
  done
fi

if [ ! -f "$DEMO_SECRETS_FILE" ]; then
  # Secrets gone while the volumes remain means the state directory was wiped under a live database.
  # The regenerated DB_PASSWORD would not match the existing PostgreSQL volume and the regenerated
  # ENCRYPTION_KEY could not read the existing rows, so the two can never be reconciled.
  #
  # Production fails closed here because its data is irreplaceable. The demo is the opposite: public,
  # disposable and re-seeded on every bootstrap, so recreating it beats leaving the environment
  # permanently unbootstrappable. The reset is bounded to the five aetheus-demo volumes and to
  # containers carrying that exact prefix; the identity guards above have already refused any project
  # or path containing a production token.
  ORPHANED_VOLUMES=""
  for vol in db-data git-repos dp-keys artifacts packages; do
    if docker volume inspect "aetheus-demo-$vol" >/dev/null 2>&1; then
      ORPHANED_VOLUMES="$ORPHANED_VOLUMES aetheus-demo-$vol"
    fi
  done
  if [ -n "$ORPHANED_VOLUMES" ]; then
    echo ">>> Demo secrets are missing while persistent volumes remain; resetting the disposable demo."
    echo ">>> Volumes to recreate:$ORPHANED_VOLUMES"
    for container in $(docker ps -a --format '{{.Names}}' | grep -E '^aetheus-demo-' || true); do
      case "$container" in
        aetheus-demo-*) docker rm -f "$container" >/dev/null 2>&1 || true ;;
        *) fail "Refusing to remove a container outside the demo project: $container" ;;
      esac
    done
    # shellcheck disable=SC2086
    docker volume rm $ORPHANED_VOLUMES >/dev/null || fail "Could not remove the orphaned demo volumes."
    rm -f "$DEMO_STATE_DIR/live-color" "$DEMO_STATE_DIR/source-commit" "$DEMO_STATE_DIR/nightly-metrics"
    rm -rf "$DEMO_STATE_DIR/deployment-transaction"
    echo ">>> Demo reset complete; a fresh seeded environment will be created."
  fi
  echo ">>> Materialising the demo secrets from the Vault at $DEMO_SECRETS_FILE"
  OLD_UMASK="$(umask)"
  umask 177
  SECRETS_TMP="$DEMO_SECRETS_FILE.first-deploy.$$"
  # ADMIN_PASSWORD is not a secret and is deliberately not in the Vault: the public demo advertises
  # this credential. Keeping it inline is what stops it being mistaken for one that must be protected.
  cat > "$SECRETS_TMP" <<SECRETS_EOF
DB_PASSWORD=$DB_PASSWORD
JWT_KEY=$JWT_KEY
ADMIN_PASSWORD=aetheus-demo
ENCRYPTION_KEY=$ENCRYPTION_KEY
ENCRYPTION_SALT=$ENCRYPTION_SALT
SECRETS_EOF
  chmod 600 "$SECRETS_TMP"
  mv "$SECRETS_TMP" "$DEMO_SECRETS_FILE"
  umask "$OLD_UMASK"
fi

if [ ! -f "$DEMO_ENV_FILE" ]; then
  echo ">>> First demo deploy: generating persistent environment at $DEMO_ENV_FILE"
  BOOT_DB_PASSWORD="$(sed -n 's/^DB_PASSWORD=//p' "$DEMO_SECRETS_FILE" | tail -n 1)"
  BOOT_JWT_KEY="$(sed -n 's/^JWT_KEY=//p' "$DEMO_SECRETS_FILE" | tail -n 1)"
  BOOT_ADMIN_PASSWORD="$(sed -n 's/^ADMIN_PASSWORD=//p' "$DEMO_SECRETS_FILE" | tail -n 1)"
  BOOT_ENCRYPTION_KEY="$(sed -n 's/^ENCRYPTION_KEY=//p' "$DEMO_SECRETS_FILE" | tail -n 1)"
  BOOT_ENCRYPTION_SALT="$(sed -n 's/^ENCRYPTION_SALT=//p' "$DEMO_SECRETS_FILE" | tail -n 1)"
  OLD_UMASK="$(umask)"
  umask 177
  ENV_TMP="$DEMO_ENV_FILE.first-deploy.$$"
  cat > "$ENV_TMP" <<ENV_EOF
APPNAME=aetheus
ENV=demo
DB_NAME=aetheus_demo
DB_USER=aetheus
DB_PASSWORD=$BOOT_DB_PASSWORD
JWT_KEY=$BOOT_JWT_KEY
ADMIN_USER=admin
ADMIN_PASSWORD=$BOOT_ADMIN_PASSWORD
ENCRYPTION_KEY=$BOOT_ENCRYPTION_KEY
ENCRYPTION_SALT=$BOOT_ENCRYPTION_SALT
DEPLOYMENT_BOOTSTRAP_IDENTITY_KEY=$(openssl rand -hex 32)
API_BASE_URL=https://$DEMO_HOST
FRONT_URL=https://$DEMO_HOST
FRONT_URL_ACCEPT=https://$DEMO_HOST
FRONT_URL_PROD=https://$DEMO_HOST
AETHEUS_PUBLIC_DEMO=true
SEED_DEMO=true
ENV_EOF
  chmod 600 "$ENV_TMP"
  mv "$ENV_TMP" "$DEMO_ENV_FILE"
  umask "$OLD_UMASK"
fi

[ ! -L "$DEMO_ENV_FILE" ] && [ ! -L "$DEMO_SECRETS_FILE" ] || fail "Demo secret files must not be symbolic."

# Back-fill, not regeneration. remote-bluegreen.compose.yml interpolates
# DEPLOYMENT_BOOTSTRAP_IDENTITY_KEY with no default, and only the production path ever wrote it, so on
# the demo host it resolved to an empty string: the deployed backend could not re-derive the throwaway
# smoke account and answered the blocking probe with 401 on a deployment that was otherwise healthy.
# That is what failed nightly run 1204. Appending a key that never existed is safe in a way rotating
# one is not - it decrypts nothing, so no stored row and no live session depends on its old value.
if ! grep -q "^DEPLOYMENT_BOOTSTRAP_IDENTITY_KEY=.\+" "$DEMO_ENV_FILE"; then
  echo ">>> Back-filling DEPLOYMENT_BOOTSTRAP_IDENTITY_KEY in $DEMO_ENV_FILE"
  OLD_UMASK="$(umask)"
  umask 177
  ENV_BACKFILL_TMP="$DEMO_ENV_FILE.backfill.$$"
  { cat "$DEMO_ENV_FILE"; echo "DEPLOYMENT_BOOTSTRAP_IDENTITY_KEY=$(openssl rand -hex 32)"; } > "$ENV_BACKFILL_TMP"
  chmod 600 "$ENV_BACKFILL_TMP"
  mv "$ENV_BACKFILL_TMP" "$DEMO_ENV_FILE"
  umask "$OLD_UMASK"
fi

env_value() {
  sed -n "s/^$1=//p" "$DEMO_ENV_FILE" | tail -n 1
}

[ "$(env_value APPNAME)" = aetheus ] || fail "Demo APPNAME is invalid."
[ "$(env_value ENV)" = demo ] || fail "Demo ENV is invalid."
[ "$(env_value DB_NAME)" = aetheus_demo ] || fail "Demo database name is invalid."
[ "$(env_value DB_USER)" = aetheus ] || fail "Demo database user is invalid."
[ "$(env_value API_BASE_URL)" = "https://$DEMO_HOST" ] || fail "Demo API URL is invalid."
[ "$(env_value FRONT_URL)" = "https://$DEMO_HOST" ] || fail "Demo front URL is invalid."
[ "$(env_value AETHEUS_PUBLIC_DEMO)" = true ] || fail "Public demo mode is not enabled."
[ "$(env_value SEED_DEMO)" = true ] || fail "Demo seed is not enabled."
[ "$(sed -n 's/^ADMIN_PASSWORD=//p' "$DEMO_SECRETS_FILE" | tail -n 1)" = aetheus-demo ] \
  || fail "The intentionally public demo credential is invalid."
for secret_name in DB_PASSWORD JWT_KEY ENCRYPTION_KEY ENCRYPTION_SALT; do
  grep -Eq "^${secret_name}=[0-9a-f]+$" "$DEMO_SECRETS_FILE" || fail "Demo secret $secret_name is missing or invalid."
  [ "$(env_value "$secret_name")" = "$(sed -n "s/^${secret_name}=//p" "$DEMO_SECRETS_FILE" | tail -n 1)" ] \
    || fail "Demo environment and persistent secret $secret_name differ."
done
[ "$(env_value ADMIN_PASSWORD)" = aetheus-demo ] || fail "Demo environment uses an unexpected admin credential."
# Checked here so a missing key fails this step with a name, instead of surfacing three stages later
# as an unexplained 401 from the blocking smoke.
grep -Eq "^DEPLOYMENT_BOOTSTRAP_IDENTITY_KEY=[0-9a-f]{64}$" "$DEMO_ENV_FILE" \
  || fail "DEPLOYMENT_BOOTSTRAP_IDENTITY_KEY is missing or invalid in $DEMO_ENV_FILE."
DEMO_ADMIN_USER="$(env_value ADMIN_USER)"
[ -n "$DEMO_ADMIN_USER" ] || fail "Demo admin user is missing from the environment file."

# --- 4. Payload: verified against its independent evidence, then loaded --------------------------
ARTIFACT_DIR="$WORKSPACE/.pipeline-artifacts"
EVIDENCE_DIR="$WORKSPACE/.nightly-evidence"
ARTIFACT_SOURCE="$(tr -d '\r\n' < "$ARTIFACT_DIR/source-commit")"
[ "$ARTIFACT_SOURCE" = "$SOURCE_SHA" ] || fail "Nightly payload belongs to another source commit."
NODE="$(sh "$WORKSPACE/deploy/scripts/ensure-node-runtime.sh")"
"$NODE" "$WORKSPACE/deploy/scripts/nightly-evidence.mjs" verify \
  "$SOURCE_SHA" "$EVIDENCE_DIR/nightly-evidence.json" \
  "$ARTIFACT_DIR/aetheus-back.tar.gz" \
  "$ARTIFACT_DIR/aetheus-front.tar.gz" \
  "$ARTIFACT_DIR/aetheus-vitrine.tar.gz" \
  "$ARTIFACT_DIR/aetheus-browser-smoke.tar.gz"
gzip -t "$ARTIFACT_DIR/aetheus-back.tar.gz"
gzip -t "$ARTIFACT_DIR/aetheus-front.tar.gz"
gzip -t "$ARTIFACT_DIR/aetheus-browser-smoke.tar.gz"

gzip -dc "$ARTIFACT_DIR/aetheus-back.tar.gz" | docker load
gzip -dc "$ARTIFACT_DIR/aetheus-front.tar.gz" | docker load
gzip -dc "$ARTIFACT_DIR/aetheus-browser-smoke.tar.gz" | docker import \
  --change 'ENV DOTNET_ROOT=/usr/lib/dotnet-10' \
  --change 'ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright' \
  --change 'WORKDIR /src' \
  --change 'USER pwuser' \
  --change "LABEL org.opencontainers.image.revision=$SOURCE_SHA" \
  - "aetheus-nightly-browser-smoke:$SOURCE_SHA" >/dev/null

AETHEUS_BACK_IMAGE="aetheus-back:$SOURCE_SHA"
AETHEUS_FRONT_IMAGE="aetheus-front:$SOURCE_SHA"
AETHEUS_BROWSER_SMOKE_IMAGE="aetheus-nightly-browser-smoke:$SOURCE_SHA"
for image in "$AETHEUS_BACK_IMAGE" "$AETHEUS_FRONT_IMAGE" "$AETHEUS_BROWSER_SMOKE_IMAGE"; do
  [ "$(docker image inspect --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' "$image")" = "$SOURCE_SHA" ] \
    || fail "Image revision is invalid: $image"
done

# --- 5. Run variables the Compose files and the smoke step interpolate ---------------------------
# These are published, not exported: the typed steps that follow run as separate tasks in separate
# processes, and a `compose_env:` entry the run does not define is refused before a task is created.
#
# Every value below is public by construction. A `setvariable` directive is echoed into the run log,
# so a run-scoped bootstrap password could not travel this way without being published with it. The
# demo authenticates its smoke with the credential it already advertises instead: the environment is
# public, disposable and reseeded, which is precisely why that credential is not a secret.
echo "##aetheus[setvariable name=AETHEUS_BACK_IMAGE]$AETHEUS_BACK_IMAGE"
echo "##aetheus[setvariable name=AETHEUS_FRONT_IMAGE]$AETHEUS_FRONT_IMAGE"
echo "##aetheus[setvariable name=SOURCE_COMMIT]$SOURCE_SHA"
echo "##aetheus[setvariable name=AETHEUS_SMOKE_ADMIN_USER]$DEMO_ADMIN_USER"
echo "##aetheus[setvariable name=AETHEUS_SMOKE_ADMIN_PASSWORD]aetheus-demo"
echo "Demo host prepared for $SOURCE_SHA; the cutover steps own the environment from here."
