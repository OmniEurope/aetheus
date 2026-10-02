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
#   2. the Apache upstream the vhosts read their ports from, bootstrapped on the first run;
#   3. the demo secrets, which come from the Vault and are materialised into the environment file the
#      typed steps then read;
#   4. the payload, verified against its independent evidence and loaded as images tagged by revision;
#   5. the run variables the Compose files interpolate, published for `compose_env:`.
#
# PLAN-003 2.1: the demo is served like production, on two hosts (APP_HOST, API_HOST) with the same
# vhost templates, one certificate each and an upstream file of its own. The vhosts and the
# certificates are the pipeline's next stages (apache-config, ensure-tls-certificates.sh), not this
# script's. Every name comes from the aetheus.demo library; nothing here names the host.
#
# It performs no cutover and holds no transaction: a failure here leaves the live demo untouched.
set -eu

fail() {
  echo "FATAL: $*" >&2
  exit 1
}

# --- 1. Demo identity (M-052). A production token anywhere here ends the run. --------------------
#
# These are SHAPE checks, not value checks. They used to pin the exact hostname, the four host ports
# and every path, which made the Variable Library that supplies them decorative. What actually
# protects the demo is below: no identity may contain a production token, so a mis-set library can
# send the run nowhere near production.
require_set() {
  [ -n "$2" ] || fail "$1 is not set."
}
for name in STATE_DIR ENV_FILE SECRETS_FILE COMPOSE_PROJECT COMPOSE_BASE COMPOSE_OVERRIDE APP_HOST API_HOST \
  UPSTREAM_CONF UPSTREAM_LINK UPSTREAM_DEFINE PUBLIC_ADMIN_PASSWORD; do
  eval "require_set $name \"\${$name:-}\""
done

# State lives under /var/lib and nowhere else: a state directory pointing at /etc or a home directory
# is a mis-set library, not a deployment.
case "$STATE_DIR" in
  /var/lib/*/..*|/var/lib/*/*/*) fail "The demo state directory must be a direct child of /var/lib: $STATE_DIR" ;;
  /var/lib/*) ;;
  *) fail "The demo state directory must live under /var/lib: $STATE_DIR" ;;
esac
case "$ENV_FILE" in "$STATE_DIR"/*) ;; *) fail "The demo environment file must live in the demo state directory." ;; esac
case "$SECRETS_FILE" in "$STATE_DIR"/*) ;; *) fail "The demo secrets file must live in the demo state directory." ;; esac
case "$COMPOSE_BASE" in /*|*..*) fail "The demo Compose file must be a path inside the workspace." ;; esac
case "$COMPOSE_OVERRIDE" in /*|*..*) fail "The demo Compose override must be a path inside the workspace." ;; esac
for demo_host in "$APP_HOST" "$API_HOST"; do
  case "$demo_host" in
    *[!a-zA-Z0-9.-]*|.*|-*|*.) fail "Not a hostname: $demo_host" ;;
    *.*) ;;
    *) fail "The demo hostname must be fully qualified: $demo_host" ;;
  esac
done
case "$UPSTREAM_CONF" in *..*) fail "UPSTREAM_CONF must not contain a traversal." ;; */sites-available/?*) ;; *) fail "UPSTREAM_CONF must be a file in a sites-available directory: $UPSTREAM_CONF" ;; esac
case "$UPSTREAM_LINK" in *..*) fail "UPSTREAM_LINK must not contain a traversal." ;; */sites-enabled/?*) ;; *) fail "UPSTREAM_LINK must be a link in a sites-enabled directory: $UPSTREAM_LINK" ;; esac
for demo_port in "${PORT_FRONT_BLUE:-}" "${PORT_BACK_BLUE:-}" "${PORT_FRONT_GREEN:-}" "${PORT_BACK_GREEN:-}"; do
  case "$demo_port" in
    ''|*[!0-9]*) fail "A demo host port is missing or not numeric: '$demo_port'" ;;
  esac
  [ "$demo_port" -ge 1024 ] && [ "$demo_port" -le 65535 ] \
    || fail "A demo host port is outside the unprivileged range: $demo_port"
done
# The guard that matters, and the only one that was ever about safety rather than about pinning a
# value: nothing in the demo identity may name production.
case "${STATE_DIR}|${ENV_FILE}|${COMPOSE_PROJECT}|${APP_HOST}|${API_HOST}|${UPSTREAM_CONF}|${UPSTREAM_LINK}" in
  *prod*) fail "A demo identity contains a production token." ;;
esac

