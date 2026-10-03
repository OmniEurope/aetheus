#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
# Empties a disposable blue-green environment on this host: its containers, its volumes (the
# database with them) and the deployment state, so the next deployment starts from nothing
# (recette R-523).
#
#   aetheus-nightly calls it last, whatever happened before (`condition: always()`): its QA is
#   created and destroyed by every run.
#   aetheus-demo calls it first: the demo is reset once a day, then redeployed and reseeded.
#
# What stays: the Apache upstream file and the vhosts (the hosts keep answering, 503 while nothing
# runs) and the certificates. The secrets file goes with the database it opened; the Vault
# materialises it again on the next preparation.
#
# Inputs, read from the environment (the environment's library):
#   WORKSPACE        the run's workspace
#   STATE_DIR        the environment's state root, a direct child of WORKSPACE
#   ENV_FILE         its Compose environment file, inside STATE_DIR
#   SECRETS_FILE     its secrets file, inside STATE_DIR
#   COMPOSE_PROJECT  the Compose project; its containers and volumes carry this name. Only a
#                    project named *-demo or *-nightly is reset.
#   REMOVE_BROWSER_SMOKE_IMAGES  true to also remove the project's browser smoke images. The nightly
#                    sets it at its end; the demo does not, because it resets after building the image
#                    it is about to deploy and removes it itself once used (Retain images).
set -eu

fail() {
  echo "FATAL: $*" >&2
  exit 1
}

STATE_DIR="${STATE_DIR:?STATE_DIR is required}"
ENV_FILE="${ENV_FILE:?ENV_FILE is required}"
SECRETS_FILE="${SECRETS_FILE:?SECRETS_FILE is required}"
COMPOSE_PROJECT="${COMPOSE_PROJECT:?COMPOSE_PROJECT is required}"

# The same identity guards as the preparation: this script deletes data, so a library that names
# production, or a state root outside the run workspace, ends the run before anything is touched.
# State lives in the run's workspace (user decision, 2026-10-02: only production Aetheus has a
# directory on the host), as nightly-demo-prepare.sh requires.
WORKSPACE="${WORKSPACE:?WORKSPACE is required}"
case "$WORKSPACE" in /*) ;; *) fail "WORKSPACE must be an absolute path: $WORKSPACE" ;; esac
case "$STATE_DIR" in
  *..*) fail "The state directory must not contain a traversal: $STATE_DIR" ;;
  "$WORKSPACE"/*/*) fail "The state directory must be a direct child of the run workspace: $STATE_DIR" ;;
  "$WORKSPACE"/?*) ;;
  *) fail "The state directory must live in the run workspace ($WORKSPACE): $STATE_DIR" ;;
esac
case "$ENV_FILE" in "$STATE_DIR"/*) ;; *) fail "The environment file must live in the state directory." ;; esac
case "$SECRETS_FILE" in "$STATE_DIR"/*) ;; *) fail "The secrets file must live in the state directory." ;; esac
case "$COMPOSE_PROJECT" in
  ''|*[!a-z0-9-]*|-*) fail "Not a Compose project name: '$COMPOSE_PROJECT'" ;;
esac
case "${STATE_DIR}|${ENV_FILE}|${SECRETS_FILE}|${COMPOSE_PROJECT}" in
  *prod*) fail "A reset identity contains a production token; refusing to delete anything." ;;
esac
# The deletions below match by name prefix, so the project name itself is bounded: a library naming
# "aetheus" would otherwise reach aetheus-prod-* through the prefix.
case "$COMPOSE_PROJECT" in
  *-demo|*-nightly) ;;
  *) fail "Only a disposable project (*-demo or *-nightly) is reset, not '$COMPOSE_PROJECT'." ;;
esac
command -v docker >/dev/null || fail "docker is required to reset the environment."

# Containers: by Compose label, then by the name prefix every service of the project carries
# (container_name is APPNAME-ENV-<service>, which the library keeps equal to the project name). The
# trailing hyphen keeps a project named aetheus-nightly from matching aetheus-nightly2.
CONTAINERS="$(
  {
    docker ps -aq --filter "label=com.docker.compose.project=$COMPOSE_PROJECT"
    docker ps -a --format '{{.ID}} {{.Names}}' | awk -v prefix="$COMPOSE_PROJECT-" 'index($2, prefix) == 1 { print $1 }'
  } | sort -u
)"
if [ -n "$CONTAINERS" ]; then
  # shellcheck disable=SC2086
  if docker inspect --format '{{.Name}}' $CONTAINERS | grep -qi prod; then
    fail "A container selected for removal names production; refusing to delete anything."
  fi
  echo ">>> Removing the containers of $COMPOSE_PROJECT"
  # shellcheck disable=SC2086
  docker rm -f $CONTAINERS >/dev/null
fi

VOLUMES="$(
  {
    docker volume ls -q --filter "label=com.docker.compose.project=$COMPOSE_PROJECT"
    docker volume ls -q | awk -v prefix="$COMPOSE_PROJECT-" 'index($1, prefix) == 1'
  } | sort -u
)"
if [ -n "$VOLUMES" ]; then
  # shellcheck disable=SC2086
  if printf '%s\n' $VOLUMES | grep -qi prod; then
    fail "A volume selected for removal names production; refusing to delete it."
  fi
  echo ">>> Removing the volumes of $COMPOSE_PROJECT: $(printf "%s " $VOLUMES)"
  # shellcheck disable=SC2086
  docker volume rm $VOLUMES >/dev/null
fi

NETWORKS="$(docker network ls -q --filter "label=com.docker.compose.project=$COMPOSE_PROJECT")"
if [ -n "$NETWORKS" ]; then
  # shellcheck disable=SC2086
  docker network rm $NETWORKS >/dev/null
fi

# The browser smoke images the preparation loaded under the project's name, on request only (see
# REMOVE_BROWSER_SMOKE_IMAGES above).
if [ "${REMOVE_BROWSER_SMOKE_IMAGES:-false}" = true ]; then
  SMOKE_IMAGES="$(docker image ls -q "$COMPOSE_PROJECT-browser-smoke" | sort -u)"
  if [ -n "$SMOKE_IMAGES" ]; then
    # shellcheck disable=SC2086
    docker image rm -f $SMOKE_IMAGES >/dev/null 2>&1 || true
  fi
fi

rm -f "$ENV_FILE" "$SECRETS_FILE" "$STATE_DIR/live-color" "$STATE_DIR/source-commit"
rm -rf "$STATE_DIR/deployment-transaction"

# Proof, not hope: nothing of the project may remain.
[ -z "$(docker ps -aq --filter "label=com.docker.compose.project=$COMPOSE_PROJECT")" ] \
  || fail "Containers of $COMPOSE_PROJECT survived the reset."
[ -z "$(docker volume ls -q --filter "label=com.docker.compose.project=$COMPOSE_PROJECT")" ] \
  || fail "Volumes of $COMPOSE_PROJECT survived the reset."
[ ! -e "$ENV_FILE" ] && [ ! -e "$STATE_DIR/live-color" ] || fail "The deployment state survived the reset."
echo "Environment $COMPOSE_PROJECT reset: no container, no volume, no deployment state."
