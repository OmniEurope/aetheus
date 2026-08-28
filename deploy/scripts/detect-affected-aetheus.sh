#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
set -eu

ROOT="${1:-.}"
BASELINE_FILE="${2:-$ROOT/.production-baseline/.pipeline-artifacts/source-commit}"
cd "$ROOT"
CURRENT="$(git rev-parse HEAD)"
BASELINE="$(tr -d '\r\n' < "$BASELINE_FILE" 2>/dev/null || true)"

FULL=false
if ! printf '%s' "$BASELINE" | grep -Eq '^[0-9a-fA-F]{40}([0-9a-fA-F]{24})?$'; then
  FULL=true
elif ! git merge-base --is-ancestor "$BASELINE" "$CURRENT" 2>/dev/null; then
  FULL=true
fi

CHANGED="$(mktemp)"
trap 'rm -f "$CHANGED"' EXIT HUP INT TERM
if [ "$FULL" = true ]; then
  git ls-files > "$CHANGED"
else
  git diff --name-only "$BASELINE...$CURRENT" > "$CHANGED"
fi

matches()
{
  grep -Eq "$1" "$CHANGED"
}

BACK=false
FRONT=false
AGENT=false
QUALITY=false
SECURITY=false
TRANSVERSE=false

matches '^(Aetheus\.slnx|Directory\.|global\.json|NuGet\.config|src/Aetheus\.Shared/|src/Aetheus\.Analyzers/|deploy/docker/)' \
  && TRANSVERSE=true
matches '^(src/Aetheus\.Back/|tests/Aetheus\.Back)' && BACK=true
matches '^(src/Aetheus\.Front/|tests/Aetheus\.Front)' && FRONT=true
matches '^(src/Aetheus\.Agent|tests/Aetheus\.Agent)' && AGENT=true
matches '\.(cs|csproj|props|targets|js|mjs|json|ya?ml)$' && QUALITY=true
matches '(^\.pipeline/|^deploy/|(^|/)(packages\.lock\.json|package-lock\.json|Dockerfile[^/]*|NuGet\.config)$)' \
  && SECURITY=true

if [ "$FULL" = true ] || [ "$TRANSVERSE" = true ]; then
  BACK=true
  FRONT=true
  AGENT=true
  QUALITY=true
  SECURITY=true
fi

emit()
{
  NAME="$1"
  VALUE="$2"
  echo "##aetheus[setvariable name=$NAME]$VALUE"
}

emit AETHEUS_BASELINE_COMMIT "${BASELINE:-none}"
emit AETHEUS_IMPACT_FULL "$FULL"
emit AETHEUS_RUN_BACK "$BACK"
emit AETHEUS_RUN_FRONT "$FRONT"
emit AETHEUS_RUN_AGENT "$AGENT"
emit AETHEUS_RUN_QUALITY "$QUALITY"
emit AETHEUS_RUN_SECURITY "$SECURITY"
echo "Impact baseline=${BASELINE:-none} current=$CURRENT full=$FULL"
sed 's/^/  - /' "$CHANGED"