WORKSPACE="${WORKSPACE:?WORKSPACE is required}"
SOURCE_SHA="${BUILD_SOURCEVERSION:?BUILD_SOURCEVERSION is required}"
[ "${#SOURCE_SHA}" -eq 40 ] || fail "Nightly source SHA must contain exactly 40 characters."
case "$SOURCE_SHA" in *[!0-9a-fA-F]*) fail "Nightly source SHA is not hexadecimal." ;; esac
# shellcheck source=deploy-identity.sh
. "$WORKSPACE/deploy/scripts/deploy-identity.sh"
deploy_image_repos

for command_name in awk basename cat chmod cp date dirname docker grep gzip install ln mv openssl rm sed \
  stat tail tr; do
  command -v "$command_name" >/dev/null || fail "$command_name is required to prepare the demo."
done
docker --version
# No Compose probe here: this script never orchestrates Compose. The typed cutover steps check the
# daemon themselves before they touch the environment.
[ -f "$WORKSPACE/$COMPOSE_BASE" ] || fail "Demo base Compose file is missing."
[ -f "$WORKSPACE/$COMPOSE_OVERRIDE" ] || fail "Demo Compose override is missing."

if [ ! -d "$STATE_DIR" ]; then
  install -d -m 700 "$STATE_DIR"
  echo ">>> Created demo state directory $STATE_DIR"
fi

# --- 2. The Apache upstream ----------------------------------------------------------------------
# The switch step renders the upstream template itself and substitutes only the port and colour
# placeholders, by design: it is generic and knows nothing about this environment. The Define name is
# therefore resolved here, into a run-local copy the step is pointed at (BG_UPSTREAM_TEMPLATE).
UPSTREAM_TEMPLATE="$WORKSPACE/.pipeline/configs/apache/aetheus-upstream.conf"
sh "$WORKSPACE/deploy/scripts/render-apache-upstream.sh" --define-only \
  "$UPSTREAM_TEMPLATE" "$WORKSPACE/.bluegreen-upstream.conf"

# The vhosts the next stages apply read their ports from this file's Defines, so it must exist before
# them. The first run creates it on the colour the journal says is live (blue on a fresh host); later
# runs leave it alone: it is live state the switch owns.
if [ ! -f "$UPSTREAM_CONF" ]; then
  LIVE_COLOR="$(cat "$STATE_DIR/live-color" 2>/dev/null || echo blue)"
  case "$LIVE_COLOR" in
    green) FRONT_PORT="$PORT_FRONT_GREEN"; BACK_PORT="$PORT_BACK_GREEN" ;;
    *) LIVE_COLOR=blue; FRONT_PORT="$PORT_FRONT_BLUE"; BACK_PORT="$PORT_BACK_BLUE" ;;
  esac
  echo ">>> Creating the demo upstream on the $LIVE_COLOR ports"
  sh "$WORKSPACE/deploy/scripts/render-apache-upstream.sh" \
    "$UPSTREAM_TEMPLATE" "$UPSTREAM_CONF" "$FRONT_PORT" "$BACK_PORT" "$LIVE_COLOR"
fi
[ ! -L "$UPSTREAM_CONF" ] || fail "The demo upstream file must not be symbolic."
[ -L "$UPSTREAM_LINK" ] || ln -s "$UPSTREAM_CONF" "$UPSTREAM_LINK"

# --- 3. Demo secrets come from the Vault, never from this host -----------------------------------
# Generating them here made the host the source of truth: the values existed nowhere else, could not
# be rotated without wiping the environment, and no other project could reuse the pattern. Missing or
# malformed values fail the run rather than falling back to a local `openssl rand`, because a silent
# fallback would recreate exactly the situation this replaces.
for vault_secret in DB_PASSWORD JWT_KEY ENCRYPTION_KEY ENCRYPTION_SALT; do
  eval "vault_value=\${$vault_secret:-}"
  [ -n "$vault_value" ] \
    || fail "$vault_secret is not provided by the demo Vault; refusing to deploy the demo."
  case "$vault_value" in
    *[!0-9a-fA-F]*) fail "$vault_secret must be a hexadecimal string." ;;
  esac
done
[ "${#ENCRYPTION_KEY}" -ge 32 ] || fail "ENCRYPTION_KEY is too short."
[ "${#ENCRYPTION_SALT}" -ge 16 ] || fail "ENCRYPTION_SALT is too short."
# The administrator password of a public demo is advertised on purpose, so it is library data
# (PUBLIC_ADMIN_PASSWORD), not a secret: a vault value would be masked out of the very smoke step that
# has to use it. Still only a shape check here, never a literal.
case "$PUBLIC_ADMIN_PASSWORD" in *[!A-Za-z0-9._-]*) fail "PUBLIC_ADMIN_PASSWORD holds characters the env file cannot carry." ;; esac

