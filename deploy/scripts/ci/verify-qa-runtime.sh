# SPDX-License-Identifier: EUPL-1.2
#
# Refuses to publish a QaRuntime artifact that is missing a file the QA pipeline reads: the
# integration test assembly and its restore graph, the agent downloads README the backend serves,
# and the four contracts the later stages check. Every missing file is named, not only the first
# one, so a partial build is diagnosed in one run.
#
# Takes no input: the paths are relative to the workspace root the step runs in.
set -eu
MISSING=0
for REQUIRED_FILE in \
  tests/Aetheus.Back.IntegrationTests/bin/Release/net10.0/Aetheus.Back.IntegrationTests.dll \
  tests/Aetheus.Back.IntegrationTests/obj/project.assets.json \
  src/Aetheus.Back/wwwroot/downloads/README.md \
  .pipeline-artifacts/qa-rollback-contract \
  .pipeline-artifacts/source-commit \
  .pipeline-artifacts/delivery-contract.json \
  .pipeline-artifacts/artifact-provenance.json
do
  if [ ! -s "$REQUIRED_FILE" ]; then
    echo "QA runtime is incomplete: $REQUIRED_FILE is missing or empty." >&2
    MISSING=1
  fi
done
exit "$MISSING"
