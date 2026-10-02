# SPDX-License-Identifier: EUPL-1.2
#
# Picks the commit range the Git-history secret scan covers, and publishes it as
# AETHEUS_GITLEAKS_LOG_RANGE for the scanner step that follows.
#
# Both SHAs are validated for length and hexadecimal shape before the range is published, because the
# value goes on to build a revision-list argument: an unvalidated one is a command injection with the
# whole history as its payload.
#
# This was 24 lines inlined in .pipeline/aetheus-security.yaml. The body is relocated unchanged.
#
# Inputs, all read from the environment (none positional):
#   BUILD_SOURCEVERSION    the revision this run scans up to
#   AETHEUS_GITLEAKS_MODE  `release-range` (default) or `full`; anything else is refused
set -eu
case "${AETHEUS_GITLEAKS_MODE:-release-range}" in
  full)
    test "$(git rev-parse --is-shallow-repository)" = false
    BASE_SHA="$(git rev-list "${BUILD_SOURCEVERSION}" | tail -n 1)"
    echo "Full Gitleaks mode scans from the repository root commit."
    ;;
  release-range)
    RELEASE_TAG="$(git describe --tags --abbrev=0 --match 'v[0-9]*' "${BUILD_SOURCEVERSION}" 2>/dev/null || true)"
    if [ -n "$RELEASE_TAG" ]; then
      BASE_SHA="$(git rev-list -n 1 "$RELEASE_TAG")"
    else
      BASE_SHA="$(git rev-list "${BUILD_SOURCEVERSION}" | tail -n 1)"
      echo "No release tag is reachable; Gitleaks history falls back to the oldest fetched commit." >&2
    fi
    ;;
  *) echo "AETHEUS_GITLEAKS_MODE must be release-range or full." >&2; exit 1 ;;
esac
case "${#BASE_SHA}" in 40|64) ;; *) echo "Invalid Gitleaks baseline SHA length." >&2; exit 1 ;; esac
case "${#BUILD_SOURCEVERSION}" in 40|64) ;; *) echo "Invalid source SHA length." >&2; exit 1 ;; esac
case "$BASE_SHA" in ''|*[!0-9a-fA-F]*) echo "Invalid Gitleaks baseline SHA." >&2; exit 1 ;; esac
case "${BUILD_SOURCEVERSION}" in ''|*[!0-9a-fA-F]*) echo "Invalid source SHA." >&2; exit 1 ;; esac
echo "Gitleaks history range: ${BASE_SHA}..${BUILD_SOURCEVERSION}"
echo "##aetheus[setvariable name=AETHEUS_GITLEAKS_LOG_RANGE]${BASE_SHA}..${BUILD_SOURCEVERSION}"
