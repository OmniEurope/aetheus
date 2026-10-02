# SPDX-License-Identifier: EUPL-1.2
#
# Restores and builds the opted-in example consumer against the Telemetry package this run produced,
# from a throwaway package cache and intermediate tree so nothing on the host can make it pass.
#
# This was 15 lines inlined in .pipeline/package-aetheus-telemetry.yaml. The body is relocated
# unchanged apart from PACKAGE_VERSION, noted below.
#
# Inputs, all read from the environment (none positional):
#   WORKSPACE        the run's checkout, holding .package-candidate/telemetry
#   PACKAGE_VERSION  the version this run packaged. It was written `$(...)` in the YAML, which the
#                    control plane substitutes in a `shell:` block; inside a script that same text is
#                    a shell command substitution, so the caller passes it by name.
set -eu
DOTNET="$(sh deploy/scripts/ensure-dotnet-sdk.sh)"
TEMP="$(mktemp -d)"
trap 'rm -rf "$TEMP"' EXIT HUP INT TERM
"$DOTNET" restore examples/optional-observability/OptionalObservabilityExample.csproj \
  --packages "$TEMP/packages" \
  -p:EnableAetheusTelemetry=true \
  -p:AetheusTelemetryVersion="${PACKAGE_VERSION}" \
  -p:BaseIntermediateOutputPath="$TEMP/obj/" \
  -p:RestoreAdditionalProjectSources="$WORKSPACE/.package-candidate/telemetry"
"$DOTNET" build examples/optional-observability/OptionalObservabilityExample.csproj \
  --configuration Release --no-restore \
  -p:EnableAetheusTelemetry=true \
  -p:AetheusTelemetryVersion="${PACKAGE_VERSION}" \
  -p:BaseOutputPath="$TEMP/bin/" -p:BaseIntermediateOutputPath="$TEMP/obj/"