# The persisted database is encrypted with the values it was created under, so a rotated Vault makes
# it unreadable. The demo is disposable and reseeded on every bootstrap, so it is recreated rather
# than left in a state nothing can open.
if [ -f "$SECRETS_FILE" ]; then
  for vault_secret in DB_PASSWORD ENCRYPTION_KEY ENCRYPTION_SALT; do
    eval "vault_value=\${$vault_secret}"
    persisted_value="$(sed -n "s/^${vault_secret}=//p" "$SECRETS_FILE" | tail -n 1)"
    if [ "$vault_value" != "$persisted_value" ]; then
      echo ">>> $vault_secret differs from the persisted demo secret; the existing demo data cannot be read."
      rm -f "$SECRETS_FILE" "$ENV_FILE"
      break
    fi
  done
fi

if [ ! -f "$SECRETS_FILE" ]; then
  # Secrets gone while the volumes remain means the state directory was wiped under a live database.
  # The regenerated DB_PASSWORD would not match the existing PostgreSQL volume and the regenerated
  # ENCRYPTION_KEY could not read the existing rows, so the two can never be reconciled.
  #
  # Production fails closed here because its data is irreplaceable. The demo is the opposite: public,
  # disposable and re-seeded on every bootstrap, so recreating it beats leaving the environment
  # permanently unbootstrappable. The reset is bounded to the five volumes of this Compose project and
  # to containers carrying that exact prefix; the identity guards above have already refused any
  # project or path containing a production token.
  ORPHANED_VOLUMES=""
  for vol in db-data git-repos dp-keys artifacts packages; do
    if docker volume inspect "$COMPOSE_PROJECT-$vol" >/dev/null 2>&1; then
      ORPHANED_VOLUMES="$ORPHANED_VOLUMES $COMPOSE_PROJECT-$vol"
    fi
  done
  if [ -n "$ORPHANED_VOLUMES" ]; then
    echo ">>> Demo secrets are missing while persistent volumes remain; resetting the disposable demo."
    echo ">>> Volumes to recreate:$ORPHANED_VOLUMES"
    for container in $(docker ps -a --format '{{.Names}}' | grep -E "^$COMPOSE_PROJECT-" || true); do
      case "$container" in
        "$COMPOSE_PROJECT"-*) docker rm -f "$container" >/dev/null 2>&1 || true ;;
        *) fail "Refusing to remove a container outside the demo project: $container" ;;
      esac
    done
    # shellcheck disable=SC2086
    docker volume rm $ORPHANED_VOLUMES >/dev/null || fail "Could not remove the orphaned demo volumes."
    rm -f "$STATE_DIR/live-color" "$STATE_DIR/source-commit" "$STATE_DIR/nightly-metrics"
    rm -rf "$STATE_DIR/deployment-transaction"
    echo ">>> Demo reset complete; a fresh seeded environment will be created."
  fi
  echo ">>> Materialising the demo secrets from the Vault at $SECRETS_FILE"
  OLD_UMASK="$(umask)"
  umask 177
  SECRETS_TMP="$SECRETS_FILE.first-deploy.$$"
  cat > "$SECRETS_TMP" <<SECRETS_EOF
DB_PASSWORD=$DB_PASSWORD
JWT_KEY=$JWT_KEY
ADMIN_PASSWORD=$PUBLIC_ADMIN_PASSWORD
ENCRYPTION_KEY=$ENCRYPTION_KEY
ENCRYPTION_SALT=$ENCRYPTION_SALT
SECRETS_EOF
  chmod 600 "$SECRETS_TMP"
  mv "$SECRETS_TMP" "$SECRETS_FILE"
  umask "$OLD_UMASK"
fi

if [ ! -f "$ENV_FILE" ]; then
  echo ">>> First demo deploy: generating persistent environment at $ENV_FILE"
  BOOT_DB_PASSWORD="$(sed -n 's/^DB_PASSWORD=//p' "$SECRETS_FILE" | tail -n 1)"
  BOOT_JWT_KEY="$(sed -n 's/^JWT_KEY=//p' "$SECRETS_FILE" | tail -n 1)"
  BOOT_ADMIN_PASSWORD="$(sed -n 's/^ADMIN_PASSWORD=//p' "$SECRETS_FILE" | tail -n 1)"
  BOOT_ENCRYPTION_KEY="$(sed -n 's/^ENCRYPTION_KEY=//p' "$SECRETS_FILE" | tail -n 1)"
  BOOT_ENCRYPTION_SALT="$(sed -n 's/^ENCRYPTION_SALT=//p' "$SECRETS_FILE" | tail -n 1)"
  OLD_UMASK="$(umask)"
  umask 177
  ENV_TMP="$ENV_FILE.first-deploy.$$"
  cat > "$ENV_TMP" <<ENV_EOF
