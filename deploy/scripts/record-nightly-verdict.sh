# SPDX-License-Identifier: EUPL-1.2
#
# Records the nightly qualification verdict from the grade files the security, quality and extension
# stages produced, against the revision this nightly rebuilt, together with the grade its assurance
# seal published.
#
# This was 8 lines inlined in .pipeline/aetheus-nightly.yaml. PLAN-007 lot 2 added the two extension
# summaries (aetheus-security-history, aetheus-qa-extended) and the sealed grade.
#
# Inputs, all read from the environment (none positional):
#   BUILD_SOURCEVERSION        the revision this nightly qualified
#   CANDIDATE_ASSURANCE_GRADE  the grade AssuranceSeal published; read by the recorder itself
set -eu
NODE="$(sh deploy/scripts/ensure-node-runtime.sh)"
"$NODE" deploy/scripts/record-nightly-qualification.mjs \
  .nightly-evidence/nightly-qualification.json \
  "$BUILD_SOURCEVERSION" \
  .nightly-grades/security \
  .nightly-grades/quality \
  .nightly-grades/security-history \
  .nightly-grades/qa-extended
