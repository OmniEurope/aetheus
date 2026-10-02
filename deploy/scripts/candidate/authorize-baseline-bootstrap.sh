# SPDX-License-Identifier: EUPL-1.2
#
# Refuses a candidate that would bootstrap a delivery baseline without being told it may.
#
# It runs BEFORE CI on purpose: without it the chain spends an hour building and qualifying, then
# discovers at the deployment that there is no rollback-capable baseline to deploy over, and the
# operator has to choose under time pressure what they should have chosen at launch.
#
# This was 7 lines inlined in .pipeline/aetheus-candidate.yaml. The body is relocated unchanged apart
# from the parameter read, noted below.
#
# Inputs, all read from the environment (none positional):
#   DELIVERY_BASELINE_BOOTSTRAP  true (default) when no rollback-capable deployed baseline exists
#   ALLOW_BOOTSTRAP              the run's allowBootstrap parameter. It was written
#                                `${{ parameters.allowBootstrap }}` in the YAML, which the control
#                                plane expands before execution and a script cannot read, so the
#                                calling step passes it by name.
set -eu
if [ "${DELIVERY_BASELINE_BOOTSTRAP:-true}" = true ] \
  && [ "${ALLOW_BOOTSTRAP}" != true ]; then
  echo "No rollback-capable deployed baseline exists and allowBootstrap is false; refusing bootstrap before CI." >&2
  exit 1
fi
echo "Baseline authorization accepted before CI."