APPNAME=$PROJECT_SLUG
ENV=demo
DB_NAME=$(printf '%s' "$PROJECT_SLUG" | tr '-' '_')_demo
DB_USER=$(printf '%s' "$PROJECT_SLUG" | tr '-' '_')
DB_PASSWORD=$BOOT_DB_PASSWORD
JWT_KEY=$BOOT_JWT_KEY
ADMIN_USER=admin
ADMIN_PASSWORD=$BOOT_ADMIN_PASSWORD
ENCRYPTION_KEY=$BOOT_ENCRYPTION_KEY
ENCRYPTION_SALT=$BOOT_ENCRYPTION_SALT
DEPLOYMENT_BOOTSTRAP_IDENTITY_KEY=$(openssl rand -hex 32)
API_BASE_URL=https://$API_HOST
FRONT_URL=https://$APP_HOST
FRONT_URL_ACCEPT=https://$APP_HOST
FRONT_URL_PROD=https://$APP_HOST
AETHEUS_PUBLIC_DEMO=true
SEED_DEMO=true
ENV_EOF
  chmod 600 "$ENV_TMP"
  mv "$ENV_TMP" "$ENV_FILE"
  umask "$OLD_UMASK"
fi

[ ! -L "$ENV_FILE" ] && [ ! -L "$SECRETS_FILE" ] || fail "Demo secret files must not be symbolic."

# Back-fill, not regeneration. remote-bluegreen.compose.yml interpolates
# DEPLOYMENT_BOOTSTRAP_IDENTITY_KEY with no default, and only the production path ever wrote it, so on
# the demo host it resolved to an empty string: the deployed backend could not re-derive the throwaway
# smoke account and answered the blocking probe with 401 on a deployment that was otherwise healthy.
# That is what failed nightly run 1204. Appending a key that never existed is safe in a way rotating
# one is not - it decrypts nothing, so no stored row and no live session depends on its old value.
if ! grep -q "^DEPLOYMENT_BOOTSTRAP_IDENTITY_KEY=.\+" "$ENV_FILE"; then
  echo ">>> Back-filling DEPLOYMENT_BOOTSTRAP_IDENTITY_KEY in $ENV_FILE"
  OLD_UMASK="$(umask)"
  umask 177
  ENV_BACKFILL_TMP="$ENV_FILE.backfill.$$"
  { cat "$ENV_FILE"; echo "DEPLOYMENT_BOOTSTRAP_IDENTITY_KEY=$(openssl rand -hex 32)"; } > "$ENV_BACKFILL_TMP"
  chmod 600 "$ENV_BACKFILL_TMP"
  mv "$ENV_BACKFILL_TMP" "$ENV_FILE"
  umask "$OLD_UMASK"
fi

# The demo moved from one same-origin host to APP_HOST and API_HOST (PLAN-003 2.1). Its environment
# file was written when it had one name, so the URLs follow the library here; nothing else is touched.
sh "$WORKSPACE/deploy/scripts/reconcile-env-urls.sh" "$ENV_FILE" \
  "API_BASE_URL=https://$API_HOST" "FRONT_URL=https://$APP_HOST" \
  "FRONT_URL_ACCEPT=https://$APP_HOST" "FRONT_URL_PROD=https://$APP_HOST"

env_value() {
  sed -n "s/^$1=//p" "$ENV_FILE" | tail -n 1
}

DEMO_DB_USER="$(printf '%s' "$PROJECT_SLUG" | tr '-' '_')"
[ "$(env_value APPNAME)" = "$PROJECT_SLUG" ] || fail "Demo APPNAME is invalid."
[ "$(env_value ENV)" = demo ] || fail "Demo ENV is invalid."
[ "$(env_value DB_NAME)" = "${DEMO_DB_USER}_demo" ] || fail "Demo database name is invalid."
[ "$(env_value DB_USER)" = "$DEMO_DB_USER" ] || fail "Demo database user is invalid."
[ "$(env_value API_BASE_URL)" = "https://$API_HOST" ] || fail "Demo API URL is invalid."
[ "$(env_value FRONT_URL)" = "https://$APP_HOST" ] || fail "Demo front URL is invalid."
[ "$(env_value AETHEUS_PUBLIC_DEMO)" = true ] || fail "Public demo mode is not enabled."
[ "$(env_value SEED_DEMO)" = true ] || fail "Demo seed is not enabled."
[ "$(sed -n 's/^ADMIN_PASSWORD=//p' "$SECRETS_FILE" | tail -n 1)" = "$PUBLIC_ADMIN_PASSWORD" ] \
  || fail "The intentionally public demo credential differs from PUBLIC_ADMIN_PASSWORD."
