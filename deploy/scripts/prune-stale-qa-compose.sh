#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
set -eu

CURRENT_RUN_ID="${1:-${BUILD_BUILDID:-}}"
case "$CURRENT_RUN_ID" in
  ''|*[!0-9]*) echo "Current QA run id is missing or invalid." >&2; exit 1 ;;
esac

PROJECTS="$({
  docker ps -a --filter 'label=com.docker.compose.project' \
    --format '{{.Label "com.docker.compose.project"}}'
  docker network ls --filter 'label=com.docker.compose.project' \
    --format '{{.Label "com.docker.compose.project"}}'
  docker volume ls --filter 'label=com.docker.compose.project' \
    --format '{{.Label "com.docker.compose.project"}}'
} | awk '/^aetheus-qa-(rollback-)?[0-9]+$/ { print }' | sort -u)"

for PROJECT in $PROJECTS; do
  case "$PROJECT" in
    aetheus-qa-rollback-*) PROJECT_RUN_ID="${PROJECT#aetheus-qa-rollback-}" ;;
    aetheus-qa-*) PROJECT_RUN_ID="${PROJECT#aetheus-qa-}" ;;
    *) continue ;;
  esac
  case "$PROJECT_RUN_ID" in
    ''|*[!0-9]*) echo "Refusing malformed QA Compose project '$PROJECT'." >&2; exit 1 ;;
  esac
  if [ "$PROJECT_RUN_ID" -ge "$CURRENT_RUN_ID" ]; then
    echo "Protected current or newer QA Compose project '$PROJECT'."
    continue
  fi

  echo "Removing stale QA Compose project '$PROJECT' before storage measurement."
  docker ps -aq --filter "label=com.docker.compose.project=$PROJECT" \
    | while IFS= read -r ID; do [ -z "$ID" ] || docker rm -f "$ID"; done
  docker network ls -q --filter "label=com.docker.compose.project=$PROJECT" \
    | while IFS= read -r ID; do [ -z "$ID" ] || docker network rm "$ID"; done
  docker volume ls -q --filter "label=com.docker.compose.project=$PROJECT" \
    | while IFS= read -r ID; do [ -z "$ID" ] || docker volume rm "$ID"; done

  test -z "$(docker ps -aq --filter "label=com.docker.compose.project=$PROJECT")"
  test -z "$(docker network ls -q --filter "label=com.docker.compose.project=$PROJECT")"
  test -z "$(docker volume ls -q --filter "label=com.docker.compose.project=$PROJECT")"
done

echo "Stale QA Compose resource maintenance completed."
