# SPDX-License-Identifier: EUPL-1.2
#
# Restores, builds, publishes and then actually RUNS the opted-in example consumer against the
# package this run produced, and refuses unless the packaged asset is served with a non-empty body.
# A NuGet package that restores and compiles proves nothing about whether its static web asset
# reaches a browser, which is the only thing a consumer of this package cares about.
#
# This was 52 lines inlined in .pipeline/package-aetheus-web-analytics-dotnet.yaml. The body is
# relocated unchanged apart from PACKAGE_VERSION, noted below.
#
# Inputs, all read from the environment (none positional):
#   WORKSPACE        the run's checkout, holding .package-candidate/web-analytics-dotnet
#   PACKAGE_VERSION  the version this run packaged. It was written `$(...)` in the YAML, which the
#                    control plane substitutes in a `shell:` block; inside a script that same text is
#                    a shell command substitution, so the caller passes it by name.
set -eu
DOTNET="$(sh deploy/scripts/ensure-dotnet-sdk.sh)"
TEMP="$(mktemp -d)"
APP_PID=""
cleanup() {
  if [ -n "$APP_PID" ]; then
    kill "$APP_PID" 2>/dev/null || true
    wait "$APP_PID" 2>/dev/null || true
  fi
  rm -rf "$TEMP"
}
trap cleanup EXIT HUP INT TERM
"$DOTNET" restore examples/optional-observability/OptionalObservabilityExample.csproj \
  --packages "$TEMP/packages" \
  -p:EnableAetheusWebAnalytics=true \
  -p:AetheusWebAnalyticsVersion="${PACKAGE_VERSION}" \
  -p:BaseIntermediateOutputPath="$TEMP/obj/" \
  -p:RestoreAdditionalProjectSources="$WORKSPACE/.package-candidate/web-analytics-dotnet"
"$DOTNET" build examples/optional-observability/OptionalObservabilityExample.csproj \
  --configuration Release --no-restore \
  -p:EnableAetheusWebAnalytics=true \
  -p:AetheusWebAnalyticsVersion="${PACKAGE_VERSION}" \
  -p:BaseOutputPath="$TEMP/bin/" -p:BaseIntermediateOutputPath="$TEMP/obj/"
"$DOTNET" publish examples/optional-observability/OptionalObservabilityExample.csproj \
  --configuration Release --no-restore --output "$TEMP/publish" \
  -p:EnableAetheusWebAnalytics=true \
  -p:AetheusWebAnalyticsVersion="${PACKAGE_VERSION}" \
  -p:BaseIntermediateOutputPath="$TEMP/obj/"
# The pinned, checksummed runtime, not whatever the host happens to have: a simulator
# rebuild removed a hand-installed /usr/local/bin/node and every package step that calls
# node or npm bare died with "not found" (publisher run 1236).
NODE="$(sh deploy/scripts/ensure-node-runtime.sh)"
PATH="$(dirname "$NODE"):$PATH"
export PATH
PORT="$(node -e 'const net=require("node:net");const server=net.createServer();server.listen(0,"127.0.0.1",()=>{console.log(server.address().port);server.close();});')"
DOTNET_ROLL_FORWARD=Major ASPNETCORE_URLS="http://127.0.0.1:$PORT" \
  "$DOTNET" "$TEMP/publish/OptionalObservabilityExample.dll" \
  --contentRoot "$TEMP/publish" \
  > "$TEMP/consumer.log" 2>&1 &
APP_PID=$!
ASSET_URL="http://127.0.0.1:$PORT/_content/Aetheus.WebAnalytics/${PACKAGE_VERSION}/aetheus-web-analytics.js"
ATTEMPT=0
until node -e 'fetch(process.argv[1]).then(async response=>{const body=await response.text();if(!response.ok||body.length===0)process.exit(1);}).catch(()=>process.exit(1));' "$ASSET_URL"
do
  ATTEMPT=$((ATTEMPT + 1))
  if [ "$ATTEMPT" -ge 30 ]; then
    cat "$TEMP/consumer.log"
    echo "The packaged Web Analytics asset was not served with a non-empty body." >&2
    exit 1
  fi
  sleep 1
done