for secret_name in DB_PASSWORD JWT_KEY ENCRYPTION_KEY ENCRYPTION_SALT; do
  grep -Eq "^${secret_name}=[0-9a-f]+$" "$SECRETS_FILE" || fail "Demo secret $secret_name is missing or invalid."
  [ "$(env_value "$secret_name")" = "$(sed -n "s/^${secret_name}=//p" "$SECRETS_FILE" | tail -n 1)" ] \
    || fail "Demo environment and persistent secret $secret_name differ."
done
[ "$(env_value ADMIN_PASSWORD)" = "$PUBLIC_ADMIN_PASSWORD" ] || fail "Demo environment uses an unexpected admin credential."
# Checked here so a missing key fails this step with a name, instead of surfacing three stages later
# as an unexplained 401 from the blocking smoke.
grep -Eq "^DEPLOYMENT_BOOTSTRAP_IDENTITY_KEY=[0-9a-f]{64}$" "$ENV_FILE" \
  || fail "DEPLOYMENT_BOOTSTRAP_IDENTITY_KEY is missing or invalid in $ENV_FILE."
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

AETHEUS_BACK_IMAGE="$BACK_IMAGE_REPO:$SOURCE_SHA"
AETHEUS_FRONT_IMAGE="$FRONT_IMAGE_REPO:$SOURCE_SHA"
# Named after the environment, not the product: the demo's copy is removed after each run
# (nightly-demo-evidence.sh) and must never be the one production's smoke is using.
AETHEUS_BROWSER_SMOKE_IMAGE="$COMPOSE_PROJECT-browser-smoke:$SOURCE_SHA"

gzip -dc "$ARTIFACT_DIR/aetheus-back.tar.gz" | docker load
gzip -dc "$ARTIFACT_DIR/aetheus-front.tar.gz" | docker load
gzip -dc "$ARTIFACT_DIR/aetheus-browser-smoke.tar.gz" | docker import \
  --change 'ENV DOTNET_ROOT=/usr/lib/dotnet-10' \
  --change 'ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright' \
  --change 'WORKDIR /src' \
  --change 'USER pwuser' \
  --change "LABEL org.opencontainers.image.revision=$SOURCE_SHA" \
  - "$AETHEUS_BROWSER_SMOKE_IMAGE" >/dev/null

for image in "$AETHEUS_BACK_IMAGE" "$AETHEUS_FRONT_IMAGE" "$AETHEUS_BROWSER_SMOKE_IMAGE"; do
  [ "$(docker image inspect --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' "$image")" = "$SOURCE_SHA" ] \
    || fail "Image revision is invalid: $image"
done

# --- 5. Run variables the Compose files and the smoke step interpolate ---------------------------
# These are published, not exported: the typed steps that follow run as separate tasks in separate
# processes, and a `compose_env:` entry the run does not define is refused before a task is created.
# The step declares them in `outputs:`, which is what lets the run's variables name them (D-02).
#
# Every value below is public by construction. A `setvariable` directive is echoed into the run log,
# so a run-scoped bootstrap password could not travel this way without being published with it. The
# demo authenticates its smoke with the credential it already advertises instead: the environment is
# public, disposable and reseeded, which is precisely why that credential is not a secret.
echo "##aetheus[setvariable name=AETHEUS_BACK_IMAGE]$AETHEUS_BACK_IMAGE"
echo "##aetheus[setvariable name=AETHEUS_FRONT_IMAGE]$AETHEUS_FRONT_IMAGE"
echo "##aetheus[setvariable name=SOURCE_COMMIT]$SOURCE_SHA"
echo "##aetheus[setvariable name=AETHEUS_BROWSER_SMOKE_IMAGE]$AETHEUS_BROWSER_SMOKE_IMAGE"
echo "##aetheus[setvariable name=AETHEUS_SMOKE_ADMIN_USER]$DEMO_ADMIN_USER"
echo "##aetheus[setvariable name=AETHEUS_SMOKE_ADMIN_PASSWORD]$PUBLIC_ADMIN_PASSWORD"
echo "Demo host prepared for $SOURCE_SHA; the cutover steps own the environment from here."
