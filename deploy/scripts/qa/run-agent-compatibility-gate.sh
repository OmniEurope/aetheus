# SPDX-License-Identifier: EUPL-1.2
#
# Runs the retained N-1 agent against the backend N this run deployed, and turns its verdict into an
# advisory gate status. In bootstrap mode no retained agent contract artifact exists yet, and it
# writes an honest attestation saying so rather than a passing result.
#
# The exit code mapping is the point: 10 means the contract found something, which downgrades the
# gate to 1 without failing the step, while any other non-zero code is a real failure and propagates.
#
# This was 26 lines inlined in .pipeline/aetheus-qa.yaml. The body is relocated unchanged apart from
# QA_PORT_BACK, noted below.
#
# Inputs, all read from the environment (none positional):
#   WORKSPACE     the run's checkout, holding .qa-agent-bootstrap-mode, .qa-previous-commit,
#                 .qa-admin-pwd, .nminus1-agent-release and the .qa-test-results tree
#   QA_PORT_BACK  the run-scoped backend port this QA stack listens on. It was written `$(...)` in
#                 the YAML, which the control plane substitutes in a `shell:` block; inside a script
#                 that same text is a shell command substitution, so the caller passes it by name.
set -u
STATUS=0
(
  set -eu
  mkdir -p "$WORKSPACE/.qa-test-results/agent-compatibility"
  if [ "$(cat "$WORKSPACE/.qa-agent-bootstrap-mode")" = 1 ]; then
    printf '%s\n' '{"schema":1,"mode":"Bootstrap","previousAgentTested":false}' \
      > "$WORKSPACE/.qa-test-results/agent-compatibility/attestation.json"
    echo ">>> Honest agent compatibility bootstrap: no retained N-1 agent contract artifact exists yet."
  else
    PREVIOUS="$(cat "$WORKSPACE/.qa-previous-commit")"
    NODE="$(sh deploy/scripts/ensure-node-runtime.sh)"
    sh deploy/scripts/verify-nminus1-agent-contract.sh \
      "http://127.0.0.1:${QA_PORT_BACK}" \
      "$WORKSPACE/.qa-admin-pwd" \
      "$WORKSPACE/.nminus1-agent-release" \
      "$PREVIOUS" \
      "$WORKSPACE/.qa-test-results/agent-compatibility/attestation.json" \
      "$NODE"
  fi
) || STATUS=$?
case "$STATUS" in 0) ;; 10) STATUS=1 ;; *) exit "$STATUS" ;; esac
printf '%s\n' "$STATUS" > "$WORKSPACE/.qa-agent-compatibility-gate-status"
echo "##aetheus[setvariable name=QA_AGENT_COMPATIBILITY_GATE_STATUS]$STATUS"
echo "Agent compatibility findings status: $STATUS."
