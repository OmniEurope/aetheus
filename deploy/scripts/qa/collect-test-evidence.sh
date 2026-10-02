# SPDX-License-Identifier: EUPL-1.2
#
# Copies the executed-test evidence out of the directory the teardown is about to destroy, and says
# how many files it actually preserved.
#
# It never fails the run over evidence collection, but it never pretends either: an empty directory
# is reported as such rather than passing silence off as a clean result.
#
# This was 12 lines inlined in .pipeline/aetheus-qa.yaml. The body is relocated unchanged.
#
# Inputs, all read from the environment (none positional):
#   WORKSPACE  the run's checkout, holding .qa-test-results and receiving .qa-evidence
set -eu
# Never fail the run over evidence collection, but never pretend either: an empty
# directory is reported as such instead of passing silence off as a clean result.
rm -rf "$WORKSPACE/.qa-evidence"
mkdir -p "$WORKSPACE/.qa-evidence"
if [ -d "$WORKSPACE/.qa-test-results" ]; then
  cp -a "$WORKSPACE/.qa-test-results/." "$WORKSPACE/.qa-evidence/"
fi
COUNT="$(find "$WORKSPACE/.qa-evidence" -type f | wc -l)"
echo "QA evidence files preserved: $COUNT."
find "$WORKSPACE/.qa-evidence" -type f -name '*.trx' -exec echo "TRX: {}" \;
