#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
set -eu

DOTNET_BIN="${DOTNET:-dotnet}"
GIT_BIN="${GIT:-git}"
MODE="${AETHEUS_FORMAT_MODE:-affected}"
BASELINE="${DELIVERY_BASELINE_SOURCE_SHA:-}"
HEAD="${BUILD_SOURCEVERSION:-}"
case "$MODE" in affected|full) ;; *) echo "AETHEUS_FORMAT_MODE must be affected or full." >&2; exit 1 ;; esac
SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
WORKSPACE_ROOT="${WORKSPACE:-$(pwd)}"
REPORT_DIR="$WORKSPACE_ROOT/.analysis-format"
REPORT_JSON="$REPORT_DIR/format-report.json"
REPORT_SARIF="$REPORT_DIR/dotnet-format.sarif"
NODE_BIN="${NODE:-$(sh "$SCRIPT_DIR/ensure-node-runtime.sh")}"

rm -rf "$REPORT_DIR"
mkdir -p "$REPORT_DIR"

run_and_publish_format() {
  RAW_EXIT=0
  "$@" --report "$REPORT_DIR" || RAW_EXIT=$?
  test -s "$REPORT_JSON" || {
    echo "dotnet format did not produce its required JSON evidence." >&2
    exit 1
  }
  FINDING_COUNT="$("$NODE_BIN" "$SCRIPT_DIR/convert-dotnet-format-report.mjs" "$REPORT_JSON" "$REPORT_SARIF")"
  case "$FINDING_COUNT" in ''|*[!0-9]*) echo "Invalid dotnet format finding count." >&2; exit 1 ;; esac
  if [ "$RAW_EXIT" = 0 ] && [ "$FINDING_COUNT" = 0 ]; then
    STATUS=0
  elif [ "$RAW_EXIT" != 0 ] && [ "$FINDING_COUNT" -gt 0 ]; then
    STATUS=1
  else
    echo "dotnet format exit code and evidence are inconsistent." >&2
    exit 1
  fi
  test -s "$REPORT_SARIF"
  echo "##aetheus[setvariable name=DOTNET_FORMAT_GATE_STATUS]$STATUS"
  echo "dotnet format findings status: $STATUS ($FINDING_COUNT finding(s)); SARIF evidence recorded."
}

case "$HEAD" in
  ''|*[!0-9a-fA-F]* ) MODE=full ;;
esac
[ "${#HEAD}" -eq 40 ] || MODE=full

if [ "$MODE" = affected ]; then
  case "$BASELINE" in ''|*[!0-9a-fA-F]*) MODE=full ;; esac
  [ "${#BASELINE}" -eq 40 ] || MODE=full
fi
if [ "$MODE" = affected ] && ! "$GIT_BIN" cat-file -e "${BASELINE}^{commit}" 2>/dev/null; then
  MODE=full
fi
if [ "$MODE" = affected ] && ! "$GIT_BIN" merge-base --is-ancestor "$BASELINE" "$HEAD"; then
  MODE=full
fi

FILES=""
if [ "$MODE" = affected ]; then
  CHANGES="$("$GIT_BIN" diff --name-status --find-renames "$BASELINE..$HEAD")" || MODE=full
  if [ "$MODE" = affected ] && printf '%s\n' "$CHANGES" | grep -Eq '^(D|R|C)[0-9]*[[:space:]]'; then
    MODE=full
  fi
  if [ "$MODE" = affected ] && printf '%s\n' "$CHANGES" | cut -f2- | grep -Eq '[[:space:]]'; then
    MODE=full
  fi
  if [ "$MODE" = affected ] && printf '%s\n' "$CHANGES" | cut -f2- | grep -Eq '(^|/)(\.editorconfig|\.globalconfig|Directory\.Build\.|Directory\.Packages\.props|[^/]+\.(slnx?|csproj))$'; then
    MODE=full
  fi
  if [ "$MODE" = affected ]; then
    FILES="$(printf '%s\n' "$CHANGES" | awk -F '\t' '$1 == "A" || $1 == "M" { print $2 }' | grep -E '\.(cs|razor)$' || true)"
  fi
fi

"$DOTNET_BIN" restore Aetheus.slnx --verbosity minimal
if [ "$MODE" = full ]; then
  echo "Formatting mode: full (fail-closed fallback or explicit nightly mode)."
  echo "##aetheus[setvariable name=DOTNET_FORMAT_MODE]full"
  run_and_publish_format "$DOTNET_BIN" format Aetheus.slnx --no-restore --verify-no-changes --verbosity minimal
  exit 0
fi
if [ -z "$FILES" ]; then
  echo "Formatting mode: affected; no changed C# or Razor file."
  echo "##aetheus[setvariable name=DOTNET_FORMAT_MODE]affected-empty"
  "$NODE_BIN" -e 'require("node:fs").writeFileSync(process.argv[1], "[]\n")' "$REPORT_JSON"
  FINDING_COUNT="$("$NODE_BIN" "$SCRIPT_DIR/convert-dotnet-format-report.mjs" "$REPORT_JSON" "$REPORT_SARIF")"
  test "$FINDING_COUNT" = 0 -a -s "$REPORT_SARIF"
  echo "##aetheus[setvariable name=DOTNET_FORMAT_GATE_STATUS]0"
  exit 0
fi

echo "Formatting mode: affected ($(printf '%s\n' "$FILES" | wc -l | tr -d ' ') files)."
echo "##aetheus[setvariable name=DOTNET_FORMAT_MODE]affected"
# Intentional word splitting: Git paths containing whitespace force the full-mode safeguards above
# in project policy; repository source paths are validated not to contain whitespace.
# shellcheck disable=SC2086
run_and_publish_format "$DOTNET_BIN" format Aetheus.slnx --no-restore --verify-no-changes --verbosity minimal --include $FILES
