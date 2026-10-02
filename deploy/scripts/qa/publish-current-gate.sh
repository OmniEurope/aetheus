# SPDX-License-Identifier: EUPL-1.2
#
# Publishes the advisory gate over the current version's suites: the V integration and E2E runs.
# Advisory means a failure is graded into the assurance seal rather than failing this pipeline, so a
# candidate becomes undeployable while the run stays green.
#
# This was 35 lines inlined in .pipeline/aetheus-qa.yaml. PLAN-007 lot 2 removed the agent-contract
# and performance branches, which moved to aetheus-qa-extended with their stages.
#
# Inputs, all read from the environment (none positional):
#   WORKSPACE                    the run's checkout, holding the per-suite status files this reads
set -eu
STATUS=0
read_status() {
  FILE="$1"
  FALLBACK="$2"
  if [ -s "$FILE" ]; then cat "$FILE"; else printf '%s' "$FALLBACK"; fi
}
check_status() {
  NAME="$1"
  VALUE="$2"
  case "$VALUE" in 0|1) ;; *) echo "$NAME evidence status is unavailable or invalid ($VALUE)." >&2; exit 1 ;; esac
  if [ "$VALUE" = 0 ]; then
    echo "$NAME passed."
  else
    echo "$NAME failed, was skipped, or is unavailable (status=$VALUE)." >&2
    STATUS=1
  fi
}
check_status "Current integration gate" "$(read_status "$WORKSPACE/.qa-current-integration-gate-status" "${QA_CURRENT_INTEGRATION_GATE_STATUS:-1}")"
check_status "Current E2E gate" "$(read_status "$WORKSPACE/.qa-current-e2e-gate-status" "${QA_CURRENT_E2E_GATE_STATUS:-1}")"
# The retained-agent contract and the performance smoke are aetheus-qa-extended's gates since
# PLAN-007 lot 2 (persist-extended-gates.sh); this pipeline no longer runs them.
if [ "$STATUS" = 0 ]; then
  echo "Advisory current validation gate: passing evidence recorded."
else
  echo "Advisory current validation gate: incomplete or failing evidence recorded for assurance grading."
fi
