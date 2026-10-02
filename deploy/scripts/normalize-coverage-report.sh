#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
# Gives each coverlet.MTP Cobertura report the layout coverlet.collector used to write.
#
# coverlet.collector (VSTest) wrote <results>/<guid>/coverage.cobertura.xml. coverlet.MTP writes
# <results>/coverage.cobertura.<timestamp>.xml instead, and every reader of the evidence looks for the
# exact name: classify-dotnet-test-result.mjs, ci/verify-test-evidence.sh, merge-quality-coverage.sh,
# and the agent's default coverage glob. Each stamped report moves to <results>/<timestamp>/ under the
# old name, so several reports in one directory never overwrite each other.
#
# A directory without a stamped report is left as it is: whether coverage is missing is the
# classifier's verdict to give, not this script's.
#
#   $1  results directory of one test run
set -eu

RESULTS="${1:?results directory is required}"
[ -d "$RESULTS" ] || { echo "Results directory not found: $RESULTS" >&2; exit 2; }

for REPORT in "$RESULTS"/coverage.cobertura.*.xml; do
  [ -f "$REPORT" ] || continue
  STAMP="${REPORT##*/coverage.cobertura.}"
  STAMP="${STAMP%.xml}"
  case "$STAMP" in *[!0-9]*|'') echo "Unexpected coverage report name: $REPORT" >&2; exit 2 ;; esac
  mkdir -p "$RESULTS/$STAMP"
  mv "$REPORT" "$RESULTS/$STAMP/coverage.cobertura.xml"
  echo "Coverage report: $RESULTS/$STAMP/coverage.cobertura.xml"
done
