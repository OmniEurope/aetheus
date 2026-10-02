# SPDX-License-Identifier: EUPL-1.2
#
# Merges every raw Cobertura report the test stages produced into the single report the quality gate
# reads, restricted to the three product assemblies.
#
# It refuses when it found no report at all, because an empty merge produces a valid file that
# reports zero of zero lines covered, which reads as a perfect score.
#
# The final sed strips a DOCTYPE that reportgenerator emits and the downstream parser rejects.
#
# This was 13 lines inlined in .pipeline/aetheus-quality.yaml. The body is relocated unchanged.
#
# Inputs: none. It runs in the run's checkout and reads whatever is under coverage/.
set -eu
DOTNET="$(sh deploy/scripts/ensure-dotnet-sdk.sh)"
NODE="$(sh deploy/scripts/ensure-node-runtime.sh)"
# CI's Cobertura files name its cleaned workspace. Rebase copies for this checkout while
# keeping the restored raw reports unchanged.
REBASED_REPORTS="$(mktemp -d "$(pwd)/.coverage-quality-rebased.XXXXXX")"
trap 'rm -rf "$REBASED_REPORTS"' EXIT
REPORTS="$("$NODE" deploy/scripts/rebase-quality-coverage-sources.mjs "$(pwd)" "$REBASED_REPORTS")"
"$DOTNET" tool restore
"$DOTNET" tool run reportgenerator -- \
  "-reports:$REPORTS" \
  "-targetdir:coverage-quality-merged" \
  "-reporttypes:Cobertura" \
  "-assemblyfilters:+Aetheus.Back;+Aetheus.Front;+Aetheus.Agent.Core" \
  "-verbosity:Warning"
test -s coverage-quality-merged/Cobertura.xml
sed -i '/^[[:space:]]*<!DOCTYPE coverage /d' coverage-quality-merged/Cobertura.xml
