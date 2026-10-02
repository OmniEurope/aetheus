# SPDX-License-Identifier: EUPL-1.2
#
# Turns the two gates aetheus-qa-extended runs (the retained N-1 agent contract and the bounded
# response-time smoke) into the run output variables the nightly's assurance seal reads. It is the
# extension's counterpart of persist-compatibility-mode.sh, which since PLAN-007 lot 2 publishes only
# what aetheus-qa itself measures: two pipelines publishing the same name to one parent would leave
# the value to whichever finished last.
#
# A gate that ran and failed is graded here rather than failing the pipeline, so the verdict must be
# honest: a missing status file reads as a failure (1), never as a pass.
#
# Inputs, all read from the environment (none positional):
#   WORKSPACE  the run's checkout, holding the per-gate status files, .qa-agent-bootstrap-mode,
#              .qa-previous-commit and the .qa-test-results tree
#
# Publishes QA_AGENT_COMPATIBILITY_GATE_STATUS, AgentCompatibilityMode, PreviousAgentTested,
# AgentCompatibilityVerdict and QA_PERFORMANCE_GATE_STATUS.
set -eu
normalize_status() {
  case "$1" in 0) printf '%s' 0 ;; *) printf '%s' 1 ;; esac
}
read_status() {
  if [ -s "$1" ]; then cat "$1"; else printf '%s' 1; fi
}
AGENT_COMPATIBILITY="$(normalize_status "$(read_status "$WORKSPACE/.qa-agent-compatibility-gate-status")")"
PERFORMANCE="$(normalize_status "$(read_status "$WORKSPACE/.qa-performance-gate-status")")"
echo "##aetheus[setvariable name=QA_AGENT_COMPATIBILITY_GATE_STATUS]$AGENT_COMPATIBILITY"
echo "##aetheus[setvariable name=QA_PERFORMANCE_GATE_STATUS]$PERFORMANCE"
# `PreviousAgentTested` names the revision this run actually exercised.
if [ "$(cat "$WORKSPACE/.qa-agent-bootstrap-mode")" = 1 ]; then
  echo "##aetheus[setvariable name=AgentCompatibilityMode]Bootstrap"
  echo "##aetheus[setvariable name=PreviousAgentTested]false"
else
  PREVIOUS="$(cat "$WORKSPACE/.qa-previous-commit")"
  test -n "$PREVIOUS"
  echo "##aetheus[setvariable name=AgentCompatibilityMode]NMinusOne"
  echo "##aetheus[setvariable name=PreviousAgentTested]$PREVIOUS"
fi
if [ "$AGENT_COMPATIBILITY" = 0 ]; then
  test -s "$WORKSPACE/.qa-test-results/agent-compatibility/attestation.json"
  echo "##aetheus[setvariable name=AgentCompatibilityVerdict]Verified"
else
  echo "##aetheus[setvariable name=AgentCompatibilityVerdict]Degraded"
fi
echo "Extended gates: agent compatibility status $AGENT_COMPATIBILITY, performance status $PERFORMANCE."
