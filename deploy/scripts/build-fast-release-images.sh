# SPDX-License-Identifier: EUPL-1.2
#
# Pins the revision this fast deploy is allowed to ship, publishes the application on the host, and
# builds the two production images from that publish.
#
# The pinning is not decoration: a fast deploy skips the qualification chain, so the only thing
# standing between it and shipping an arbitrary tree is the refusal below when the checked-out
# revision does not match the revision the run was launched on.
#
# This was 27 lines inlined in .pipeline/aetheus-release-fast.yaml. The body is relocated unchanged
# apart from APP_VERSION, noted below.
#
# Inputs, all read from the environment (none positional):
#   BUILD_SOURCEVERSION  the revision the run was launched on; the checkout must match it
#   APP_VERSION          the version stamped into the publish and the backend image. It was written
#                        `$(...)` in the YAML, which the control plane substitutes in a `shell:`
#                        block; inside a script that same text is a shell command substitution, so
#                        the caller passes it by name.
set -eu
SOURCE_COMMIT="$(git rev-parse HEAD)"
case "${#SOURCE_COMMIT}" in 40|64) ;; *) echo "Resolved source revision has an invalid length."; exit 1 ;; esac
case "$SOURCE_COMMIT" in *[!0-9a-fA-F]*) echo "Resolved source revision is not hexadecimal."; exit 1 ;; esac
if [ -z "${BUILD_SOURCEVERSION:-}" ] || [ "$BUILD_SOURCEVERSION" != "$SOURCE_COMMIT" ]; then
  echo "Checked-out revision does not match BUILD_SOURCEVERSION; refusing an unpinned fast deploy." >&2
  exit 1
fi
docker --version
docker compose version

# Same builder as the CI (deploy/scripts/ci/package-application.sh), for two reasons that both cost
# minutes. A bare `docker build` runs on the daemon's default builder, which nobody maintains: the
# agent's storage maintenance only prunes its own named builder, so that cache grew unbounded and
# was trimmed by the daemon's own garbage collector between two runs - measured on this host as 14
# cached layers on 6 September, 6 the next day, 1 the day after, and a build that went from 10 to 19
# minutes on an unchanged Dockerfile. And the two builders share nothing, so the agent and EF-bundle
# stages the CI had just built for this very commit were rebuilt from scratch here every time.
BUILDER="${AETHEUS_BUILDX_BUILDER:-aetheus-$(hostname 2>/dev/null || echo agent)}"
sh deploy/scripts/ensure-buildx-builder.sh "$BUILDER"

# The host publish and the ef-bundle stage are run CONCURRENTLY, because neither needs the other and
# running them in series was most of this step's wall clock. Measured on release-fast 2326, a
# 1111 s image build:
#   0 -> 422 s   host publish (Aetheus.Back 209 s, Aetheus.Front 176 s, StaticServer 15 s)
#   422 -> 972 s Dockerfile.back, of which ef-bundle 16/16 alone was 512 s
#   972 -> 1111s Dockerfile.front
# ef-bundle COPYs only src/ and deploy/ - never .pipeline-publish/ - so it can start immediately,
# and it is the critical path. Overlapping the two turns 422 s + 512 s into max(422, 512).
#
# Aetheus.Back is compiled twice per release and that is deliberate, not an oversight: the host
# publish produces the runtime payload, while `dotnet ef migrations bundle` must compile the project
# again INSIDE this image, because it emits an apphost for the build host's OS. Producing the bundle
# on a Windows agent writes a PE executable that this Linux image copies, chmods, and then fails to
# run with no output at all. The second compile buys host independence; what it does not have to buy
# is the first compile's wall clock.
#
# The warm build starts AFTER the backend compile, no longer at time zero (2026-09-13). Both compile
# Aetheus.Back with every core they can get, so running them together only split the CPU between two
# multi-threaded compiles (release-fast 2355: 260 s for the host backend compile, 14 s on a dev
# machine). The frontend publish that follows is mostly the single-threaded WebAssembly trimming
# (177 s of its 265 s in 2355), which leaves the cores the EF-bundle compile needs. Same commands,
# same outputs; only the overlap moved.
DOTNET="$(sh deploy/scripts/ensure-dotnet-sdk.sh)"
DOTNET="$DOTNET" APP_VERSION="${APP_VERSION}" \
  sh deploy/scripts/publish-application.sh .pipeline-publish backend

WARM_LOG="$(mktemp)"
docker buildx build --builder "$BUILDER" \
  --target ef-bundle \
  --output type=cacheonly \
  --build-arg "APP_VERSION=${APP_VERSION}" \
  --build-arg "SOURCE_COMMIT=${SOURCE_COMMIT}" \
  --build-arg "CACHE_COHORT=shared" \
  --file deploy/docker/Dockerfile.back \
  . >"$WARM_LOG" 2>&1 &
WARM_PID="$!"
# Never leave the warm build running if the publish below fails and `set -e` ends the script.
trap 'kill "$WARM_PID" 2>/dev/null || true; rm -f "$WARM_LOG"' EXIT INT TERM

# Both Dockerfiles copy a host publish instead of compiling. This pipeline compiles nothing of its
# own, so it produces that publish here, exactly as CI does (the backend half ran above).
DOTNET="$DOTNET" APP_VERSION="${APP_VERSION}" \
  sh deploy/scripts/publish-application.sh .pipeline-publish frontend

# The warm build is an accelerator, never a verdict. If it failed, the full build below rebuilds the
# same stage from scratch and reports the real error with its own output; a green release is never
# declared on the strength of this one, so its failure is logged and not swallowed silently.
if wait "$WARM_PID"; then
  echo ">>> ef-bundle stage warmed while the host publish ran; the build below reuses it."
else
  echo ">>> Warming the ef-bundle stage failed; the full build will rebuild it and report why." >&2
  cat "$WARM_LOG" >&2
fi
trap - EXIT INT TERM
rm -f "$WARM_LOG"

# shellcheck source=deploy-identity.sh
. deploy/scripts/deploy-identity.sh
deploy_image_repos
sh deploy/scripts/buildx-build-load.sh --builder "$BUILDER" --load \
  --label "org.opencontainers.image.revision=${SOURCE_COMMIT}" \
  --build-arg "APP_VERSION=${APP_VERSION}" \
  --build-arg "SOURCE_COMMIT=${SOURCE_COMMIT}" \
  --build-arg "CACHE_COHORT=shared" \
  --file deploy/docker/Dockerfile.back \
  --tag "$BACK_IMAGE_REPO:${SOURCE_COMMIT}" .
sh deploy/scripts/buildx-build-load.sh --builder "$BUILDER" --load \
  --label "org.opencontainers.image.revision=${SOURCE_COMMIT}" \
  --file deploy/docker/Dockerfile.front \
  --tag "$FRONT_IMAGE_REPO:${SOURCE_COMMIT}" .
docker image inspect "$BACK_IMAGE_REPO:${SOURCE_COMMIT}" "$FRONT_IMAGE_REPO:${SOURCE_COMMIT}" >/dev/null
sh deploy/scripts/package-fast-release-artifacts.sh
