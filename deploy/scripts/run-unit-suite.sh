#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
# Runs one unit-test project, collects its coverage, and publishes a classified gate status.
#
# The three product suites ran through forty identical lines each, copied three times in
# aetheus-ci.yaml, including the two comments explaining why `node` has to be resolved first. A
# fourth suite meant a fourth copy, and a fix to one of them meant remembering the other two.
#
#   $1  test project path, e.g. tests/Aetheus.Back.Tests
#   $2  coverage sub-directory under coverage/, e.g. backend
#   $3  name of the run variable the classified status is published under
# Env: WORKSPACE (required), TEST_TIMEOUT_LABEL (optional, defaults to the project path)
set -eu

PROJECT="${1:?test project path is required}"
COVERAGE_NAME="${2:?coverage directory name is required}"
STATUS_VARIABLE="${3:?status variable name is required}"
WORKSPACE="${WORKSPACE:?WORKSPACE is required}"

case "$COVERAGE_NAME" in *[!a-zA-Z0-9_-]*|'') echo "Invalid coverage directory name." >&2; exit 2 ;; esac
case "$STATUS_VARIABLE" in *[!A-Z0-9_]*|'') echo "Invalid status variable name." >&2; exit 2 ;; esac
[ -d "$WORKSPACE/$PROJECT" ] || { echo "Test project not found: $PROJECT" >&2; exit 2; }

DOTNET="$(sh "$WORKSPACE/deploy/scripts/ensure-dotnet-sdk.sh")"
DOTNET_ROOT="$(dirname "$DOTNET")"
export DOTNET_ROOT
export PATH="$DOTNET_ROOT:$PATH"

# Several suites resolve `node` as a hard prerequisite and throw rather than skip, because it is what
# the delivery pipelines run to seal their own evidence. The classification below already uses the
# pinned, checksummed runtime; resolving it first and putting it on PATH makes the tests see that
# same runtime instead of depending on whatever the host happens to have installed - a simulator
# rebuild removed a hand-installed /usr/local/bin/node and took the whole backend suite down with it
# (mirror run 1219).
NODE="$(sh "$WORKSPACE/deploy/scripts/ensure-node-runtime.sh")"
export PATH="$(dirname "$NODE"):$PATH"

RESULTS="$WORKSPACE/coverage/$COVERAGE_NAME"
mkdir -p "$RESULTS"

# The suite's own exit code is captured rather than allowed to end the step: a test that ran and
# failed is graded into the assurance contract, and only an unusable RESULT (no TRX, no coverage) is
# a technical failure. classify-dotnet-test-result.mjs is what draws that line.
#
# The suites run on Microsoft.Testing.Platform (global.json "test.runner"): TRX comes from the TrxReport
# extension and coverage from coverlet.MTP, whose exclusions live in tests/testconfig.json. A failed
# test exits 2 there, not 1; the classifier reads the TRX, not the code's value.
RAW_TEST_EXIT=0
(
  set -eu
  cd "$WORKSPACE"
  sh "$WORKSPACE/deploy/scripts/run-with-progress.sh" "$PROJECT" \
    "$DOTNET" test --project "$WORKSPACE/$PROJECT" --configuration Release --no-build --no-progress \
    --results-directory "$RESULTS" \
    --report-trx --report-trx-filename "$COVERAGE_NAME.trx" \
    --coverlet
) || RAW_TEST_EXIT=$?

# coverlet.MTP stamps its report (coverage.cobertura.<time>.xml); every reader of this evidence (the
# classifier, verify-test-evidence.sh, merge-quality-coverage.sh) looks for coverage.cobertura.xml.
sh "$WORKSPACE/deploy/scripts/normalize-coverage-report.sh" "$RESULTS"

STATUS="$("$NODE" "$WORKSPACE/deploy/scripts/classify-dotnet-test-result.mjs" \
  "$RAW_TEST_EXIT" "$RESULTS/$COVERAGE_NAME.trx" "$RESULTS")"
echo "##aetheus[setvariable name=$STATUS_VARIABLE]$STATUS"
echo "$PROJECT findings status: $STATUS. Valid TRX and coverage evidence recorded."
