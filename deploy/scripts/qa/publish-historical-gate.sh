# SPDX-License-Identifier: EUPL-1.2
#
# Publishes the advisory gate over the historical compatibility suites: the V-1 integration and E2E
# runs against schema V. Advisory means a failure is graded into the assurance seal rather than
# failing this pipeline, so a candidate becomes undeployable while the run stays green.
#
# This was 32 lines inlined in .pipeline/aetheus-qa.yaml. The body is relocated unchanged.
#
# Inputs, all read from the environment (none positional):
#   WORKSPACE  the run's checkout, holding the per-suite status files this gate reads
set -eu
STATUS=0
read_status() {
  FILE="$1"
  FALLBACK="$2"
  if [ -s "$FILE" ]; then cat "$FILE"; else printf '%s' "$FALLBACK"; fi
}
SMOKE="$(read_status "$WORKSPACE/.qa-previous-smoke-gate-status" "${QA_PREVIOUS_SMOKE_GATE_STATUS:-1}")"
case "$SMOKE" in
  0) echo "Previous smoke gate passed." ;;
  1) echo "Previous smoke gate failed or is unavailable." >&2; STATUS=1 ;;
  *) echo "Previous smoke evidence status is invalid ($SMOKE)." >&2; exit 1 ;;
esac
for ENTRY in \
  "Previous integration gate:$(read_status "$WORKSPACE/.qa-previous-integration-gate-status" "${QA_PREVIOUS_INTEGRATION_GATE_STATUS:-1}")" \
  "Previous E2E gate:$(read_status "$WORKSPACE/.qa-previous-e2e-gate-status" "${QA_PREVIOUS_E2E_GATE_STATUS:-1}")"
do
  NAME="${ENTRY%%:*}"
  VALUE="${ENTRY##*:}"
  case "$VALUE" in 0|1) ;; *) echo "$NAME evidence status is unavailable or invalid ($VALUE)." >&2; exit 1 ;; esac
  if [ "$VALUE" = 0 ]; then
    echo "$NAME passed."
  else
    echo "$NAME failed, was skipped, or is unavailable (status=$VALUE)." >&2
    STATUS=1
  fi
done
if [ "$STATUS" = 0 ]; then
  echo "Advisory historical validation gate: passing evidence recorded."
else
  echo "Advisory historical validation gate: incomplete or failing evidence recorded for assurance grading."
fi
