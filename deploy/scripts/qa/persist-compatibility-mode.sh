# SPDX-License-Identifier: EUPL-1.2
#
# Turns the QA gate statuses this run published into the compatibility verdict the assurance seal
# reads. A test that ran and failed is graded here rather than failing the pipeline, so the run stays
# green while the candidate becomes undeployable; that only works if this verdict is honest, which is
# what "honest compatibility mode" in the step name means.
#
# This was 82 lines inlined in .pipeline/aetheus-qa.yaml. PLAN-007 lot 2 removed the profile, the
# performance status and the retained-agent verdict: those gates run in aetheus-qa-extended, whose
# persist-extended-gates.sh publishes them. Two pipelines publishing the same name to one parent would
# leave the value to whichever finished last, so each name now has exactly one publisher.
#
# Inputs, all read from the environment (none positional):
#   QA_*_GATE_STATUS                       the per-suite statuses the QA stages published
#     (QA_CURRENT_INTEGRATION_GATE_STATUS, QA_CURRENT_E2E_GATE_STATUS,
#      QA_PREVIOUS_INTEGRATION_GATE_STATUS, QA_PREVIOUS_E2E_GATE_STATUS,
#      QA_PREVIOUS_SMOKE_GATE_STATUS)
#
# Publishes the compatibility verdict as run output variables.
set -eu
normalize_status() {
  case "$1" in 0) printf '%s' 0 ;; *) printf '%s' 1 ;; esac
}
read_status() {
  FILE="$1"
  FALLBACK="$2"
  if [ -s "$FILE" ]; then cat "$FILE"; else printf '%s' "$FALLBACK"; fi
}
CURRENT_INTEGRATION="$(normalize_status "$(read_status "$WORKSPACE/.qa-current-integration-gate-status" "${QA_CURRENT_INTEGRATION_GATE_STATUS:-1}")")"
CURRENT_E2E="$(normalize_status "$(read_status "$WORKSPACE/.qa-current-e2e-gate-status" "${QA_CURRENT_E2E_GATE_STATUS:-1}")")"
PREVIOUS_SMOKE="$(normalize_status "$(read_status "$WORKSPACE/.qa-previous-smoke-gate-status" "${QA_PREVIOUS_SMOKE_GATE_STATUS:-1}")")"
echo "##aetheus[setvariable name=QA_CURRENT_INTEGRATION_GATE_STATUS]$CURRENT_INTEGRATION"
echo "##aetheus[setvariable name=QA_CURRENT_E2E_GATE_STATUS]$CURRENT_E2E"
echo "##aetheus[setvariable name=QA_PREVIOUS_SMOKE_GATE_STATUS]$PREVIOUS_SMOKE"
# The V-1 suites are the rollback proof, and it belongs to the commit being packaged.
PREVIOUS_INTEGRATION="$(normalize_status "$(read_status "$WORKSPACE/.qa-previous-integration-gate-status" "${QA_PREVIOUS_INTEGRATION_GATE_STATUS:-1}")")"
PREVIOUS_E2E="$(normalize_status "$(read_status "$WORKSPACE/.qa-previous-e2e-gate-status" "${QA_PREVIOUS_E2E_GATE_STATUS:-1}")")"
echo "##aetheus[setvariable name=QA_PREVIOUS_INTEGRATION_GATE_STATUS]$PREVIOUS_INTEGRATION"
echo "##aetheus[setvariable name=QA_PREVIOUS_E2E_GATE_STATUS]$PREVIOUS_E2E"
if [ "$CURRENT_INTEGRATION" = 0 ] && [ "$CURRENT_E2E" = 0 ]; then
  CURRENT_VERDICT=CurrentVersionVerified
else
  CURRENT_VERDICT=CurrentVersionDegraded
fi
if [ "$(cat "$WORKSPACE/.qa-bootstrap-mode")" = 1 ]; then
  echo "##aetheus[setvariable name=CompatibilityMode]Bootstrap"
  echo "##aetheus[setvariable name=PreviousVersionTested]false"
  echo "##aetheus[setvariable name=CompatibilityVerdict]$CURRENT_VERDICT"
else
  PREVIOUS="$(cat "$WORKSPACE/.qa-previous-commit")"
  test -n "$PREVIOUS"
  echo "##aetheus[setvariable name=CompatibilityMode]NMinusOne"
  echo "##aetheus[setvariable name=PreviousVersionTested]$PREVIOUS"
  # One verdict, one meaning: the V-1 suites and the smoke all ran.
  HISTORICAL_OK=0
  if [ "$PREVIOUS_SMOKE" != 0 ] || [ "$PREVIOUS_INTEGRATION" != 0 ] || [ "$PREVIOUS_E2E" != 0 ]; then
    HISTORICAL_OK=1
  fi
  if [ "$CURRENT_VERDICT" = CurrentVersionVerified ] && [ "$HISTORICAL_OK" = 0 ]; then
    echo "##aetheus[setvariable name=CompatibilityVerdict]NMinusOneVerified"
  else
    echo "##aetheus[setvariable name=CompatibilityVerdict]NMinusOneDegraded"
  fi
fi
